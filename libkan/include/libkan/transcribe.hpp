#pragma once
// Offline transcription helper (NOT real-time): runs the validated blind
// note stream over a recording and collects one record per struck note
// into a chart draft. Chart semantics (chart-transcription-evaluation.md
// s3b): onsets anchor to the AUDIO attack; durations are bias-corrected
// (release-bias constant measured on 1.5M+ notes) with the raw end and
// `end_faded` preserved for the editor. The facts are measurements, not
// verdicts — a transcribed chart is a DRAFT for human review.
#include "libkan/note_stream.hpp"
#include "libkan/instrument_profile.hpp"

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <filesystem>
#include <string>
#include <vector>

namespace kan {

// Small filesystem helpers for the separator adapters (header-only).
inline void fs_create_all(const std::string& p) {
    std::error_code ec;
    std::filesystem::create_directories(p, ec);
}
inline bool fs_exists(const std::string& p) {
    std::error_code ec;
    return std::filesystem::exists(p, ec);
}
inline void fs_rename(const std::string& a, const std::string& b) {
    std::error_code ec;
    std::filesystem::rename(a, b, ec);
}

/// ---- Layer 0: source separation (single stack: GGUF + Vulkan) --------
/// One backend, chosen deliberately (user decision 2026-10-12): the GGUF
/// RoFormer runtime. GGML backends cover Vulkan (GPU), CPU (low-budget
/// machines) and Metal (macOS) in the SAME binary with NO torch at
/// runtime — one path serves everyone and unused code does not
/// accumulate. Locate the binary/model via KAN_ROFORMER_CLI /
/// KAN_ROFORMER_GGUF (or PATH + models/6stem.gguf default).
inline std::string find_separator() {
    const char* cli = std::getenv("KAN_ROFORMER_CLI");
    const std::string bs = (cli != nullptr && *cli != '\0') ? cli : "bs_roformer-cli";
    const char* gguf = std::getenv("KAN_ROFORMER_GGUF");
    const std::string model = (gguf != nullptr && *gguf != '\0')
        ? gguf : std::string("models/6stem.gguf");
    std::error_code ec;
    if (std::filesystem::exists(model, ec)) return "bsroformer:" + bs + ":" + model;
    return "";
}

/// Run the separator: produces <out_dir>/{bass,drums,guitar,other,piano,
/// vocals}.wav (official stem order normalized). Empty on failure.
inline std::string separate_stems(const std::string& audio,
                                  const std::string& out_dir,
                                  const std::string& stems = "6") {
    (void)stems;
    const std::string sep = find_separator();
    if (sep.empty()) return "";
    const size_t c1 = sep.find(':', 11);
    const std::string cli = sep.substr(11, c1 - 11);
    const std::string gguf = sep.substr(c1 + 1);
    fs_create_all(out_dir);
    const std::string stem_out = out_dir + "/roformer_out.wav";
    const std::string cmd = cli + " '" + gguf + "' '" + audio + "' '" + stem_out + "'";
    std::printf("[separate] %s\n", cmd.c_str());
    if (std::system(cmd.c_str()) != 0) return "";
    static const char* N[6] = {"bass", "drums", "other", "vocals", "guitar", "piano"};
    const std::string dir = out_dir + "/roformer";
    fs_create_all(dir);
    bool any = false;
    for (int i = 0; i < 6; ++i) {
        const std::string src = out_dir + "/roformer_out_stem_" + std::to_string(i) + ".wav";
        if (fs_exists(src)) {
            fs_rename(src, dir + "/" + N[i] + ".wav");
            any = true;
        }
    }
    return any ? dir : std::string("");
}

/// Find a usable audio downloader for URLs (yt-dlp on PATH, user install,
/// python module, or youtube-dl). Empty string = none available.
inline std::string find_audio_downloader() {
    if (std::system("command -v yt-dlp > /dev/null 2>&1") == 0) return "yt-dlp";
    if (std::system("test -x \"$HOME/.local/bin/yt-dlp\"") == 0) {
        return "\"$HOME/.local/bin/yt-dlp\"";
    }
    if (std::system("python3 -m yt_dlp --version > /dev/null 2>&1") == 0) {
        return "python3 -m yt_dlp";
    }
    if (std::system("command -v youtube-dl > /dev/null 2>&1") == 0) return "youtube-dl";
    return "";
}

/// One struck note as transcribed from audio.
struct TranscribedNote {
    float onset_ms = 0.0f;
    float end_ms = 0.0f;      // measured end (raw)
    float dur_ms = 0.0f;      // bias-corrected duration (chart-ready)
    int midi = -1;            // trajectory center, rounded (-1 = unresolved)
    float cents = 0.0f;
    float dyn_db = -120.0f;
    float vib_rate = 0.0f, vib_depth = 0.0f;
    bool end_faded = false;   // end inferred (decay/pedal), not a release
    bool transition = false;  // legato boundary (no strike)
};

/// Blind transcription of one mono buffer. `profile` selects the
/// instrument expectations (I10). Deterministic: same audio, same chart.
inline std::vector<TranscribedNote> transcribe_audio(const float* sig, size_t n,
                                                     float sr,
                                                     const InstrumentProfile& profile) {
    NoteDescriber desc(sr, profile);
    NoteEventStream stream(1024);
    desc.subscribe(stream);

    struct NRow {
        uint32_t id = 0;
        TranscribedNote t;
        bool have_pitch = false;
    };
    std::vector<NRow> out;
    auto process_events = [&]() {
        NoteEvent batch[64];
        for (;;) {
            const size_t got = stream.pop(batch, 64);
            if (got == 0) break;
            for (size_t k = 0; k < got; ++k) {
                const NoteEvent& e = batch[k];
                NRow* r = nullptr;
                for (auto& q : out) {
                    if (q.id == e.note_id) { r = &q; break; }
                }
                if (r == nullptr && e.kind == NoteKind::Hit) {
                    out.push_back(NRow{});
                    r = &out.back();
                    r->id = e.note_id;
                    r->t.onset_ms = e.u.hit.onset_ms;
                    if (e.u.hit.midi > 0.0f) {
                        r->t.midi = int(e.u.hit.midi + 0.5f);
                    }
                }
                if (r == nullptr) continue;
                switch (e.kind) {
                case NoteKind::Pitch:
                    if (e.stage == PitchStage::Final && e.u.pitch.midi > 0.0f) {
                        r->t.midi = int(e.u.pitch.midi + 0.5f);
                        r->t.cents = e.u.pitch.cents;
                        r->have_pitch = true;
                    }
                    break;
                case NoteKind::Expression:
                    r->t.dyn_db = e.u.expression.dynamics_db;
                    r->t.vib_rate = e.u.expression.vib_rate_hz;
                    r->t.vib_depth = e.u.expression.vib_depth_c;
                    break;
                case NoteKind::Close:
                    r->t.end_ms = e.u.close.end_ms;
                    break;
                default:
                    break;
                }
            }
        }
    };

    constexpr size_t kBlock = 128;
    for (size_t off = 0; off + kBlock <= n; off += kBlock) {
        desc.process_block(sig + off, kBlock);
        while (desc.finalize_due()) {}
        process_events();
    }
    desc.flush();
    process_events();

    std::vector<TranscribedNote> notes;
    for (auto& r : out) {
        if (r.t.end_ms <= 0.0f) r.t.end_ms = r.t.onset_ms;
        const float raw = r.t.end_ms - r.t.onset_ms;
        // Bias-corrected chart duration (s3b): the measured end includes
        // ~210 ms of release/padding; clamp below at a 20 ms minimum.
        float dur = raw - 210.0f;
        if (dur < 20.0f) dur = std::min(raw, 20.0f);
        r.t.dur_ms = dur;
        if (r.t.midi < 0) r.t.midi = -1;
        notes.push_back(r.t);
    }
    std::sort(notes.begin(), notes.end(), [](const TranscribedNote& a, const TranscribedNote& b) {
        return a.onset_ms < b.onset_ms;
    });
    return notes;
}

/// Musical note name ("C5") for a MIDI number.
inline std::string note_name(int midi) {
    static const char* NAMES[12] = {"C", "C#", "D", "D#", "E", "F",
                                    "F#", "G", "G#", "A", "A#", "B"};
    if (midi < 0) return "?";
    char buf[16];
    std::snprintf(buf, sizeof(buf), "%s%d", NAMES[midi % 12], midi / 12 - 1);
    return std::string(buf);
}

/// Chart text (parse_score-compatible: `onset_ms duration_ms note`; the
/// annotation after '#' is documentation for the editor, ignored by the
/// parser).
inline std::string chart_text(const std::vector<TranscribedNote>& notes) {
    std::string out = "# transcribed chart (draft): onset dur note  # facts\n";
    for (const auto& n : notes) {
        char line[256];
        std::snprintf(line, sizeof(line), "%.0f %.0f %s  # dyn %.0f dB",
                      double(n.onset_ms), double(n.dur_ms), note_name(n.midi).c_str(),
                      double(n.dyn_db));
        out += line;
        if (n.vib_rate > 0.5f) {
            std::snprintf(line, sizeof(line), " vibrato %.1fHz/%.0fc",
                          double(n.vib_rate), double(n.vib_depth));
            out += line;
        }
        if (n.end_faded) out += " end-faded";
        out += "\n";
    }
    return out;
}

/// Simple human-readable log (the "visualizarlo de forma simple" view):
/// a timeline table with one line per note.
inline std::string chart_log(const std::vector<TranscribedNote>& notes) {
    std::string out = "transcription log — one row per struck note (draft)\n";
    out += "     t_start     t_end    dur   note   dyn     vib      flags\n";
    for (const auto& n : notes) {
        char line[256];
        std::snprintf(line, sizeof(line), "%9.0f %9.0f %6.0f  %-5s %5.0f dB  %s%s%s\n",
                      double(n.onset_ms), double(n.end_ms), double(n.dur_ms),
                      note_name(n.midi).c_str(), double(n.dyn_db),
                      n.vib_rate > 0.5f ? "vibrato " : "",
                      n.end_faded ? "end-faded " : "",
                      n.midi < 0 ? "UNRESOLVED" : "");
        out += line;
    }
    return out;
}

/// Minimal Standard MIDI File writer (format 0): the transcribed chart as
/// a performance sketch. Velocity is derived from the measured dynamics.
inline std::vector<uint8_t> chart_midi(const std::vector<TranscribedNote>& notes,
                                       int tpq = 480) {
    std::vector<uint8_t> track;
    auto vlq = [&track](uint32_t v) {
        uint8_t buf[5];
        int n = 0;
        buf[n++] = uint8_t(v & 0x7F);
        while (v >>= 7) buf[n++] = uint8_t(0x80 | (v & 0x7F));
        while (n--) track.push_back(buf[n]);
    };
    // 500 ms per quarter: tick = ms * tpq / 500.
    std::vector<std::pair<uint64_t, std::array<uint8_t, 3>>> ev;
    for (const auto& n : notes) {
        if (n.midi < 0 || n.midi > 127) continue;
        const uint64_t on = uint64_t(n.onset_ms * double(tpq) / 500.0 + 0.5);
        const uint64_t off = uint64_t((n.onset_ms + n.dur_ms) * double(tpq) / 500.0 + 0.5);
        int vel = int(64.0f + 2.0f * n.dyn_db); // ~ -32..0 dB -> 0..127
        vel = std::clamp(vel, 30, 120);
        ev.push_back({on, {0x90, uint8_t(n.midi), uint8_t(vel)}});
        ev.push_back({std::max(off, on + 1), {0x80, uint8_t(n.midi), 0}});
    }
    std::stable_sort(ev.begin(), ev.end(), [](const auto& a, const auto& b) {
        return a.first < b.first;
    });
    uint64_t prev = 0;
    for (const auto& e : ev) {
        vlq(uint32_t(e.first - prev));
        prev = e.first;
        track.insert(track.end(), e.second.begin(), e.second.end());
    }
    vlq(0);
    track.insert(track.end(), {0xFF, 0x2F, 0x00});

    std::vector<uint8_t> f = {'M', 'T', 'h', 'd', 0, 0, 0, 6,
                              0, 0, 0, 1,
                              uint8_t(tpq >> 8), uint8_t(tpq & 0xFF)};
    const uint32_t tl = uint32_t(track.size());
    const uint8_t th[8] = {'M', 'T', 'r', 'k',
                           uint8_t(tl >> 24), uint8_t(tl >> 16),
                           uint8_t(tl >> 8), uint8_t(tl)};
    f.insert(f.end(), th, th + 8);
    f.insert(f.end(), track.begin(), track.end());
    return f;
}

} // namespace kan
