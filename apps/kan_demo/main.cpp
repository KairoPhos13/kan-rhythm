// kan_demo — live real-instrument demo for libkan (Milestone 1 pipeline).
//
// Feeds the microphone through the full detection stack — stage 0
// preconditioning, fast onset, profile-driven transient blanking, harmonic
// pitch confirmation — and shows live rhythm + intonation feedback in the
// terminal. Built for "grab an ocarina and try it" sessions; see README.md
// for what is and is not covered yet.
//
// Modes:
//   default      live tuner: onset flash + note + cents + stability
//   --expect N   score-informed: bounded-matcher confirmation vs note N
//   --calibrate  2 s reference note -> global detuning Δc readout
//   --selftest   no audio: synthesized ocarina note through the pipeline
//
// Real-time safety: the audio callback only pushes into the lock-free ring
// buffer (CONTRIBUTING §3); all analysis runs on the consumer thread.
#define MINIAUDIO_IMPLEMENTATION
#include "miniaudio.h"

#include "libkan/ring_buffer.hpp"
#include "libkan/dsp_detector.hpp"
#include "libkan/instrument_profile.hpp"
#include "libkan/precondition.hpp"
#include "libkan/bounded_pitch.hpp"
#include "libkan/calibration.hpp"
#include "libkan/technique.hpp"
#include "libkan/note_stream.hpp"
#ifdef KAN_DEMO_HAVE_BACF
#include "libkan/bacf_detector.hpp"
#endif

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <csignal>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <thread>
#include <vector>

namespace {

constexpr uint32_t SAMPLE_RATE = 48000;
constexpr uint32_t BLOCK_SIZE = 128;                 // 2.67 ms
constexpr size_t PITCH_WINDOW_SAMPLES = 2048;        // YIN rolling window

kan::LockFreeRingBuffer<float, 16384> g_audio_ring;
std::atomic<bool> g_running{true};
void signal_handler(int) { g_running.store(false); }

// Real-time audio thread: push only, no allocs / locks / I/O.
void audio_capture_callback(ma_device*, void*, const void* pInput, ma_uint32 frameCount) {
    const float* in = static_cast<const float*>(pInput);
    if (in != nullptr) g_audio_ring.push(in, frameCount);
}

// ---------------------------------------------------------------------------
// Instrument presets: app-level "pack-like" data mapped onto the plain
// InstrumentProfile struct (charts/spec will serialize exactly these fields).
struct DemoInstrument {
    const char* name;
    const char* hint;
    kan::InstrumentProfile profile;
};

DemoInstrument preset(const char* name) {
    DemoInstrument d{};
    d.name = name;
    d.hint = "";
    if (std::strcmp(name, "ocarina") == 0) {
        // 12-hole soprano ocarina in C: ~C5..F6, slow breath attack.
        d.profile = kan::InstrumentProfile::wind_bowed();
        d.profile.min_freq_hz = 480.0f;
        d.profile.max_freq_hz = 1450.0f;
        d.hint = "wind, 15 ms blanking, 480-1450 Hz (C5-F6)";
    } else if (std::strcmp(name, "flute") == 0) {
        d.profile = kan::InstrumentProfile::wind_bowed();
        d.profile.min_freq_hz = 260.0f;
        d.profile.max_freq_hz = 2400.0f;
        d.hint = "wind, 15 ms blanking, 260-2400 Hz (C4-C7)";
    } else if (std::strcmp(name, "recorder") == 0) {
        d.profile = kan::InstrumentProfile::wind_bowed();
        d.profile.min_freq_hz = 490.0f;
        d.profile.max_freq_hz = 1600.0f;
        d.hint = "wind, 15 ms blanking, 490-1600 Hz (soprano recorder)";
    } else if (std::strcmp(name, "guitar") == 0) {
        d.profile = kan::InstrumentProfile::plucked();
        d.hint = "plucked, 8 ms blanking, 80-1400 Hz (E2-)";
    } else if (std::strcmp(name, "piano") == 0) {
        d.profile = kan::InstrumentProfile::keyboard();
        d.hint = "keyboard, 6 ms blanking, 27-4300 Hz (A0-C8)";
    } else {
        d.name = "?";
    }
    return d;
}

// ---------------------------------------------------------------------------
// --expect note-name parsing: "A4", "C#5", "Bb3" -> MIDI number.
bool note_to_midi(const char* name, float& out_midi) {
    static const int BASE[7] = {9, 11, 0, 2, 4, 5, 7}; // A B C D E F G
    if (name == nullptr || name[0] == '\0') return false;
    char c = name[0];
    if (c >= 'a' && c <= 'z') c = char(c - 'a' + 'A');
    int letter = -1;
    switch (c) {
        case 'A': letter = 0; break;
        case 'B': letter = 1; break;
        case 'C': letter = 2; break;
        case 'D': letter = 3; break;
        case 'E': letter = 4; break;
        case 'F': letter = 5; break;
        case 'G': letter = 6; break;
        default: return false;
    }
    int i = 1;
    int acc = 0;
    if (name[i] == '#') { acc += 1; ++i; }
    else if (name[i] == 'b') { acc -= 1; ++i; }
    if (name[i] < '0' || name[i] > '9') return false;
    int octave = std::atoi(name + i);
    out_midi = float(12 * (octave + 1) + BASE[letter] + acc);
    return true;
}

std::string midi_to_name(float midi) {
    static const char* NAMES[12] = {"C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"};
    const int m = int(std::lround(midi));
    char buf[16];
    std::snprintf(buf, sizeof(buf), "%s%d", NAMES[((m % 12) + 12) % 12], (m / 12) - 1);
    return std::string(buf);
}

// ---------------------------------------------------------------------------
// Pipeline: stage 0 -> onset -> stage-2 blanking -> harmonic pitch.
struct Pipeline {
    float sr = float(SAMPLE_RATE);
    size_t block = BLOCK_SIZE;

    kan::Preconditioner pre;
    kan::HybridDetector onset;            // onset + YIN fallback
    kan::BoundedPitchMatcher matcher;     // score-informed confirmation
#ifdef KAN_DEMO_HAVE_BACF
    kan::BacfDetector bacf;
    bool use_bacf = false; // Cycfi Q path is opt-in: --engine bacf
#else
    bool use_bacf = false;
#endif

    bool expect_mode = false;
    float expected_midi = 60.0f;
    kan::InstrumentProfile profile;
    size_t need = 512;            // fixed matcher window (--expect mode)

    std::vector<float> rolling;   // YIN window (free mode)
    std::vector<float> clean;     // post-blanking window (--expect mode)
    float conditioned_last[BLOCK_SIZE] = {0}; ///< stage-0 output of the last
                                             /// block (the description stream)

    bool in_blanking = false;
    float ms_since_onset = 0.0f;

    struct Result {
        bool onset = false;
        bool pitch = false;
        float ms_after_onset = -1.0f;
        kan::PitchResult pr;
    };

    void setup(const kan::InstrumentProfile& prof, bool expect, float exp_midi,
               bool bacf_engine = false) {
        profile = prof;
        expect_mode = expect;
        expected_midi = exp_midi;
        use_bacf = bacf_engine;
        onset.set_profile(profile);
        pre = kan::Preconditioner(sr);
        matcher = kan::BoundedPitchMatcher(sr);
        if (expect_mode) {
            const float hz = 440.0f * std::pow(2.0f, (expected_midi - 69.0f) / 12.0f);
            matcher.set_expected_note(hz);
            need = matcher.window_samples();
        }
#ifdef KAN_DEMO_HAVE_BACF
        bacf.set_range(profile.min_freq_hz, profile.max_freq_hz);
#endif
        rolling.assign(PITCH_WINDOW_SAMPLES, 0.0f);
        clean.clear();
    }

    Result process(const float* raw) {
        Result r;
        float conditioned[BLOCK_SIZE];
        pre.process_block(raw, conditioned, block);
        std::memcpy(conditioned_last, conditioned, sizeof(conditioned));

        // Stage 1: fast onset. A new attack resets the trackers and re-arms
        // transient blanking (stage 2).
        if (onset.process_onset_block(conditioned, block)) {
            r.onset = true;
            in_blanking = true;
            ms_since_onset = 0.0f;
            clean.clear();
#ifdef KAN_DEMO_HAVE_BACF
            if (use_bacf) bacf.reset();
#endif
        } else {
            ms_since_onset += 1000.0f * float(block) / sr;
        }

        // Stage 2: transient blanking. Freeze pitch evaluation AND stop the
        // trackers from ingesting the inharmonic attack — feeding it in
        // corrupts the autocorrelation state (the reason a naive first read
        // lands a semitone high on breathy instruments).
        if (in_blanking) {
            if (onset.in_transient_blanking(ms_since_onset)) return r;
            in_blanking = false;
        }

        // Stage 3: harmonic pitch on clean audio only.
        if (expect_mode) {
            // Score-informed: bounded matcher over a fixed clean window
            // (its design size — a growing window breaks the NSDF peak).
            clean.insert(clean.end(), conditioned, conditioned + block);
            if (clean.size() > need) clean.erase(clean.begin(), clean.end() - need);
            if (clean.size() >= need) {
                auto p = matcher.match(clean.data(), clean.size());
                if (p.valid) {
                    r.pitch = true;
                    r.pr = p;
                    r.ms_after_onset = ms_since_onset;
                }
            }
        } else {
            // Free tuner: continuous tracking.
            std::move(rolling.begin() + block, rolling.end(), rolling.begin());
            std::copy(conditioned, conditioned + block, rolling.end() - block);
#ifdef KAN_DEMO_HAVE_BACF
            kan::PitchResult p = use_bacf ? bacf.process_block(conditioned, block)
                                          : onset.detect_pitch(rolling);
#else
            const kan::PitchResult p = onset.detect_pitch(rolling);
#endif
            if (p.valid) {
                r.pitch = true;
                r.pr = p;
                r.ms_after_onset = ms_since_onset;
            }
        }
        return r;
    }
};

// ---------------------------------------------------------------------------
// Per-note description stream (docs/architecture/note-description-stream.md):
// facts as they become available — HIT (fixed at the onset), provisional
// pitch (display-only), CONTENT (mono/polyphonic + per-candidate
// measurements), final pitch or honest "unresolved", EXPRESSION, CLOSE.
// With --events the same facts go to a JSONL log (the schema kan_analyze
// --events writes offline; live/offline byte-identical on the same audio).

void print_event(const kan::NoteEvent& e, FILE* jsonl) {
    if (jsonl != nullptr) {
        char buf[512];
        if (kan::note_event_jsonl(e, buf, sizeof(buf)) > 0) std::fputs(buf, jsonl);
    }
    switch (e.kind) {
    case kan::NoteKind::Hit:
        std::printf("\n▶ #%u ONSET\n", e.note_id);
        return;
    case kan::NoteKind::Pitch:
        if (e.stage == kan::PitchStage::Unresolved || e.u.pitch.hz <= 0.0f) {
            std::printf("  #%u pitch %s: unresolved%s%s\n", e.note_id,
                        e.stage == kan::PitchStage::Final ? "final      " : "provisional",
                        (e.flags & kan::kFlagHop) ? " [HOP]" : "",
                        (e.flags & kan::kFlagMixed) ? " [MIX]" : "");
        } else {
            std::printf("  #%u pitch %s: %-4s %+6.1f c  %7.2f Hz%s\n", e.note_id,
                        e.stage == kan::PitchStage::Final ? "final      " : "provisional",
                        midi_to_name(e.u.pitch.midi).c_str(), double(e.u.pitch.cents),
                        double(e.u.pitch.hz),
                        (e.flags & kan::kFlagDominant) ? " [dominant]" : "");
        }
        return;
    case kan::NoteKind::Content:
        if (e.klass != kan::ContentClass::Unknown) {
            if (e.klass == kan::ContentClass::Polyphonic) {
                std::printf("  #%u content: polyphonic, %.0f unresolved\n",
                            e.note_id, double(e.u.content.n_unresolved));
            } else {
                std::printf("  #%u content: mono\n", e.note_id);
            }
        } else {
            std::printf("     cand %-4s presence %.2f %+6.1f c amp %.3f (sim %.2f)%s\n",
                        midi_to_name(e.u.candidate.midi).c_str(),
                        double(e.u.candidate.presence), double(e.u.candidate.cents),
                        double(e.u.candidate.amplitude), double(e.u.candidate.similarity),
                        (e.flags & kan::kFlagSearchSaturated) ? " [search-saturated]" : "");
        }
        return;
    case kan::NoteKind::Expression:
        std::printf("  #%u expression: level %.1f dB, attack %.0f ms, release %.0f ms, "
                    "sustain %.2f", e.note_id, double(e.u.expression.dynamics_db),
                    double(e.u.expression.attack_ms), double(e.u.expression.release_ms),
                    double(e.u.expression.sustain_ratio));
        if (e.u.expression.vib_rate_hz > 0.0f) {
            std::printf(", vibrato %.1f Hz %.0f c", double(e.u.expression.vib_rate_hz),
                        double(e.u.expression.vib_depth_c));
        }
        std::printf("\n");
        return;
    case kan::NoteKind::Close:
        std::printf("  #%u close: %.0f ms\n", e.note_id, double(e.u.close.duration_ms));
        return;
    }
}

/// --candidates file: one line per attack, space-separated midi hypotheses
/// (include notes still sustaining from earlier attacks). Consumed in
/// attack order; anonymous hypotheses (the demo has no chart to bind to).
std::vector<std::vector<float>> load_candidates(const char* path) {
    std::vector<std::vector<float>> out;
    FILE* f = std::fopen(path, "r");
    if (f == nullptr) return out;
    char line[256];
    while (std::fgets(line, sizeof(line), f) != nullptr) {
        std::vector<float> midis;
        const char* p = line;
        for (;;) {
            char* end = nullptr;
            const float v = std::strtof(p, &end);
            if (end == p) break;
            midis.push_back(v);
            p = end;
        }
        if (!midis.empty()) out.push_back(std::move(midis));
    }
    std::fclose(f);
    return out;
}

// ---------------------------------------------------------------------------
// Synthesized ocarina-ish tone for --selftest: breathy attack (turbulence
// noise burst ~25 ms — real wind instruments have this, and it is what the
// HFC onset detector locks onto), then a nearly pure sine with a weak 2nd
// harmonic at C5 +8 cents.
std::vector<float> synth_ocarina_note(float freq, float seconds) {
    const size_t n = size_t(seconds * float(SAMPLE_RATE));
    std::vector<float> out(n);
    uint32_t rng = 0x9E3779B9u;
    for (size_t i = 0; i < n; ++i) {
        const float t = float(i) / float(SAMPLE_RATE);
        const float attack = std::min(1.0f, t / 0.025f);
        rng = rng * 1664525u + 1013904223u; // LCG noise for the breath attack
        const float noise = (float(int(rng >> 8 & 0xFFFF)) / 32767.5f - 1.0f);
        const float breath = 0.35f * noise * std::exp(-t / 0.012f);
        out[i] = attack * 0.5f * (std::sin(2.0f * float(M_PI) * freq * t)
                                + 0.2f * std::sin(2.0f * float(M_PI) * 2.0f * freq * t))
                + breath;
    }
    return out;
}

int run_selftest() {
    printf("kan_demo selftest: synthesized ocarina C5 +8 cents\n");
    const float true_hz = 523.25f * std::pow(2.0f, 8.0f / 1200.0f);
    auto sig = synth_ocarina_note(true_hz, 0.4f);
    DemoInstrument inst = preset("ocarina");

    bool pass = true;

    // Free mode, YIN engine (the robust default): onset + steady note.
    {
        Pipeline p;
        p.setup(inst.profile, false, 60.0f, false);
        bool onset = false;
        kan::PitchResult best;
        for (size_t off = 0; off + BLOCK_SIZE <= sig.size(); off += BLOCK_SIZE) {
            auto r = p.process(&sig[off]);
            onset |= r.onset;
            if (r.pitch) best = r.pr; // last (settled) reading
        }
        printf("free (YIN):  onset=%d note=%s freq=%.2f (true %.2f)\n",
               int(onset), best.note_name.c_str(), best.frequency_hz, true_hz);
        pass &= onset && best.valid && best.note_name == "C5"
                && std::fabs(best.frequency_hz - true_hz) < 8.0f;
    }

#ifdef KAN_DEMO_HAVE_BACF
    // Free mode, BACF engine: must confirm a note. Its steady accuracy on
    // real instruments is the A/B question of docs/dsp/bacf-vs-yin.md; onset
    // lock-in means we only assert a valid, in-range reading here.
    {
        Pipeline p;
        p.setup(inst.profile, false, 60.0f, true);
        bool onset = false, any = false;
        kan::PitchResult best;
        for (size_t off = 0; off + BLOCK_SIZE <= sig.size(); off += BLOCK_SIZE) {
            auto r = p.process(&sig[off]);
            onset |= r.onset;
            if (r.pitch) { any = true; best = r.pr; }
        }
        printf("free (BACF): onset=%d note=%s freq=%.2f\n",
               int(onset), best.note_name.c_str(), best.frequency_hz);
        pass &= onset && any && best.valid;
    }
#endif

    // Score-informed mode: bounded matcher confirmation (< 60 ms from onset).
    {
        Pipeline p;
        p.setup(inst.profile, true, 60.0f, false);
        bool onset = false, confirmed = false;
        float confirm_ms = -1.0f;
        for (size_t off = 0; off + BLOCK_SIZE <= sig.size(); off += BLOCK_SIZE) {
            auto r = p.process(&sig[off]);
            onset |= r.onset;
            if (r.pitch && !confirmed) {   // latch the FIRST confirmation
                confirmed = true;
                confirm_ms = r.ms_after_onset;
            }
        }
        printf("--expect C5: onset=%d confirmed=%d at +%.1f ms\n",
               int(onset), int(confirmed), confirm_ms);
        pass &= onset && confirmed && confirm_ms > 0.0f && confirm_ms < 60.0f;
    }

    // Per-note description stream: a chord with candidate hypotheses must
    // produce per-candidate presence/cents facts, and the absent hypothesis
    // must not be invented (the conditioned measurement path --candidates
    // exercises live).
    {
        kan::InstrumentProfile piano = preset("piano").profile;
        kan::NoteDescriber desc(float(SAMPLE_RATE), piano);
        kan::NoteEventStream stream(1024);
        desc.subscribe(stream);
        std::vector<float> chord(size_t(0.6f * float(SAMPLE_RATE)), 0.0f);
        const float freqs[3] = {261.63f, 329.63f, 392.00f}; // C4 E4 G4
        for (size_t i = 0; i < chord.size(); ++i) {
            const float t = float(i) / float(SAMPLE_RATE);
            float s = 0.0f;
            if (t < 0.008f) {
                const uint32_t h = uint32_t(i) * 2654435761u + 3u;
                s = 0.15f * (float(int((h ^ (h >> 13)) % 2000)) / 1000.0f - 1.0f)
                  * (1.0f - t / 0.008f);
            }
            for (float f : freqs) {
                for (int h = 1; h <= 4; ++h) {
                    s += 0.33f * (0.5f / float(h)) * std::exp(-1.2f * t)
                       * std::sin(2.0f * float(M_PI) * f * float(h) * t);
                }
            }
            chord[i] = 0.6f * s;
        }
        kan::NoteDescriber::CandidateSet cs; // hypotheses for the next attack
        cs.note_id = 0;
        cs.count = 4;
        cs.entries[0] = {0, 60.0f};
        cs.entries[1] = {0, 64.0f};
        cs.entries[2] = {0, 67.0f};
        cs.entries[3] = {0, 69.0f}; // A4: absent — must not be invented
        desc.set_candidates(cs);

        bool hit = false, provisional = false, close_seen = false, absent_low = false;
        int cand_seen = 0;
        auto drain = [&]() {
            kan::NoteEvent batch[32];
            for (;;) {
                const size_t got = stream.pop(batch, 32);
                if (got == 0) break;
                for (size_t k = 0; k < got; ++k) {
                    const auto& e = batch[k];
                    if (e.kind == kan::NoteKind::Hit) hit = true;
                    if (e.kind == kan::NoteKind::Pitch
                        && e.stage == kan::PitchStage::Provisional) provisional = true;
                    if (e.kind == kan::NoteKind::Close) close_seen = true;
                    if (e.kind == kan::NoteKind::Content
                        && e.klass == kan::ContentClass::Unknown) {
                        ++cand_seen;
                        if (e.u.candidate.midi == 69.0f
                            && e.u.candidate.presence <= 0.05f) absent_low = true;
                    }
                }
            }
        };
        for (size_t off = 0; off + BLOCK_SIZE <= chord.size(); off += BLOCK_SIZE) {
            desc.process_block(&chord[off], BLOCK_SIZE);
            while (desc.finalize_due()) {}
            drain();
        }
        desc.flush();
        drain();
        printf("note stream:  hit=%d provisional=%d candidate facts=%d "
               "absent<=0.05=%d close=%d\n",
               int(hit), int(provisional), cand_seen, int(absent_low), int(close_seen));
        pass &= hit && provisional && cand_seen == 4 && absent_low && close_seen;
    }

    printf(pass ? "SELFTEST PASS\n" : "SELFTEST FAIL\n");
    return pass ? 0 : 1;
}

void print_usage() {
    std::printf(
        "kan_demo — live real-instrument demo for libkan\n"
        "\n"
        "Usage: kan_demo [options]\n"
        "  --instrument <name>   ocarina | flute | recorder | guitar | piano\n"
        "                        (default: ocarina)\n"
        "  --expect <note>       score-informed mode: confirm the expected\n"
        "                        note (e.g. C5, A4, F#5) with the bounded\n"
        "                        matcher — the game's confirmation path\n"
        "  --engine <yin|bacf>   free-tuner pitch engine (default yin; bacf =\n"
        "                        Cycfi Q bitstream autocorrelation)\n"
        "  --calibrate           play one long comfortable note after the\n"
        "                        noise gate calibrates; prints the global\n"
        "                        detuning Δc in cents and applies it\n"
        "  --vibrato             live vibrato readout (rate Hz / depth cents)\n"
        "                        from the cents trajectory — technique check\n"
        "  --csv <file>          write per-note events (onset ms, note, Hz,\n"
        "                        cents, vibrato) — the descriptor feature\n"
        "                        stream for later evaluation\n"
        "  --events <f.jsonl>    write the per-note description stream (HIT /\n"
        "                        provisional / CONTENT / final / EXPRESSION /\n"
        "                        CLOSE facts; same schema as kan_analyze\n"
        "                        --events — live/offline diffable) and show\n"
        "                        the facts progressively on screen\n"
        "  --candidates <f>      candidate hypotheses per attack (one line per\n"
        "                        attack, space-separated midis; include notes\n"
        "                        still sustaining) — conditions the CONTENT\n"
        "                        measurements (presence/cents/amplitude)\n"
        "  --no-gate             disable the adaptive noise gate\n"
        "  --noise-cal <ms>      room-silence calibration window (default 500)\n"
        "  --selftest            offline pipeline check (no audio device)\n"
        "  --help                this text\n");
}

} // namespace

int main(int argc, char** argv) {
    const char* instrument_name = "ocarina";
    const char* expect_note = nullptr;
    const char* engine = "yin";
    const char* csv_path = nullptr;
    const char* events_path = nullptr;
    const char* candidates_path = nullptr;
    bool calibrate = false;
    bool vibrato_mode = false;
    bool use_gate = true;
    int noise_cal_ms = 500;

    for (int i = 1; i < argc; ++i) {
        if (std::strcmp(argv[i], "--instrument") == 0 && i + 1 < argc) {
            instrument_name = argv[++i];
        } else if (std::strcmp(argv[i], "--expect") == 0 && i + 1 < argc) {
            expect_note = argv[++i];
        } else if (std::strcmp(argv[i], "--engine") == 0 && i + 1 < argc) {
            engine = argv[++i];
        } else if (std::strcmp(argv[i], "--calibrate") == 0) {
            calibrate = true;
        } else if (std::strcmp(argv[i], "--vibrato") == 0) {
            vibrato_mode = true;
        } else if (std::strcmp(argv[i], "--csv") == 0 && i + 1 < argc) {
            csv_path = argv[++i];
        } else if (std::strcmp(argv[i], "--events") == 0 && i + 1 < argc) {
            events_path = argv[++i];
        } else if (std::strcmp(argv[i], "--candidates") == 0 && i + 1 < argc) {
            candidates_path = argv[++i];
        } else if (std::strcmp(argv[i], "--no-gate") == 0) {
            use_gate = false;
        } else if (std::strcmp(argv[i], "--noise-cal") == 0 && i + 1 < argc) {
            noise_cal_ms = std::atoi(argv[++i]);
        } else if (std::strcmp(argv[i], "--selftest") == 0) {
            return run_selftest();
        } else if (std::strcmp(argv[i], "--help") == 0 || std::strcmp(argv[i], "-h") == 0) {
            print_usage();
            return 0;
        } else {
            std::fprintf(stderr, "[ERROR] Unknown option: %s\n\n", argv[i]);
            print_usage();
            return -1;
        }
    }

    const bool use_bacf_engine = std::strcmp(engine, "bacf") == 0;
    if (!use_bacf_engine && std::strcmp(engine, "yin") != 0) {
        std::fprintf(stderr, "[ERROR] Unknown engine '%s'. Valid: yin, bacf.\n", engine);
        return -1;
    }
#ifndef KAN_DEMO_HAVE_BACF
    if (use_bacf_engine) {
        std::fprintf(stderr, "[ERROR] This build has no BACF (KAN_ENABLE_BACF=OFF).\n");
        return -1;
    }
#endif

    DemoInstrument inst = preset(instrument_name);
    if (std::strcmp(inst.name, "?") == 0) {
        std::fprintf(stderr, "[ERROR] Unknown instrument '%s'. "
                             "Valid: ocarina, flute, recorder, guitar, piano.\n",
                     instrument_name);
        return -1;
    }

    float expected_midi = 60.0f;
    if (expect_note != nullptr) {
        if (!note_to_midi(expect_note, expected_midi)) {
            std::fprintf(stderr, "[ERROR] Bad note name '%s' (examples: C5, A4, F#5).\n",
                         expect_note);
            return -1;
        }
    }

    std::printf("====================================================\n");
    std::printf("  kan! demo — live instrument check (libkan v0.1)\n");
    std::printf("  Instrument: %-8s %s\n", inst.name, inst.hint);
    std::printf("  Audio: %u Hz / %u samples\n", SAMPLE_RATE, BLOCK_SIZE);
    std::printf("  Engine: %s\n",
                use_bacf_engine ? "Cycfi Q BACF (fast-confirm path)"
                                : "YIN (robust free-tuner default)");
    if (expect_note != nullptr) {
        std::printf("  Mode: score-informed --expect %s\n", expect_note);
    } else if (calibrate) {
        std::printf("  Mode: calibration (play one long note when ready)\n");
    } else {
        std::printf("  Mode: free tuner (onset + note + cents)\n");
    }
    std::printf("  Ctrl+C to exit\n");
    std::printf("====================================================\n");
    if (use_gate) {
        std::printf("Keep the room quiet for the first %d ms (noise gate)...\n", noise_cal_ms);
    }

    std::signal(SIGINT, signal_handler);
    std::signal(SIGTERM, signal_handler);

    ma_device_config config = ma_device_config_init(ma_device_type_capture);
    config.capture.format = ma_format_f32;
    config.capture.channels = 1;
    config.sampleRate = SAMPLE_RATE;
    config.periodSizeInFrames = BLOCK_SIZE;
    config.performanceProfile = ma_performance_profile_low_latency;
    config.dataCallback = audio_capture_callback;

    ma_device device;
    if (ma_device_init(nullptr, &config, &device) != MA_SUCCESS) {
        std::fprintf(stderr, "[ERROR] Could not open the audio capture device.\n");
        return -1;
    }
    ma_device_start(&device);

    Pipeline pipeline;
    pipeline.setup(inst.profile, expect_note != nullptr, expected_midi, use_bacf_engine);

    // Noise-gate calibration buffer (consumer thread; allocations fine).
    std::vector<float> calib;
    if (use_gate && noise_cal_ms > 0) {
        calib.reserve(size_t(SAMPLE_RATE / 1000 * noise_cal_ms));
    }
    const size_t calib_target = calib.capacity();
    bool gate_calibrated = !use_gate || calib_target == 0;

    // Calibration mode state (item 5): 2 s reference note -> Δc, applied to
    // the tuner readout (and --expect targets) once complete.
    kan::DetuneCalibrator calibrator(2.0f);
    kan::TuningCompensation comp;
    bool cal_done = !calibrate;
    const auto session_start = std::chrono::steady_clock::now();
    auto session_seconds = [&]() {
        return float(std::chrono::duration<double>(
            std::chrono::steady_clock::now() - session_start).count());
    };

    // Tuner state: stable display, hysteresis against note flicker.
    float display_midi = -1.0f;
    int notes_played = 0;
    int pitches_shown = 0;
    double abs_cents_sum = 0.0;

    // Vibrato technique readout (--vibrato): cents trajectory per note.
    kan::TrajectoryBuffer vib_traj;
    kan::VibratoAnalyzer vib_ana;
    char vib_str[48] = "";
    int vib_tick = 0;
    float vib_rate_last = 0.0f, vib_depth_last = 0.0f;

    // CSV event log (--csv): per-note rows = the evaluation feature stream.
    FILE* csv = nullptr;
    if (csv_path != nullptr) {
        csv = std::fopen(csv_path, "w");
        if (csv == nullptr) {
            std::fprintf(stderr, "[ERROR] cannot open %s for writing\n", csv_path);
            return -1;
        }
        std::fprintf(csv, "t_ms,note,hz,cents,vib_hz,vib_depth_c\n");
    }

    // Per-note description stream: progressive facts on screen, optional
    // JSONL log (--events), optional candidate hypotheses (--candidates).
    FILE* events = nullptr;
    if (events_path != nullptr) {
        events = std::fopen(events_path, "w");
        if (events == nullptr) {
            std::fprintf(stderr, "[ERROR] cannot open %s for writing\n", events_path);
            return -1;
        }
    }
    std::vector<std::vector<float>> cand_lists;
    if (candidates_path != nullptr) {
        cand_lists = load_candidates(candidates_path);
        if (cand_lists.empty()) {
            std::fprintf(stderr, "[ERROR] no candidates read from %s\n", candidates_path);
            return -1;
        }
    }
    kan::NoteDescriber desc(float(SAMPLE_RATE), inst.profile);
    kan::NoteEventStream ev_stream(4096);
    desc.subscribe(ev_stream);
    size_t cand_idx = 0;
    auto drain_events = [&]() {
        kan::NoteEvent batch[32];
        for (;;) {
            const size_t got = ev_stream.pop(batch, 32);
            if (got == 0) break;
            for (size_t k = 0; k < got; ++k) {
                const auto& e = batch[k];
                if (e.kind == kan::NoteKind::Hit && cand_idx < cand_lists.size()) {
                    // Hypotheses for this attack (may include notes still
                    // sustaining from earlier attacks — anonymous here).
                    kan::NoteDescriber::CandidateSet cs;
                    cs.note_id = e.note_id;
                    const auto& midis = cand_lists[cand_idx++];
                    cs.count = uint8_t(std::min<size_t>(midis.size(),
                                                        kan::NoteDescriber::kMaxCandidates));
                    for (size_t m = 0; m < cs.count; ++m) cs.entries[m] = {0, midis[m]};
                    desc.set_candidates(cs);
                }
                print_event(e, events);
            }
        }
    };

    auto on_pitch = [&](const kan::PitchResult& pr, float ms_after) {
        const float freq = pr.frequency_hz;
        const float exact_midi = 69.0f + 12.0f * std::log2(freq / 440.0f);

        if (!cal_done) {
            calibrator.add_frequency(freq, session_seconds());
            if (calibrator.ready()) {
                const auto m = calibrator.finish(); // nearest equal-tempered note
                comp.set_global(m.delta_cents);
                cal_done = true;
                std::printf("\n[calibration] measured %.2f Hz vs %s = %+.2f cents "
                            "(Δc global offset, spread %.2f)\n",
                            m.measured_hz,
                            midi_to_name(m.standard_midi).c_str(), m.delta_cents,
                            m.spread_cents);
                std::printf("[calibration] compensation applied: the tuner now shows "
                            "your residual vs your OWN tuning.\n");
                if (expect_note != nullptr) {
                    pipeline.matcher.set_expected_note(comp.target_frequency(
                        440.0f * std::pow(2.0f, (expected_midi - 69.0f) / 12.0f)));
                    std::printf("[calibration] --expect target shifted by the same Δc.\n");
                }
            }
        }

        // Compensated readout: deviations relative to the instrument's own
        // tuning (SPEC §2 dynamic consonance), not to fixed A440.
        const float adj_midi = exact_midi - comp.global_cents / 100.0f;
        if (display_midi < 0.0f || std::fabs(adj_midi - display_midi) > 0.7f) {
            display_midi = std::floor(adj_midi + 0.5f); // snap, ±50 c window
        }
        const float cents = (adj_midi - display_midi) * 100.0f;

        ++pitches_shown;
        abs_cents_sum += std::fabs(cents);

        // Live technique feedback: vibrato rate/depth from the trajectory.
        if (vibrato_mode) {
            vib_traj.add(ms_after, cents);
            if (++vib_tick % 10 == 0 && vib_traj.size() >= 8) {
                const auto vm = vib_ana.measure(vib_traj);
                vib_rate_last = vm.rate_hz;
                vib_depth_last = vm.depth_cents;
                if (vm.periodic) {
                    std::snprintf(vib_str, sizeof(vib_str), " | vib %.1f Hz %+.0f c",
                                  double(vm.rate_hz), double(vm.depth_cents));
                } else {
                    std::snprintf(vib_str, sizeof(vib_str), " | vib --");
                }
            }
        }

        if (csv != nullptr) {
            std::fprintf(csv, "%.1f,%s,%.2f,%.1f,%.2f,%.1f\n",
                         double(session_seconds() * 1000.0f),
                         midi_to_name(display_midi).c_str(), double(freq),
                         double(cents), double(vib_rate_last), double(vib_depth_last));
        }

        // Live tuner line (single-line refresh).
        char bar[17];
        const int pos = std::clamp(int(std::lround(cents / 10.0f)) + 8, 0, 16);
        for (int i = 0; i < 17; ++i) bar[i] = (i == 8) ? '|' : (i == pos ? '#' : '.');
        bar[16] = '\0';
        std::printf("\r♪ %-4s %7.2f Hz  %+6.1f cents  %s  (+%.0f ms)%s   ",
                    midi_to_name(display_midi).c_str(), freq, cents, bar, ms_after, vib_str);
        std::fflush(stdout);
    };

    std::printf("Ready. Play your instrument!\n");
    float block[BLOCK_SIZE];
    while (g_running.load()) {
        if (!g_audio_ring.pop(block, BLOCK_SIZE)) {
            std::this_thread::sleep_for(std::chrono::microseconds(200));
            continue;
        }

        if (use_gate && !gate_calibrated) {
            calib.insert(calib.end(), block, block + BLOCK_SIZE);
            if (calib.size() >= calib_target) {
                pipeline.pre.calibrate(calib.data(), calib.size());
                const float floor_rms = pipeline.pre.gate().noise_floor_rms();
                pipeline.onset.set_noise_floor(floor_rms * floor_rms);
                gate_calibrated = true;
                calib.clear();
                calib.shrink_to_fit();
                std::printf("[stage 0] noise gate calibrated: floor RMS = %.5f\n", floor_rms);
            }
            continue;
        }

        auto r = pipeline.process(block);

        // The description stream consumes the same stage-0 audio: facts as
        // they become available (HIT/provisional inline; long-window final
        // measurements via finalize_due — movable to a worker thread).
        desc.process_block(pipeline.conditioned_last, BLOCK_SIZE);
        while (desc.finalize_due()) {}
        drain_events();

        if (r.onset) {
            ++notes_played;
            vib_traj.clear(); // trajectory is per-note
            std::printf("\n▶ ONSET%s", expect_note != nullptr ? " (checking note...)" : "");
            std::fflush(stdout);
        }
        if (r.pitch) on_pitch(r.pr, r.ms_after_onset);
    }

    desc.flush();
    drain_events();
    std::printf("\n\n--- session summary ---\n");
    std::printf("onsets: %d, pitch updates: %d, avg |error| vs displayed note: %.1f cents\n",
                notes_played, pitches_shown,
                pitches_shown ? abs_cents_sum / pitches_shown : 0.0);
    if (csv != nullptr) {
        std::fclose(csv);
        std::printf("event log written to %s\n", csv_path);
    }
    if (events != nullptr) {
        std::fclose(events);
        std::printf("note-description stream written to %s\n", events_path);
    }
    if (calibrate && !cal_done) {
        std::printf("calibration: not enough steady readings; hold one note for ~2 s.\n");
    }
    std::printf("Reminder: this is a detection demo — timing/pitch are measured,\n"
                "not judged. Scoring, backing-track consonance and visual feedback\n"
                "live in later milestones (see apps/kan_demo/README.md).\n");

    ma_device_uninit(&device);
    return 0;
}
