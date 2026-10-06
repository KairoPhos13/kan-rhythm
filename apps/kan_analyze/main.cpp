// kan_analyze — song performance descriptor.
//
// Describes HOW a piece is played: per-note events (time, pitch, cents,
// duration, envelope, vibrato) plus song-level features (tempo, intonation,
// articulation mix, dynamic range, polyphonic/mixed regions). These are the
// SAME features a learner gets evaluated on, so a reference performance
// descriptor doubles as level-seed material and as the evaluation rubric.
//
// Sources: a real audio file (WAV/MP3/FLAC via miniaudio decoder) or a
// built-in synthesized scenario (--synth, test material for offline runs).
//
// Offline tool: consumer-thread analysis only, no real-time constraints.
#define MINIAUDIO_IMPLEMENTATION
#include "miniaudio.h"

#include "libkan/precondition.hpp"
#include "libkan/dsp_detector.hpp"
#include "libkan/instrument_profile.hpp"
#include "libkan/technique.hpp"

#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <string>
#include <vector>

namespace {

constexpr uint32_t SR = 48000;
constexpr size_t B = 128;

// ---------------------------------------------------------------------------
// Note record = one detected note event = one judgment-relevant feature set.
struct NoteRec {
    float onset_ms = 0.0f;
    float end_ms = 0.0f;
    float freq_hz = 0.0f;
    float cents = 0.0f;        ///< vs equal temperament of the nearest note
    int midi = 0;
    float duration_ms = 0.0f;
    float attack_ms = 0.0f;    ///< 10-90% of peak (envelope)
    float release_ms = 0.0f;
    float sustain_ratio = 0.0f;
    float peak_rms = 0.0f;
    float valid_ratio = 0.0f;  ///< blocks with pitch / blocks in note (<0.5 = mixed)
    bool reliable = true;      ///< false: polyphonic/hopping pitch (excluded)
    bool vibrato = false;
    float vib_rate_hz = 0.0f;
    float vib_depth_c = 0.0f;
};

struct Report {
    std::string source;
    std::vector<NoteRec> notes;
    size_t mixed_regions = 0;   ///< notes with valid_ratio < 0.5 (polyphony/duet)
    float duration_s = 0.0f;
};

// ---------------------------------------------------------------------------
// Synthesized scenarios (test material mirroring real-world corner cases).
void add_note(std::vector<float>& out, float t0, float freq, float dur,
              float amp, int partials, float vibrato_hz, float vib_depth_c,
              uint32_t seed) {
    const size_t at = size_t(t0 * SR);
    const size_t n = size_t(dur * SR);
    constexpr int kMaxH = 8;
    float phase[kMaxH] = {0};
    for (size_t i = 0; at + i < out.size() && i < n; ++i) {
        const float t = float(i) / SR;
        float s = 0.0f;
        if (t < 0.008f) {
            const uint32_t h = uint32_t(i) * 2654435761u + seed;
            s = 0.15f * (float(int((h ^ (h >> 13)) % 2000)) / 1000.0f - 1.0f)
              * (1.0f - t / 0.008f);
        } else {
            const float decay = std::exp(-1.2f * (t - 0.008f));
            // Vibrato = frequency modulation integrated into phase (a
            // t*vib(t) shortcut mixes in a huge spurious FM term — the
            // analyzer caught that as fake 20-46 Hz "vibrato").
            const float mod = (vibrato_hz > 0.0f && t > 0.15f)
                ? std::pow(2.0f, (vib_depth_c / 1200.0f)
                  * std::sin(2.0f * float(M_PI) * vibrato_hz * (t - 0.15f)))
                : 1.0f;
            for (int h = 1; h <= partials && h < kMaxH; ++h) {
                if (freq * float(h) > 16000.0f) break;
                phase[h] += 2.0f * float(M_PI) * freq * float(h) * mod / SR;
                s += (0.5f / float(h)) * decay * std::sin(phase[h]);
            }
        }
        out[at + i] += amp * s;
    }
}

std::vector<float> make_scenario(const char* name) {
    std::vector<float> s;
    if (std::strcmp(name, "ocarina") == 0) {
        // Monophonic melody: legato with vibrato + staccato runs.
        s.assign(size_t(10.0f * SR), 0.0f);
        const float notes[] = {523.25f, 587.33f, 659.26f, 698.46f, 783.99f};
        for (int rep = 0; rep < 4; ++rep) {
            for (int i = 0; i < 5; ++i) {
                const float t0 = 0.5f + float(rep * 5 + i) * 0.65f;
                const bool staccato = (rep % 2 == 1);
                add_note(s, t0, notes[i], staccato ? 0.22f : 0.6f, 0.5f, 6,
                         staccato ? 0.0f : 5.2f, staccato ? 0.0f : 38.0f,
                         uint32_t(i + rep * 7));
            }
        }
    } else if (std::strcmp(name, "piano") == 0) {
        // Polyphonic: melody + block chords + pedal sustain.
        s.assign(size_t(12.0f * SR), 0.0f);
        const float melody[] = {440.0f, 493.88f, 523.25f, 587.33f};
        for (int bar = 0; bar < 4; ++bar) {
            const float t0 = 0.5f + float(bar) * 2.5f;
            const float chord[3] = {261.63f, 329.63f, 392.0f};
            for (int c = 0; c < 3; ++c) {
                add_note(s, t0, chord[c], 2.2f, 0.33f, 8, 0, 0, uint32_t(10 + c));
            }
            for (int m = 0; m < 4; ++m) {
                add_note(s, t0 + float(m) * 0.55f, melody[m], 0.5f, 0.42f, 8,
                         0, 0, uint32_t(20 + m));
            }
        }
    } else if (std::strcmp(name, "duet") == 0) {
        // Melody instrument + piano companion (competing fundamentals).
        s.assign(size_t(10.0f * SR), 0.0f);
        const float melody[] = {587.33f, 659.26f, 698.46f, 783.99f};
        for (int n = 0; n < 12; ++n) {
            add_note(s, 0.5f + float(n) * 0.7f, melody[n % 4], 0.62f, 0.5f, 6,
                     4.8f, 30.0f, uint32_t(30 + n));
            // Companion: soft chord stabs between melody notes.
            add_note(s, 0.85f + float(n) * 0.7f, 261.63f, 0.45f, 0.30f, 8, 0, 0, 41u);
            add_note(s, 0.85f + float(n) * 0.7f, 329.63f, 0.45f, 0.30f, 8, 0, 0, 42u);
        }
    } else if (std::strcmp(name, "fx") == 0) {
        // Delay/reverb tail + noise floor: ghost-onset territory.
        s.assign(size_t(10.0f * SR), 0.0f);
        std::vector<float> dry(size_t(10.0f * SR), 0.0f);
        for (int n = 0; n < 10; ++n) {
            add_note(dry, 0.5f + float(n) * 0.9f, 440.0f * std::pow(2.0f, float(n % 5) / 12.0f),
                     0.4f, 0.5f, 6, 0, 0, uint32_t(50 + n));
        }
        for (size_t i = 0; i < s.size(); ++i) {
            s[i] = dry[i];
            const size_t d1 = size_t(0.23f * SR), d2 = size_t(0.46f * SR);
            if (i >= d1) s[i] += 0.38f * dry[i - d1];
            if (i >= d2) s[i] += 0.16f * dry[i - d2];
            const uint32_t h = uint32_t(i) * 2654435761u;
            s[i] += 0.004f * (float(int((h ^ (h >> 13)) % 2000)) / 1000.0f - 1.0f);
        }
    }
    // Peak-normalize.
    float peak = 1e-6f;
    for (float v : s) peak = std::max(peak, std::fabs(v));
    for (float& v : s) v *= 0.6f / peak;
    return s;
}

// ---------------------------------------------------------------------------
// Analysis pass: stage 0 -> onset -> blanking -> blind pitch -> note records.
Report analyze(const std::vector<float>& sig, const char* source,
               const kan::InstrumentProfile& profile, float window_ms) {
    Report rep;
    rep.source = source;
    rep.duration_s = float(sig.size()) / SR;

    kan::Preconditioner pre{float(SR)};
    kan::HybridDetector det{float(SR)};
    det.set_profile(profile);
    const size_t win = size_t(window_ms / 1000.0f * SR);
    std::vector<float> rolling(win, 0.0f);

    kan::VibratoAnalyzer vib;

    bool in_note = false;
    NoteRec cur;
    float ms_since_onset = 0.0f;
    float silent_ms = 0.0f;

    for (size_t off = 0; off + B <= sig.size(); off += B) {
        float cond[B];
        pre.process_block(&sig[off], cond, B);
        double acc = 0.0;
        for (size_t i = 0; i < B; ++i) acc += double(cond[i]) * double(cond[i]);
        const float rms = float(std::sqrt(acc / double(B)));
        const float t_ms = 1000.0f * float(off + B) / SR;

        std::move(rolling.begin() + B, rolling.end(), rolling.begin());
        std::copy(cond, cond + B, rolling.end() - B);

        if (det.process_onset_block(cond, B)) {
            if (in_note && (t_ms - cur.onset_ms) > 150.0f) {
                // Close previous note on a new attack.
                cur.end_ms = t_ms;
                rep.notes.push_back(cur);
            }
            in_note = true;
            cur = NoteRec{};
            cur.onset_ms = t_ms;
            ms_since_onset = 0.0f;
            silent_ms = 0.0f;
        }
        if (!in_note) continue;

        ms_since_onset += 1000.0f * float(B) / SR;
        if (rms < 0.01f) silent_ms += 1000.0f * float(B) / SR;

        if (!det.in_transient_blanking(ms_since_onset)) {
            auto p = det.detect_pitch(rolling);
            if (p.valid) {
                cur.freq_hz = p.frequency_hz;
                cur.cents = p.cents_offset;
                cur.midi = int(std::lround(69.0f + 12.0f * std::log2(p.frequency_hz / 440.0f)));
            }
        }

        // Note ends after sustained silence (no new onset).
        if (silent_ms > 120.0f) {
            cur.end_ms = t_ms - silent_ms;
            rep.notes.push_back(cur);
            in_note = false;
        }
    }
    if (in_note) {
        cur.end_ms = 1000.0f * float(sig.size()) / SR;
        rep.notes.push_back(cur);
    }

    // --- per-note features -----------------------------------------------
    kan::EnvelopeAnalyzer env;
    for (auto& n : rep.notes) {
        n.duration_ms = n.end_ms - n.onset_ms;
    }
    // Envelope + validity need per-note series; recompute from the last
    // note's buffers only for the final note — simpler: track during the
    // loop. (Second pass over the signal is cheap at offline speed.)
    // Re-run with per-note capture:
    {
        // Capture series per note in one more streaming pass.
        struct Series { std::vector<float> rms; size_t valid = 0, blocks = 0;
                        std::vector<float> cents; std::vector<float> t;
                        std::vector<float> freqs; float cents_freq0 = 440.0f; };
        std::vector<Series> series(rep.notes.size());
        in_note = false;
        size_t idx = 0;
        ms_since_onset = 0.0f;
        silent_ms = 0.0f;
        std::vector<float> rolling2(win, 0.0f);
        kan::HybridDetector det2{float(SR)};
        det2.set_profile(profile);
        kan::Preconditioner pre2{float(SR)};
        for (size_t off = 0; off + B <= sig.size(); off += B) {
            float cond[B];
            pre2.process_block(&sig[off], cond, B);
            double acc = 0.0;
            for (size_t i = 0; i < B; ++i) acc += double(cond[i]) * double(cond[i]);
            const float rms = float(std::sqrt(acc / double(B)));
            std::move(rolling2.begin() + B, rolling2.end(), rolling2.begin());
            std::copy(cond, cond + B, rolling2.end() - B);
            if (det2.process_onset_block(cond, B)) {
                if (in_note && idx + 1 < series.size()) ++idx;
                else if (!in_note && idx < series.size() && !series[idx].rms.empty()) ++idx;
                if (idx >= series.size()) idx = series.size() - 1;
                in_note = true;
                ms_since_onset = 0.0f;
                silent_ms = 0.0f;
            }
            if (!in_note || idx >= series.size()) continue;
            ms_since_onset += 1000.0f * float(B) / SR;
            series[idx].blocks++;
            series[idx].rms.push_back(rms);
            if (rms < 0.01f) silent_ms += 1000.0f * float(B) / SR;
            if (!det2.in_transient_blanking(ms_since_onset)) {
                auto p = det2.detect_pitch(rolling2);
                if (p.valid) {
                    series[idx].valid++;
                    series[idx].cents.push_back(p.cents_offset);
                    series[idx].t.push_back(ms_since_onset);
                    if (series[idx].freqs.empty()) series[idx].cents_freq0 = p.frequency_hz;
                    series[idx].freqs.push_back(p.frequency_hz);
                }
            }
            if (silent_ms > 120.0f) in_note = false;
        }
        for (size_t i = 0; i < rep.notes.size() && i < series.size(); ++i) {
            auto& n = rep.notes[i];
            auto& s = series[i];
            n.valid_ratio = s.blocks ? float(s.valid) / float(s.blocks) : 0.0f;
            if (n.valid_ratio < 0.5f) ++rep.mixed_regions;
            if (!s.rms.empty()) {
                auto m = env.measure(s.rms.data(), s.rms.size(), 1000.0f * float(B) / SR);
                n.attack_ms = m.attack_ms;
                n.release_ms = m.release_ms;
                n.sustain_ratio = m.sustain_ratio;
                n.peak_rms = m.peak;
            }
            if (!s.cents.empty()) {
                // Continuous deviation relative to a FIXED reference (the
                // note's first reading) — nearest-note cents wrap around at
                // ±50 and destroy vibrato trajectories.
                const float m0 = 69.0f + 12.0f * std::log2(s.cents_freq0 / 440.0f);
                const float ref_midi = std::floor(m0 + 0.5f);
                float dev_sum = 0.0f, prev_dev = 0.0f, max_dev = 0.0f;
                size_t hops = 0;
                kan::TrajectoryBuffer tt;
                for (size_t k = 0; k < s.freqs.size(); ++k) {
                    const float mk = 69.0f + 12.0f * std::log2(s.freqs[k] / 440.0f);
                    const float dev = (mk - ref_midi) * 100.0f;
                    dev_sum += dev;
                    max_dev = std::max(max_dev, std::fabs(dev));
                    if (k > 0 && std::fabs(dev - prev_dev) > 200.0f) ++hops;
                    prev_dev = dev;
                    tt.add(s.t[k], dev);
                }
                n.cents = dev_sum / float(s.freqs.size());
                n.midi = int(ref_midi);
                // A tracker hopping between sources (companion instrument,
                // chord partials) is unreliable pitch, even when every block
                // "valid": flag it and exclude it from the statistics.
                const float hop_ratio = s.freqs.size() > 1
                    ? float(hops) / float(s.freqs.size() - 1) : 0.0f;
                n.reliable = n.valid_ratio >= 0.5f && max_dev <= 200.0f
                             && hop_ratio <= 0.2f;
                if (!n.reliable && s.t.size() >= 8) {
                    // fall through: no vibrato claims on unreliable pitch
                } else if (s.t.size() >= 8) {
                    auto vm = vib.measure(tt);
                    // libkan's `periodic` is conservative (>=3 cycles); the
                    // descriptor also accepts clear 2+-cycle oscillations.
                    n.vibrato = vm.periodic
                             || (vm.rate_hz > 1.0f && vm.depth_cents >= 15.0f);
                    n.vib_rate_hz = vm.rate_hz;
                    n.vib_depth_c = vm.depth_cents;
                }
            }
        }
    }
    return rep;
}

// ---------------------------------------------------------------------------
void print_report(const Report& r, bool json, FILE* out) {
    // Song-level features = the learner-evaluation rubric. MIX regions
    // (polyphony/duet overlaps) are unreliable for mono pitch — listed and
    // counted, but excluded from the intonation/vibrato statistics.
    std::vector<float> iois;
    for (size_t i = 1; i < r.notes.size(); ++i) {
        iois.push_back(r.notes[i].onset_ms - r.notes[i - 1].onset_ms);
    }
    std::vector<float> srt = iois;
    std::sort(srt.begin(), srt.end());
    const float med_ioi = srt.empty() ? 0.0f : srt[srt.size() / 2];
    const float bpm = med_ioi > 1.0f ? 60000.0f / med_ioi : 0.0f;

    float abs_cents = 0.0f, max_cents = 0.0f;
    int intune = 0, vib_n = 0, staccato = 0, evaluated = 0;
    float vib_rate = 0.0f, vib_depth = 0.0f;
    float f_lo = 1e9f, f_hi = 0.0f;
    for (const auto& n : r.notes) {
        if (!n.reliable || n.freq_hz <= 0.0f) continue; // MIX/HOP excluded
        ++evaluated;
        abs_cents += std::fabs(n.cents);
        max_cents = std::max(max_cents, std::fabs(n.cents));
        if (std::fabs(n.cents) < 25.0f) ++intune;
        if (n.vibrato) { ++vib_n; vib_rate += n.vib_rate_hz; vib_depth += n.vib_depth_c; }
        if (n.duration_ms < 300.0f && n.sustain_ratio < 0.5f) ++staccato;
        f_lo = std::min(f_lo, n.freq_hz);
        f_hi = std::max(f_hi, n.freq_hz);
    }
    const size_t N = r.notes.size();

    if (json) {
        fprintf(out, "{\n  \"source\": \"%s\",\n  \"duration_s\": %.2f,\n", r.source.c_str(), double(r.duration_s));
        fprintf(out, "  \"notes\": %zu,\n  \"mixed_regions\": %zu,\n  \"tempo_bpm\": %.1f,\n",
               N, r.mixed_regions, double(bpm));
        fprintf(out, "  \"intonation_mean_abs_cents\": %.1f,\n  \"intonation_max_abs_cents\": %.1f,\n",
               double(N ? abs_cents / N : 0.0), double(max_cents));
        fprintf(out, "  \"events\": [\n");
        for (size_t i = 0; i < N; ++i) {
            const auto& n = r.notes[i];
            fprintf(out, "    {\"onset_ms\": %.1f, \"midi\": %d, \"hz\": %.2f, \"cents\": %.1f, "
                   "\"dur_ms\": %.1f, \"attack_ms\": %.1f, \"sustain\": %.2f, "
                   "\"vibrato\": %s, \"vib_hz\": %.2f, \"valid_ratio\": %.2f, "
                   "\"reliable\": %s}%s\n",
                   double(n.onset_ms), n.midi, double(n.freq_hz), double(n.cents),
                   double(n.duration_ms), double(n.attack_ms), double(n.sustain_ratio),
                   n.vibrato ? "true" : "false", double(n.vib_rate_hz),
                   double(n.valid_ratio),
                   n.reliable ? "true" : "false", i + 1 < N ? "," : "");
        }
        fprintf(out, "  ]\n}\n");
        return;
    }

    fprintf(out, "=== kan_analyze performance descriptor ===\n");
    fprintf(out, "source: %s | %.1f s | notes %zu, evaluated %d, mixed/polyphonic regions %zu (excluded)\n",
           r.source.c_str(), double(r.duration_s), N, evaluated, r.mixed_regions);
    fprintf(out, "tempo (median IOI): %.1f BPM (%.0f ms)\n", double(bpm), double(med_ioi));
    fprintf(out, "intonation: mean |err| %.1f c, max %.1f c, in-tune(<25c) %d/%d\n",
           double(evaluated ? abs_cents / evaluated : 0.0), double(max_cents), intune, evaluated);
    fprintf(out, "vibrato: %d notes", vib_n);
    if (vib_n) fprintf(out, " (avg %.1f Hz, %.0f c depth)", double(vib_rate / vib_n), double(vib_depth / vib_n));
    fprintf(out, " | staccato: %d | range: %.0f-%.0f Hz\n", staccato, double(f_lo), double(f_hi));
    fprintf(out, "\n #    t(ms)  note   Hz      cents  dur   atk   rel   sus  vib(Hz/c)  mix\n");
    for (size_t i = 0; i < N; ++i) {
        const auto& n = r.notes[i];
        char name[16] = "-";
        if (n.freq_hz > 0.0f) {
            static const char* NAMES[12] = {"C","C#","D","D#","E","F","F#","G","G#","A","A#","B"};
            std::snprintf(name, sizeof(name), "%s%d", NAMES[((n.midi % 12) + 12) % 12], (n.midi / 12) - 1);
        }
        fprintf(out, "%2zu %8.0f %-5s %7.1f %+6.1f %5.0f %5.0f %5.0f %5.2f  %s  %s\n",
               i + 1, double(n.onset_ms), name, double(n.freq_hz), double(n.cents),
               double(n.duration_ms), double(n.attack_ms), double(n.release_ms),
               double(n.sustain_ratio),
               n.vibrato ? "vib" : "  -",
               !n.reliable ? (n.valid_ratio < 0.5f ? "MIX" : "HOP") : " -");
    }
}

} // namespace

int main(int argc, char** argv) {
    const char* input = nullptr;
    const char* synth = nullptr;
    const char* profile_name = "ocarina";
    const char* out_wav = nullptr;
    const char* json_out = nullptr;
    bool json = false;

    for (int i = 1; i < argc; ++i) {
        if (std::strcmp(argv[i], "--synth") == 0 && i + 1 < argc) synth = argv[++i];
        else if (std::strcmp(argv[i], "--instrument") == 0 && i + 1 < argc) profile_name = argv[++i];
        else if (std::strcmp(argv[i], "--json") == 0) json = true;
        else if (std::strcmp(argv[i], "--json-out") == 0 && i + 1 < argc) json_out = argv[++i];
        else if (std::strcmp(argv[i], "--out") == 0 && i + 1 < argc) out_wav = argv[++i];
        else if (std::strcmp(argv[i], "--help") == 0) {
            std::printf("kan_analyze — song performance descriptor\n"
                        "  kan_analyze <audio.wav|mp3|flac> [--instrument ocarina|flute|"
                        "recorder|guitar|piano] [--json|--json-out f.json]\n"
                        "  kan_analyze --synth <ocarina|piano|duet|fx> [...] [--out f.wav]\n"
                        "  --out <f.wav>  also write the source audio (hear the scenario)\n");
            return 0;
        } else input = argv[i];
    }
    if (!input && !synth) {
        std::fprintf(stderr, "[ERROR] need an audio file or --synth <scenario>. --help for usage.\n");
        return -1;
    }

    kan::InstrumentProfile profile = kan::InstrumentProfile::wind_bowed();
    float window_ms = 2048.0f / SR * 1000.0f; // 2048 samples
    if (std::strcmp(profile_name, "piano") == 0) {
        profile = kan::InstrumentProfile::keyboard();
        window_ms = 4096.0f / SR * 1000.0f; // resolve sub-95 Hz blind
    } else if (std::strcmp(profile_name, "guitar") == 0) {
        profile = kan::InstrumentProfile::plucked();
    } else if (std::strcmp(profile_name, "flute") == 0 || std::strcmp(profile_name, "recorder") == 0) {
        profile = kan::InstrumentProfile::wind_bowed();
    }

    std::vector<float> sig;
    std::string source;
    if (synth) {
        sig = make_scenario(synth);
        source = std::string("synth:") + synth;
    } else {
        ma_decoder_config cfg = ma_decoder_config_init(ma_format_f32, 1, SR);
        ma_decoder dec;
        if (ma_decoder_init_file(input, &cfg, &dec) != MA_SUCCESS) {
            std::fprintf(stderr, "[ERROR] cannot open %s\n", input);
            return -1;
        }
        float chunk[4096];
        for (;;) {
            ma_uint64 got = 0;
            if (ma_decoder_read_pcm_frames(&dec, chunk, 4096, &got) != MA_SUCCESS || got == 0) break;
            sig.insert(sig.end(), chunk, chunk + got);
        }
        ma_decoder_uninit(&dec);
        source = input;
    }

    auto report = analyze(sig, source.c_str(), profile, window_ms);

    // Optional: write the source audio (so a synthesized scenario or a
    // decoded recording can actually be listened to).
    if (out_wav != nullptr) {
        ma_encoder_config ecfg = ma_encoder_config_init(ma_encoding_format_wav, ma_format_f32, 1, SR);
        ma_encoder enc;
        if (ma_encoder_init_file(out_wav, &ecfg, &enc) != MA_SUCCESS) {
            std::fprintf(stderr, "[ERROR] cannot write %s\n", out_wav);
            return -1;
        }
        ma_uint64 written = 0;
        ma_encoder_write_pcm_frames(&enc, sig.data(), sig.size(), &written);
        ma_encoder_uninit(&enc);
        std::fprintf(stderr, "audio written: %s (%.1f s)\n", out_wav, double(sig.size()) / SR);
    }

    if (json_out != nullptr) {
        FILE* f = std::fopen(json_out, "w");
        if (f == nullptr) {
            std::fprintf(stderr, "[ERROR] cannot write %s\n", json_out);
            return -1;
        }
        print_report(report, true, f);
        std::fclose(f);
    }
    print_report(report, json, stdout); // text report always (also with --json-out)
    return 0;
}
