#pragma once
// Offline transcription helper (NOT real-time): runs the validated blind
// note stream over a recording and collects one record per struck note
// into a chart draft. Chart semantics (chart-transcription-evaluation.md
// s3b): onsets anchor to the AUDIO attack; durations are bias-corrected
// (release-bias constant measured on 1.5M+ notes) with the raw end and
// `end_faded` preserved for the editor. The facts are measurements, not
// verdicts; a transcribed chart is a DRAFT for human review.
#include "libkan/verbose.hpp"
#include "libkan/note_stream.hpp"
#ifdef KAN_HAVE_ROFORMER_RUNTIME
#include "libkan/roformer_runtime.hpp"
#endif
#include "libkan/instrument_profile.hpp"
#ifdef KAN_HAVE_BASIC_PITCH
#include "libkan/basic_pitch.hpp"
#endif

#include <algorithm>
#include <array>
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
/// runtime: one path serves everyone and unused code does not
/// accumulate. Locate the binary/model via KAN_ROFORMER_CLI /
/// KAN_ROFORMER_GGUF (or PATH + models/6stem.gguf default).
// ---- progress protocol (game-facing, user request 2026-10-12) ---------
// The game shows a progress notice for downloads AND processing. Two
// channels, same numbers:
//   1. in-process callback:     set_progress(fn, user)   (the game)
//   2. machine line on stdout:  PROGRESS <phase> <percent>
//      when no callback listens (a run from a terminal), or KAN_VERBOSE
// Phases: "download", "separate". Percent 0..100.
using ProgressFn = void (*)(void* user, const char* phase, int percent);
inline ProgressFn g_progress_fn = nullptr;
inline void* g_progress_user = nullptr;
inline void set_progress(ProgressFn f, void* u = nullptr) {
    g_progress_fn = f;
    g_progress_user = u;
}
inline void report_progress(const char* phase, int percent) {
    if (percent < 0) percent = 0;
    if (percent > 100) percent = 100;
    if (g_progress_fn != nullptr) g_progress_fn(g_progress_user, phase, percent);
    if (g_progress_fn == nullptr || verbose()) {
        std::printf("PROGRESS %s %d\n", phase, percent);
        std::fflush(stdout);
    }
}

/// Run a command, scanning its output for "NN%" progress tokens and
/// reporting them under `phase`. Returns true on exit code 0.
inline bool run_with_progress(const std::string& cmd, const char* phase) {
    // Force unbuffered child output (stdbuf) so NN% tokens arrive live:
    // block-buffered children would flush their whole progress at exit and
    // the game would only ever see 100%. stdbuf is a POSIX tool and
    // `command -v` a POSIX probe, on Windows (cmd.exe) skip both; the
    // children there (curl.exe, yt-dlp) write progress unbuffered already.
    std::string full = cmd + " 2>&1";
#ifndef _WIN32
    if (std::system("command -v stdbuf > /dev/null 2>&1") == 0) {
        full = "stdbuf -o0 -e0 " + full; // -o0: unbuffered (\r bars need it)
    }
#endif
#ifdef _WIN32
    FILE* p = _popen(full.c_str(), "r");
#else
    FILE* p = popen(full.c_str(), "r");
#endif
    if (p == nullptr) return false;
    char buf[512];
    int last = -1;
    while (std::fgets(buf, int(sizeof(buf)), p) != nullptr) {
        for (char* q = buf; *q != '\0'; ++q) {
            if (*q != '%') continue;
            char* st = q;
            while (st > buf && ((st[-1] >= '0' && st[-1] <= '9') || st[-1] == '.')) --st;
            if (st == q) continue;
            const int pct = int(std::atof(st));
            if (pct != last && pct >= 0 && pct <= 100) {
                last = pct;
                report_progress(phase, pct);
            }
        }
    }
#ifdef _WIN32
    const int rc = _pclose(p);
#else
    const int rc = pclose(p);
#endif
    if (rc == 0) report_progress(phase, 100); // failure never reports 100
    return rc == 0;
}

/// Model acquisition (user decision 2026-10-12): if the model is not
/// found, download it automatically. Initially from the community
/// official-ish repos; eventually from OUR distribution (availability +
/// compatibility control) via KAN_MODEL_URL. One model, three grades:
/// KAN_ROFORMER_GRADE = f16 (default, max quality) | q8 (opt-in) |
/// q4 (fastest CPU, none public yet).
///
/// Default grade = f16 (owner decision 2026-11-11, docs/release-credits.md
/// GAP 7): f16 is the only grade whose source declares a licence (MIT,
/// studio409/BSRoformer-SW-6Stem-GGUF, a GGUF conversion of the MIT ONNX
/// upstream). The q8 grade (FractalSurfer/BS-RoFormer-SW-6stem) declares
/// no licence on its card and is an explicit opt-in only
/// (KAN_ROFORMER_GRADE=q8).
inline const char* roformer_default_grade() { return "f16"; }

/// Pure URL mapping (unit-tested, no env and no network). An empty
/// override means "no self-hosting"; an empty result means "this grade
/// has no public source" (q4: verified 404).
inline std::string roformer_model_url_for(const std::string& grade,
                                          const std::string& override_url) {
    if (!override_url.empty()) return override_url; // self-hosting wins
    if (grade == "f16") {
        return "https://huggingface.co/studio409/BSRoformer-SW-6Stem-GGUF"
               "/resolve/main/bs_roformer_sw_6stem-F16.gguf";
    }
    if (grade == "q8") {
        return "https://huggingface.co/FractalSurfer/BS-RoFormer-SW-6stem"
               "/resolve/main/gguf/BS-RoFormer-SW-6stem-q8_0.gguf";
    }
    return "";
}

/// Model URL for `grade` with the environment's KAN_MODEL_URL override.
inline std::string roformer_model_url(const std::string& grade) {
    const char* url = std::getenv("KAN_MODEL_URL");
    return roformer_model_url_for(grade, (url != nullptr) ? url : "");
}

/// ---- shell command construction (pure; unit-tested, no network) ------
/// std::system() runs cmd.exe on Windows and /bin/sh elsewhere. Quoting
/// differs: single quotes are literal characters in cmd.exe (it needs
/// double quotes), and the PATH probe is `command -v` on POSIX but `where`
/// on Windows (Windows 10 1803+ ships curl.exe; wget is not standard).
/// `windows` selects the shell; the runtime #ifdefs pick the real one.

/// Quote one argument for the target shell.
inline std::string shell_quote(const std::string& s, bool windows) {
    std::string out;
    if (windows) {
        out = "\""; // cmd.exe: double quotes, backslash-escaped inner quotes
        for (char c : s) {
            if (c == '"') out += '\\'; // MSVCRT argv rules
            out += c;
        }
        out += '"';
    } else {
        out = "'"; // POSIX: single quotes quote everything
        out += s;
        out += "'";
    }
    return out;
}

/// Probe that answers "is this tool installed?" in the target shell.
inline std::string tool_probe_cmd(const std::string& tool, bool windows) {
    return windows ? "where " + tool + " > NUL 2>&1"
                   : "command -v " + tool + " > /dev/null 2>&1";
}

/// Download `url` to `path` with curl (both platforms) or wget (POSIX).
inline std::string curl_download_cmd(const std::string& path, const std::string& url,
                                     bool windows) {
    return std::string(windows ? "curl.exe" : "curl") +
           " -L --fail --progress-bar -o " + shell_quote(path, windows) +
           " " + shell_quote(url, windows);
}
inline std::string wget_download_cmd(const std::string& path, const std::string& url,
                                     bool windows) {
    return std::string(windows ? "wget.exe" : "wget") +
           " -q --show-progress -O " + shell_quote(path, windows) +
           " " + shell_quote(url, windows);
}

/// Find the GGUF model, downloading it to models/ if missing. "" on fail.
inline std::string ensure_roformer_model() {
    std::error_code ec;
    const char* gguf = std::getenv("KAN_ROFORMER_GGUF");
    if (gguf != nullptr && *gguf != '\0' && std::filesystem::exists(gguf, ec)) {
        return gguf;
    }
    const char* grade_env = std::getenv("KAN_ROFORMER_GRADE");
    std::string grade = (grade_env != nullptr && *grade_env != '\0')
        ? grade_env : roformer_default_grade();
    std::string url = roformer_model_url(grade);
    if (url.empty()) {
        // This grade has no public source (q4: verified 404). Fall back
        // to the default grade; only KAN_MODEL_URL can host another one.
        std::printf("[model] no public %s grade yet - using the default %s "
                    "(set KAN_MODEL_URL to host one)\n",
                    grade.c_str(), roformer_default_grade());
        grade = roformer_default_grade();
        url = roformer_model_url(grade);
    }
    fs_create_all("models");
    const std::string path = "models/6stem_" + grade + ".gguf";
    if (std::filesystem::exists(path, ec)) return path;
    std::printf("[model] downloading %s model...\n", grade.c_str());
#ifdef _WIN32
    // Windows 10 (1803+) ships curl.exe; cmd.exe has no `command -v` and no
    // wget by default, so probe with `where` and quote for cmd.exe.
    if (std::system(tool_probe_cmd("curl.exe", true).c_str()) != 0) {
        std::fprintf(stderr, "[model] need curl.exe to download "
                             "(ships with Windows 10 1803+)\n");
        return "";
    }
    const std::string cmd = curl_download_cmd(path, url, /*windows=*/true);
#else
    std::string cmd = curl_download_cmd(path, url, /*windows=*/false);
    if (std::system(tool_probe_cmd("curl", false).c_str()) != 0) {
        cmd = wget_download_cmd(path, url, /*windows=*/false);
        if (std::system(tool_probe_cmd("wget", false).c_str()) != 0) {
            std::fprintf(stderr, "[model] need curl or wget to download\n");
            return "";
        }
    }
#endif
    if (!run_with_progress(cmd, "download") || !std::filesystem::exists(path, ec)) {
        std::fprintf(stderr, "[model] download failed (set KAN_MODEL_URL "
                             "or KAN_ROFORMER_GGUF)\n");
        return "";
    }
    std::printf("[model] ready: %s\n", path.c_str());
    return path;
}

inline std::string find_separator() {
#ifdef KAN_HAVE_ROFORMER_RUNTIME
    // Official flow (editor-pipeline s7b): the runtime is compiled in.
    // Probe the RUNTIME, not just the model. No installed separator needed.
    {
        const std::string model = ensure_roformer_model();
        if (!model.empty() && roformer_runtime_available()) {
            return "runtime:" + model;
        }
    }
#endif
    // Developer tooling fallback (external CLI, provisional per s7b).
    const char* cli = std::getenv("KAN_ROFORMER_CLI");
    const std::string bs = (cli != nullptr && *cli != '\0') ? cli : "bs_roformer-cli";
    const std::string model = ensure_roformer_model();
    if (!model.empty()) return "bsroformer:" + bs + ":" + model;
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
    if (sep.rfind("runtime:", 0) == 0) {
#ifdef KAN_HAVE_ROFORMER_RUNTIME
        const std::string model = sep.substr(8);
        const std::string dir = out_dir + "/roformer";
        fs_create_all(dir);
        if (verbose()) std::printf("[separate] in-process runtime: %s\n", model.c_str());
        const bool okrun = roformer_separate_wav(
            audio, dir, model, [](float p) {
                report_progress("separate", int(p * 100.0f + 0.5f));
            });
        return okrun ? dir : std::string("");
#else
        return "";
#endif
    }
    const size_t c1 = sep.find(':', 11);
    const std::string cli = sep.substr(11, c1 - 11);
    const std::string gguf = sep.substr(c1 + 1);
    fs_create_all(out_dir);
    const std::string stem_out = out_dir + "/roformer_out.wav";
    const std::string cmd = cli + " '" + gguf + "' '" + audio + "' '" + stem_out + "'";
    if (verbose()) std::printf("[separate] %s\n", cmd.c_str());
    if (!run_with_progress(cmd, "separate")) return "";
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
#ifdef _WIN32
    // cmd.exe: `where` is the PATH probe; the Python launcher `py` and
    // `python` host the yt_dlp module (POSIX probes python3).
    if (std::system(tool_probe_cmd("yt-dlp.exe", true).c_str()) == 0) return "yt-dlp";
    if (std::system("py -m yt_dlp --version > NUL 2>&1") == 0) return "py -m yt_dlp";
    if (std::system("python -m yt_dlp --version > NUL 2>&1") == 0) return "python -m yt_dlp";
    if (std::system(tool_probe_cmd("youtube-dl.exe", true).c_str()) == 0) return "youtube-dl";
    return "";
#else
    if (std::system("command -v yt-dlp > /dev/null 2>&1") == 0) return "yt-dlp";
    if (std::system("test -x \"$HOME/.local/bin/yt-dlp\"") == 0) {
        return "\"$HOME/.local/bin/yt-dlp\"";
    }
    if (std::system("python3 -m yt_dlp --version > /dev/null 2>&1") == 0) {
        return "python3 -m yt_dlp";
    }
    if (std::system("command -v youtube-dl > /dev/null 2>&1") == 0) return "youtube-dl";
    return "";
#endif
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
    int last_pct = -1;
    for (size_t off = 0; off + kBlock <= n; off += kBlock) {
        desc.process_block(sig + off, kBlock);
        while (desc.finalize_due()) {}
        process_events();
        if (n > 0) {
            const int pct = int(100.0 * double(off) / double(n));
            if (pct / 5 != last_pct / 5) {
                last_pct = pct;
                report_progress("transcribe", pct);
            }
        }
    }
    desc.flush();
    process_events();
    report_progress("transcribe", 100);

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

#ifdef KAN_HAVE_BASIC_PITCH
/// Neural transcription (Basic Pitch, owner decision 2026-10-15) into the
/// same chart draft records as transcribe_audio. The profile bounds the
/// range and says whether the instrument plays one note at a time
/// (`max_simultaneous_notes == 1`: harmonics reported as notes are
/// dropped). Ends are the model's note ends: no release bias to correct.
/// `dyn_db`, vibrato and `transition` are not measured here.
inline std::vector<TranscribedNote> transcribe_audio_neural(const float* sig, size_t n, float sr,
                                                            const InstrumentProfile& profile) {
    report_progress("transcribe", 0);
    const basic_pitch::Posteriorgrams post = basic_pitch::infer(sig, n, sr);
    report_progress("transcribe", 90);
    auto hz_to_midi = [](float hz) { return int(std::lround(69.0 + 12.0 * std::log2(double(hz) / 440.0))); };
    basic_pitch::NoteOptions opt;
    opt.monophonic = profile.max_simultaneous_notes == 1;
    opt.min_midi = hz_to_midi(profile.min_freq_hz);
    opt.max_midi = hz_to_midi(profile.max_freq_hz);
    std::vector<TranscribedNote> notes;
    for (const basic_pitch::Note& b : basic_pitch::notes(post, opt)) {
        TranscribedNote t;
        t.onset_ms = float(b.start_s * 1000.0);
        t.end_ms = float(b.end_s * 1000.0);
        t.dur_ms = t.end_ms - t.onset_ms;
        t.midi = b.midi;
        t.cents = b.cents;
        notes.push_back(t);
    }
    report_progress("transcribe", 100);
    return notes;
}
#endif

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
    std::string out = "transcription log: one row per struck note (draft)\n";
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
