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
        std::printf("\n[%9.1f] ▶ #%u ONSET (strike %u%s)\n", double(e.t_ms),
                    e.note_id, e.u.hit.strike_group,
                    (e.flags & kan::kFlagTransition) ? ", legato transition" : "");
        return;
    case kan::NoteKind::Pitch: {
        const char* label = (e.stage == kan::PitchStage::Provisional)
            ? "provisional" : "final      ";
        if (e.stage == kan::PitchStage::Unresolved || e.u.pitch.hz <= 0.0f) {
            std::printf("[%9.1f]   #%u pitch %s: unresolved%s%s\n", double(e.t_ms),
                        e.note_id, label,
                        (e.flags & kan::kFlagHop) ? " [HOP]" : "",
                        (e.flags & kan::kFlagMixed) ? " [MIX]" : "");
        } else {
            std::printf("[%9.1f]   #%u pitch %s: %-4s %+6.1f c  %7.2f Hz%s\n",
                        double(e.t_ms), e.note_id, label,
                        midi_to_name(e.u.pitch.midi).c_str(), double(e.u.pitch.cents),
                        double(e.u.pitch.hz),
                        (e.flags & kan::kFlagDominant) ? " [dominant]" : "");
        }
        return;
    }
    case kan::NoteKind::Content:
        if (e.klass != kan::ContentClass::Unknown) {
            if (e.klass == kan::ContentClass::Polyphonic) {
                std::printf("[%9.1f]   #%u content: polyphonic, %.0f unresolved\n",
                            double(e.t_ms), e.note_id, double(e.u.content.n_unresolved));
            } else {
                std::printf("[%9.1f]   #%u content: mono\n", double(e.t_ms), e.note_id);
            }
        } else {
            std::printf("[%9.1f]      cand %-4s presence %.2f %+6.1f c amp %.3f (sim %.2f)%s\n",
                        double(e.t_ms),
                        midi_to_name(e.u.candidate.midi).c_str(),
                        double(e.u.candidate.presence), double(e.u.candidate.cents),
                        double(e.u.candidate.amplitude), double(e.u.candidate.similarity),
                        (e.flags & kan::kFlagSearchSaturated) ? " [search-saturated]" : "");
        }
        return;
    case kan::NoteKind::Expression:
        std::printf("[%9.1f]   #%u expression: level %.1f dB (%s), attack %.0f ms, "
                    "release %.0f ms, sustain %.2f", double(e.t_ms), e.note_id, double(e.u.expression.dynamics_db),
                    kan::dynamics_tier_name(
                        kan::DynamicsTier(int(e.u.expression.dynamics_tier))),
                    double(e.u.expression.attack_ms), double(e.u.expression.release_ms),
                    double(e.u.expression.sustain_ratio));
        if (e.u.expression.vib_rate_hz > 0.0f) {
            std::printf(", vibrato %.1f Hz %.0f c", double(e.u.expression.vib_rate_hz),
                        double(e.u.expression.vib_depth_c));
        }
        std::printf("\n");
        return;
    case kan::NoteKind::Close:
        std::printf("[%9.1f]   #%u close: %.0f ms (own end)\n", double(e.t_ms),
                    e.note_id, double(e.u.close.duration_ms));
        return;
    }
}

/// --candidates file: one line per attack. "@<t_ms> midi midi ..." binds
/// the hypotheses to the attack nearest that time (within 250 ms); a plain
/// "midi midi ..." line applies to the next attack without a timed line.
/// Hypotheses may include notes still sustaining from earlier attacks.
/// Lines starting with '#' are comments.
struct CandidateLine {
    float t_ms = -1.0f;          ///< attack time to bind to (< 0 = next attack)
    std::vector<float> midis;
};

std::vector<CandidateLine> load_candidates(const char* path) {
    std::vector<CandidateLine> out;
    FILE* f = std::fopen(path, "r");
    if (f == nullptr) return out;
    char line[512];
    while (std::fgets(line, sizeof(line), f) != nullptr) {
        const char* p = line;
        while (*p == ' ' || *p == '\t') ++p;
        if (*p == '\0' || *p == '\n' || *p == '#') continue;
        CandidateLine cl;
        if (*p == '@') {
            char* end = nullptr;
            cl.t_ms = std::strtof(p + 1, &end);
            p = end;
        }
        for (;;) {
            while (*p == ' ' || *p == '\t') ++p;
            const bool sustaining = (*p == '~');
            if (sustaining) ++p;
            char* end = nullptr;
            const float v = std::strtof(p, &end);
            if (end == p) break;
            cl.midis.push_back(sustaining ? -v : v); // sign marks sustaining
            p = end;
        }
        if (!cl.midis.empty()) out.push_back(std::move(cl));
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
        cs.entries[0] = {0, 60.0f, 1}; // struck here (own record each)
        cs.entries[1] = {0, 64.0f, 1};
        cs.entries[2] = {0, 67.0f, 1};
        cs.entries[3] = {0, 69.0f, 1}; // A4: absent — must not be invented
        desc.set_candidates(cs);

        bool provisional = false, absent_low = false;
        int hits = 0, closes = 0, cand_seen = 0;
        auto drain = [&]() {
            kan::NoteEvent batch[32];
            for (;;) {
                const size_t got = stream.pop(batch, 32);
                if (got == 0) break;
                for (size_t k = 0; k < got; ++k) {
                    const auto& e = batch[k];
                    if (e.kind == kan::NoteKind::Hit) ++hits;
                    if (e.kind == kan::NoteKind::Pitch
                        && e.stage == kan::PitchStage::Provisional) provisional = true;
                    if (e.kind == kan::NoteKind::Close) ++closes;
                    if (e.kind == kan::NoteKind::Content
                        && e.klass == kan::ContentClass::Unknown) {
                        ++cand_seen;
                        // absence evidence: the FINAL fact (trajectory)
                        if (e.u.candidate.midi == 69.0f && e.t_ms > 0.5f
                            && e.u.candidate.presence <= 0.10f) absent_low = true;
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
        printf("note stream:  hits=%d provisional=%d candidate facts=%d "
               "absent-final<=0.10=%d closes=%d\n",
               hits, int(provisional), cand_seen, int(absent_low), closes);
        // New record model: 4 struck hypotheses -> 4 records (one per note),
        // each with its own facts and its own close.
        pass &= hits == 4 && provisional && cand_seen >= 4 && absent_low && closes == 4;
    }

    // File input path (--input): writing a WAV and decoding it back must
    // describe byte-identically to feeding the same samples directly — the
    // file mode runs the exact live consumer (live/offline equivalence).
    {
        auto describe_jsonl = [](const std::vector<float>& sig) {
            kan::NoteDescriber d(float(SAMPLE_RATE), preset("ocarina").profile);
            kan::NoteEventStream s(1024);
            d.subscribe(s);
            std::string out;
            auto drain = [&]() {
                kan::NoteEvent batch[32];
                for (;;) {
                    const size_t got = s.pop(batch, 32);
                    if (got == 0) break;
                    for (size_t k = 0; k < got; ++k) {
                        char buf[512];
                        if (kan::note_event_jsonl(batch[k], buf, sizeof(buf)) > 0) {
                            out.append(buf);
                        }
                    }
                }
            };
            for (size_t off = 0; off + BLOCK_SIZE <= sig.size(); off += BLOCK_SIZE) {
                d.process_block(&sig[off], BLOCK_SIZE);
                while (d.finalize_due()) {}
                drain();
            }
            d.flush();
            drain();
            return out;
        };

        const char* tmp = "kan_demo_selftest_roundtrip.wav";
        const auto sig = synth_ocarina_note(523.25f, 0.4f);
        ma_encoder_config ecfg = ma_encoder_config_init(ma_encoding_format_wav,
                                                        ma_format_f32, 1, SAMPLE_RATE);
        ma_encoder enc;
        bool wrote = ma_encoder_init_file(tmp, &ecfg, &enc) == MA_SUCCESS;
        if (wrote) {
            ma_uint64 written = 0;
            ma_encoder_write_pcm_frames(&enc, sig.data(), sig.size(), &written);
            ma_encoder_uninit(&enc);
            wrote = written == sig.size();
        }
        std::vector<float> decoded;
        ma_decoder_config dcfg = ma_decoder_config_init(ma_format_f32, 1, SAMPLE_RATE);
        ma_decoder dec;
        if (wrote && ma_decoder_init_file(tmp, &dcfg, &dec) == MA_SUCCESS) {
            float chunk[4096];
            for (;;) {
                ma_uint64 got = 0;
                if (ma_decoder_read_pcm_frames(&dec, chunk, 4096, &got) != MA_SUCCESS
                    || got == 0) {
                    break;
                }
                decoded.insert(decoded.end(), chunk, chunk + got);
            }
            ma_decoder_uninit(&dec);
        }
        std::remove(tmp);
        const std::string direct = describe_jsonl(sig);
        const std::string via_file = describe_jsonl(decoded);
        printf("file round-trip: wrote=%d decoded=%zu/%zu streams equal=%d\n",
               int(wrote), decoded.size(), sig.size(), int(direct == via_file));
        pass &= wrote && decoded.size() == sig.size() && !direct.empty()
                && direct == via_file;
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
        "  --input <wav>         offline validation: run the exact live\n"
        "                        pipeline over a recording (no audio device) —\n"
        "                        same events, same schema as a live session\n"
        "  --expect <note>       score-informed mode: confirm the expected\n"
        "                        note (e.g. C5, A4, F#5) with the bounded\n"
        "                        matcher — the game's confirmation path.\n"
        "                        Also feeds the note as a CONTENT candidate\n"
        "                        (presence/cents measured per attack)\n"
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
        "                        attack: '@<t_ms> midi midi ...' binds to the\n"
        "                        attack at that time, or plain 'midi ...' for\n"
        "                        the next attack; include notes still\n"
        "                        sustaining) — conditions the CONTENT\n"
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
    const char* input_path = nullptr;
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
        } else if (std::strcmp(argv[i], "--input") == 0 && i + 1 < argc) {
            input_path = argv[++i];
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
    if (input_path != nullptr) {
        std::printf("  Input: %s (offline run of the live pipeline)\n", input_path);
    } else {
        std::printf("  Ctrl+C to exit\n");
    }
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
    if (input_path == nullptr) {
        if (ma_device_init(nullptr, &config, &device) != MA_SUCCESS) {
            std::fprintf(stderr, "[ERROR] Could not open the audio capture device.\n");
            return -1;
        }
        ma_device_start(&device);
    }

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
    // Session clock = audio time, advanced per consumed block in both live
    // and --input mode: timestamps derive from samples, so a live session
    // and an offline run of the same audio agree.
    float session_ms = 0.0f;
    auto session_seconds = [&]() { return session_ms / 1000.0f; };

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
    std::vector<CandidateLine> cand_lists;
    if (candidates_path != nullptr) {
        cand_lists = load_candidates(candidates_path);
        if (cand_lists.empty()) {
            std::fprintf(stderr, "[ERROR] no candidates read from %s\n", candidates_path);
            return -1;
        }
    }
    std::vector<bool> cand_used(cand_lists.size(), false);
    kan::NoteDescriber desc(float(SAMPLE_RATE), inst.profile);
    kan::NoteEventStream ev_stream(4096);
    desc.subscribe(ev_stream);

    // The display is rendered FROM the event stream (the same facts the
    // JSONL log carries) — one schema for display, log and offline tools.
    // The validation artifact: one line per struck note at the end of the
    // log (its whole life in one place).
    struct NoteSummary {
        uint32_t id = 0;
        float onset = 0.0f, end = 0.0f, dur = 0.0f;
        float presence = 0.0f, sim = 0.0f, cents = 0.0f;
        bool has_cand = false;  // blind records carry no candidate facts
        int midi = -1;          // -1 = unresolved
        const char* tier = "warming-up";
    };
    std::vector<NoteSummary> score;
    struct StreamStats {
        int attacks = 0, mono = 0, poly = 0;
        int provisional = 0, provisional_unres = 0;
        int finals = 0, final_unres = 0;
        int cand_facts = 0, cand_strong = 0, cand_mid = 0, cand_weak = 0;
        int exprs = 0, closes = 0, vib_claims = 0;
        int dyn[7] = {0, 0, 0, 0, 0, 0, 0}; // warming-up, pp, p, mp, mf, f, ff
    } stats;

    // Candidate sets must be PENDING before the strike: records open at the
    // strike (one per struck note), so hypotheses cannot arrive after it.
    // Timed lines arm shortly before their attack (like the game's chart);
    // untimed lines arm after the previous attack (they target "the next
    // strike"); --expect mode falls back to the charted note itself.
    size_t attacks_seen = 0;
    auto build_set = [&](const CandidateLine& line) {
        kan::NoteDescriber::CandidateSet cs;
        cs.note_id = 0;
        cs.count = uint8_t(std::min<size_t>(line.midis.size(),
                                            kan::NoteDescriber::kMaxCandidates));
        for (size_t m = 0; m < cs.count; ++m) {
            // '~midi' = sustaining from an earlier attack (its own record);
            // plain midi = struck at the upcoming attack.
            const bool struck = line.midis[m] > 0.0f;
            cs.entries[m] = {0, std::fabs(line.midis[m]),
                             struck ? uint8_t(1) : uint8_t(0)};
        }
        return cs;
    };
    auto arm_next_attack = [&](kan::NoteDescriber& d, bool has_expect, float exp_midi) {
        for (size_t c = 0; c < cand_lists.size(); ++c) {
            if (cand_used[c]) continue;
            if (cand_lists[c].t_ms >= 0.0f) continue; // timed lines arm on time
            const auto cs = build_set(cand_lists[c]);
            if (d.set_candidates(cs)) {
                cand_used[c] = true;
                return;
            }
        }
        if (has_expect) {
            kan::NoteDescriber::CandidateSet cs;
            cs.note_id = 0;
            cs.count = 1;
            cs.entries[0] = {0, exp_midi, 1};
            d.set_candidates(cs);
        }
    };
    (void)attacks_seen;
    auto drain_events = [&]() {
        kan::NoteEvent batch[32];
        for (;;) {
            const size_t got = ev_stream.pop(batch, 32);
            if (got == 0) break;
            for (size_t k = 0; k < got; ++k) {
                const auto& e = batch[k];
                // Measured session summary (the data behind the claims).
                NoteSummary* ns = nullptr;
                for (auto& row : score) {
                    if (row.id == e.note_id) { ns = &row; break; }
                }
                if (ns == nullptr && e.kind == kan::NoteKind::Hit) {
                    score.push_back(NoteSummary{});
                    ns = &score.back();
                    ns->id = e.note_id;
                    ns->onset = e.u.hit.onset_ms;
                }
                switch (e.kind) {
                case kan::NoteKind::Hit:
                    ++stats.attacks;
                    break;
                case kan::NoteKind::Pitch:
                    if (e.stage == kan::PitchStage::Provisional) {
                        if (e.u.pitch.hz > 0.0f) ++stats.provisional;
                        else ++stats.provisional_unres;
                    } else if (e.stage == kan::PitchStage::Final && e.u.pitch.hz > 0.0f) {
                        ++stats.finals;
                        if (ns != nullptr) {
                            ns->midi = int(e.u.pitch.midi);
                            ns->cents = e.u.pitch.cents;
                        }
                    } else {
                        ++stats.final_unres;
                    }
                    break;
                case kan::NoteKind::Content:
                    if (e.klass == kan::ContentClass::Mono) ++stats.mono;
                    else if (e.klass == kan::ContentClass::Polyphonic) ++stats.poly;
                    else {
                        ++stats.cand_facts;
                        if (e.u.candidate.presence > 0.3f) ++stats.cand_strong;
                        else if (e.u.candidate.presence > 0.1f) ++stats.cand_mid;
                        else ++stats.cand_weak;
                        if (ns != nullptr && (e.u.candidate.presence > ns->presence
                                              || !ns->has_cand)) {
                            ns->presence = e.u.candidate.presence;
                            ns->sim = e.u.candidate.similarity;
                            ns->has_cand = true;
                        }
                    }
                    break;
                case kan::NoteKind::Expression: {
                    ++stats.exprs;
                    if (e.u.expression.vib_rate_hz > 0.0f) ++stats.vib_claims;
                    const int tier = int(e.u.expression.dynamics_tier);
                    if (tier >= 0 && tier < 7) ++stats.dyn[tier];
                    if (ns != nullptr) {
                        ns->tier = kan::dynamics_tier_name(kan::DynamicsTier(tier));
                    }
                    break;
                }
                case kan::NoteKind::Close:
                    ++stats.closes;
                    if (ns != nullptr) {
                        ns->end = e.u.close.end_ms;
                        ns->dur = e.u.close.duration_ms;
                    }
                    break;
                }

                if (e.kind == kan::NoteKind::Hit) {
                    ++attacks_seen;
                    arm_next_attack(desc, expect_note != nullptr, expected_midi);
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

        // Live tuner line (single-line refresh) — raw readout, live sessions
        // only. In --input mode the stream transcript IS the display.
        if (input_path == nullptr) {
            char bar[17];
            const int pos = std::clamp(int(std::lround(cents / 10.0f)) + 8, 0, 16);
            for (int i = 0; i < 17; ++i) bar[i] = (i == 8) ? '|' : (i == pos ? '#' : '.');
            bar[16] = '\0';
            std::printf("\r♪ %-4s %7.2f Hz  %+6.1f cents  %s  (+%.0f ms)%s   ",
                        midi_to_name(display_midi).c_str(), freq, cents, bar, ms_after, vib_str);
            std::fflush(stdout);
        }
    };

    // Per-block consumer: the SAME code path for the capture ring (live)
    // and for a decoded recording (--input) — live/offline equivalence. The
    // description stream sees every block of the session (its clock starts
    // at session start), so event times are session/file-absolute.
    auto consume_block = [&](const float* blk) {
        // Arm timed candidate lines shortly before their attack (the
        // strike opens records, so hypotheses must already be pending).
        for (size_t c = 0; c < cand_lists.size(); ++c) {
            if (cand_used[c] || cand_lists[c].t_ms < 0.0f) continue;
            if (session_ms < cand_lists[c].t_ms
                && session_ms + 250.0f >= cand_lists[c].t_ms) {
                const auto cs = build_set(cand_lists[c]);
                if (desc.set_candidates(cs)) cand_used[c] = true;
            }
        }
        if (use_gate && !gate_calibrated) {
            float cond[BLOCK_SIZE];
            pipeline.pre.process_block(blk, cond, BLOCK_SIZE);
            desc.process_block(cond, BLOCK_SIZE);
            while (desc.finalize_due()) {}
            drain_events();
            session_ms += 1000.0f * float(BLOCK_SIZE) / float(SAMPLE_RATE);

            calib.insert(calib.end(), blk, blk + BLOCK_SIZE);
            if (calib.size() >= calib_target) {
                pipeline.pre.calibrate(calib.data(), calib.size());
                const float floor_rms = pipeline.pre.gate().noise_floor_rms();
                pipeline.onset.set_noise_floor(floor_rms * floor_rms);
                desc.set_noise_floor(floor_rms * floor_rms);
                gate_calibrated = true;
                calib.clear();
                calib.shrink_to_fit();
                std::printf("[stage 0] noise gate calibrated: floor RMS = %.5f\n", floor_rms);
            }
            return;
        }

        auto r = pipeline.process(blk);

        // The description stream consumes the same stage-0 audio: facts as
        // they become available (HIT/provisional inline; long-window final
        // measurements via finalize_due — movable to a worker thread).
        desc.process_block(pipeline.conditioned_last, BLOCK_SIZE);
        while (desc.finalize_due()) {}
        drain_events();

        if (r.onset) {
            ++notes_played;
            vib_traj.clear(); // trajectory is per-note
        }
        if (r.pitch) on_pitch(r.pr, r.ms_after_onset);
        session_ms += 1000.0f * float(BLOCK_SIZE) / float(SAMPLE_RATE);
    };

    if (input_path != nullptr) {
        // Offline validation: the exact live pipeline over a recording.
        ma_decoder_config dcfg = ma_decoder_config_init(ma_format_f32, 1, SAMPLE_RATE);
        ma_decoder dec;
        if (ma_decoder_init_file(input_path, &dcfg, &dec) != MA_SUCCESS) {
            std::fprintf(stderr, "[ERROR] cannot open %s\n", input_path);
            return -1;
        }
        std::vector<float> file;
        float chunk[4096];
        for (;;) {
            ma_uint64 got = 0;
            if (ma_decoder_read_pcm_frames(&dec, chunk, 4096, &got) != MA_SUCCESS || got == 0) {
                break;
            }
            file.insert(file.end(), chunk, chunk + got);
        }
        ma_decoder_uninit(&dec);
        std::printf("input: %s (%.1f s) — live pipeline, offline\n",
                    input_path, double(file.size()) / double(SAMPLE_RATE));
        for (size_t off = 0; off + BLOCK_SIZE <= file.size(); off += BLOCK_SIZE) {
            consume_block(&file[off]);
        }
    } else {
        std::printf("Ready. Play your instrument!\n");
        float block[BLOCK_SIZE];
        while (g_running.load()) {
            if (!g_audio_ring.pop(block, BLOCK_SIZE)) {
                std::this_thread::sleep_for(std::chrono::microseconds(200));
                continue;
            }
            consume_block(block);
        }
    }
    desc.flush();
    drain_events();

    desc.flush();
    drain_events();

    // Measured session summary — the data behind the claims, straight from
    // the same events the display and the JSONL log carry.
    std::printf("\n\n--- note description stream ---\n");
    std::printf("records: %d | content: mono %d, polyphonic %d\n",
                stats.attacks, stats.mono, stats.poly);
    std::printf("pitch:   provisional %d (+%d unresolved), final %d (+%d unresolved)\n",
                stats.provisional, stats.provisional_unres,
                stats.finals, stats.final_unres);
    if (stats.cand_facts > 0) {
        std::printf("candidates: %d facts | presence >0.3: %d, 0.1-0.3: %d, <=0.1: %d\n",
                    stats.cand_facts, stats.cand_strong, stats.cand_mid, stats.cand_weak);
    }
    std::printf("expression: %d notes | vibrato claimed %d | dynamics: "
                "warmup %d, pp %d, p %d, mp %d, mf %d, f %d, ff %d\n",
                stats.exprs, stats.vib_claims, stats.dyn[0], stats.dyn[1], stats.dyn[2],
                stats.dyn[3], stats.dyn[4], stats.dyn[5], stats.dyn[6]);
    std::printf("closed: %d notes | stream drops: %u\n", stats.closes, ev_stream.dropped());
    std::printf("measurement budget: %zu fits, %.1f ms CPU total\n",
                desc.fit_count(), desc.fit_micros() / 1000.0);
    std::printf("\n--- measured score (per struck note: the validation artifact) ---\n");
    std::printf("  onset_ms  dur_ms  note  cents  presence  sim   tier\n");
    for (const auto& r : score) {
        char pitch[24], ev[24];
        if (r.midi >= 0) {
            std::snprintf(pitch, sizeof(pitch), "%-4s %+5.1f",
                          midi_to_name(float(r.midi)).c_str(), double(r.cents));
        } else {
            std::snprintf(pitch, sizeof(pitch), "%-4s %5s", "-", "unres");
        }
        if (r.has_cand) {
            std::snprintf(ev, sizeof(ev), "%7.2f %6.2f", double(r.presence), double(r.sim));
        } else {
            std::snprintf(ev, sizeof(ev), "%7s %6s", "-", "-");
        }
        std::printf("  %8.1f %7.0f  %s  %s   %s\n", double(r.onset),
                    double(r.dur), pitch, ev, r.tier);
    }
    std::printf("(facts are measurements: 'unresolved' = not measured, never 'wrong')\n");
    std::printf("\n--- tuner session summary ---\n");
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

    if (input_path == nullptr) {
        ma_device_uninit(&device);
    }
    return 0;
}
