// harmonic_probe — offline evidence probe for score-informed chord
// validation on REAL polyphonic piano material (Phase 1 of the
// note-description-stream work: "does HarmonicChordValidator, proven only on
// synthesized guitar triads, describe a real piano recording honestly?").
//
// Test-side tool: it needs a real recording (test_results/, gitignored), so
// it is built with the test suite but NOT registered as a ctest. The
// candidate annotation lives in harmonic_probe_notes.hpp.
//
//   libkan_harmonic_probe --dump <wav>     spectral inspection per attack
//   libkan_harmonic_probe --measure <wav>  candidate measurement report
//
// --dump prints, per annotated attack, the long-window spectral content at
// +200 ms and +800 ms after the onset: harmonic-sum fundamental scores
// (which pitches look sounding) and the top spectral peaks (independent
// evidence the candidate lists are checked against).
//
// --measure runs HarmonicChordValidator over each annotated attack's
// candidates (presence / refined cents per candidate), its negative
// controls (false presence), window lengths 1024 (21 ms gate budget) vs
// 8192 (170 ms — the low-register question), and the decay trajectory
// (+8 / +200 / +800 / +1500 ms — pedal behavior). It also prints blind
// mono-vs-polyphonic features per attack (no candidates involved) against
// the annotated label.
//
// Reports only — no verdicts: every number here is a measurement of the
// validator against hand-written hypotheses and independent spectra.
#include "libkan/harmonic_chord.hpp"
#include "libkan/chord_goertzel.hpp"

#include <kiss_fftr.h>

#include "wav_io.hpp"
#include "harmonic_probe_notes.hpp"

#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <vector>

namespace {

constexpr size_t kFft = 131072;   ///< zero-padded transform (bin ~0.37 Hz)
constexpr size_t kSpecWin = 16384; ///< 341 ms Hann window fed to the FFT
constexpr float kBandLo = 55.0f;
constexpr float kBandHi = 2500.0f;

/// Magnitude spectrum of one Hann window (test-side; allocation ok).
struct Spectrum {
    std::vector<float> mag; // kFft/2+1
    float sr = 48000.0f;
    float bin_hz = 48000.0f / float(kFft);

    Spectrum() : mag(kFft / 2 + 1, 0.0f) {}

    void compute(const float* sig, size_t sig_len, size_t start) {
        kiss_fftr_cfg cfg = kiss_fftr_alloc(int(kFft), 0, nullptr, nullptr);
        std::vector<float> buf(kFft, 0.0f);
        std::vector<kiss_fft_cpx> out(kFft / 2 + 1);
        for (size_t i = 0; i < kSpecWin && start + i < sig_len; ++i) {
            const float w = 0.5f - 0.5f * std::cos(2.0f * float(M_PI) * float(i)
                                                   / float(kSpecWin - 1));
            buf[i] = sig[start + i] * w;
        }
        kiss_fftr(cfg, buf.data(), out.data());
        for (size_t k = 0; k <= kFft / 2; ++k) {
            mag[k] = std::sqrt(out[k].r * out[k].r + out[k].i * out[k].i);
        }
        std::free(cfg);
    }

    /// Magnitude at an arbitrary frequency (linear bin interpolation).
    float at(float f) const {
        const float bin = f / bin_hz;
        const size_t k = size_t(bin);
        if (k + 1 >= mag.size()) return 0.0f;
        const float frac = bin - float(k);
        return mag[k] * (1.0f - frac) + mag[k + 1] * frac;
    }
};

/// Harmonic-sum fundamental score: how well a harmonic series at f0 (1/h
/// weighted, first 6 harmonics) matches the spectrum. The "spectral ear"
/// for candidate curation and for the blind best-single-note feature.
double harmonic_sum(const Spectrum& sp, float f0, int harmonics = 6) {
    double s = 0.0;
    for (int h = 1; h <= harmonics; ++h) {
        const float f = f0 * float(h);
        if (f > kBandHi) break;
        s += double(sp.at(f)) / double(h);
    }
    return s;
}

float midi_to_hz(float midi) {
    return 440.0f * std::pow(2.0f, (midi - 69.0f) / 12.0f);
}

float hz_to_midi(float f) {
    return 69.0f + 12.0f * std::log2(f / 440.0f);
}

struct Peak {
    float f = 0.0f;
    float mag = 0.0f;
};

/// Top spectral peaks in the band (local maxima, parabolic interpolation).
std::vector<Peak> top_peaks(const Spectrum& sp, size_t max_n, float rel_db) {
    std::vector<Peak> peaks;
    const size_t k_lo = size_t(kBandLo / sp.bin_hz) + 1;
    const size_t k_hi = std::min(sp.mag.size() - 2, size_t(kBandHi / sp.bin_hz));
    float band_max = 0.0f;
    for (size_t k = k_lo; k <= k_hi; ++k) band_max = std::max(band_max, sp.mag[k]);
    const float floor_mag = band_max * std::pow(10.0f, rel_db / 20.0f);
    for (size_t k = k_lo; k <= k_hi; ++k) {
        if (sp.mag[k] < floor_mag) continue;
        if (sp.mag[k] <= sp.mag[k - 1] || sp.mag[k] < sp.mag[k + 1]) continue;
        const float a = sp.mag[k - 1], b = sp.mag[k], c = sp.mag[k + 1];
        const float shift = 0.5f * (a - c) / (a - 2.0f * b + c + 1e-20f);
        peaks.push_back({(float(k) + shift) * sp.bin_hz, b});
    }
    std::sort(peaks.begin(), peaks.end(),
              [](const Peak& x, const Peak& y) { return x.mag > y.mag; });
    if (peaks.size() > max_n) peaks.resize(max_n);
    return peaks;
}

/// Best fundamental on a 5-cent midi grid (28..92) by harmonic sum.
void best_fundamentals(const Spectrum& sp, float* out_midi, float* out_score, size_t n) {
    struct Cand { float midi; double score; };
    std::vector<Cand> all;
    for (float m = 28.0f; m <= 92.0f; m += 0.05f) {
        all.push_back({m, harmonic_sum(sp, midi_to_hz(m))});
    }
    std::sort(all.begin(), all.end(),
              [](const Cand& a, const Cand& b) { return a.score > b.score; });
    size_t taken = 0;
    for (const auto& c : all) {
        bool too_close = false;
        for (size_t i = 0; i < taken; ++i) {
            if (std::fabs(out_midi[i] - c.midi) < 2.0f) too_close = true;
        }
        if (too_close) continue;
        out_midi[taken] = c.midi;
        out_score[taken] = float(c.score);
        if (++taken == n) break;
    }
}

// ---------------------------------------------------------------------------
// --dump: spectral inspection per attack (the curation aid for the
// hand-written candidate lists).
void dump(const std::vector<float>& sig, float sr) {
    const size_t offsets[] = {size_t(0.200f * sr), size_t(0.800f * sr)};
    const char* labels[] = {"+200ms", "+800ms"};
    for (size_t a = 0; a < kAttackCount; ++a) {
        const auto& at = kAttacks[a];
        std::printf("== %s t=%.0fms\n", at.id, double(at.t_ms));
        for (size_t w = 0; w < 2; ++w) {
            const size_t start = size_t(at.t_ms / 1000.0f * sr) + offsets[w];
            if (start + kSpecWin >= sig.size()) continue;
            Spectrum sp;
            sp.sr = sr;
            sp.bin_hz = sr / float(kFft);
            sp.compute(sig.data(), sig.size(), start);

            float fmidi[10], fscore[10];
            best_fundamentals(sp, fmidi, fscore, 10);
            std::printf("  %s fundamentals:", labels[w]);
            for (size_t i = 0; i < 10; ++i) {
                std::printf(" %.2f", double(fmidi[i]));
            }
            std::printf("\n");
            const auto peaks = top_peaks(sp, 16, -35.0f);
            std::printf("  %s peaks:     ", labels[w]);
            for (const auto& p : peaks) {
                std::printf(" %.1fHz(%.2f)", double(p.f), double(hz_to_midi(p.f)));
            }
            std::printf("\n");
        }
    }
}

// ---------------------------------------------------------------------------
// --measure: HarmonicChordValidator over the annotated candidates.
struct CandRun {
    float presence[8] = {0};
    float cents[8] = {0};
    float similarity = 0.0f;
    bool valid = false;
};

CandRun run_validator(const std::vector<float>& sig, float sr, size_t start,
                      size_t len, const float* midi, size_t n) {
    CandRun r;
    if (n == 0 || n > kan::HarmonicChordValidator::kMaxNotes) return r;
    if (start + len > sig.size()) return r;
    kan::HarmonicChordValidator v(sr, 8192);
    if (!v.set_expected_chord(midi, n)) return r;
    const auto res = v.validate(sig.data() + start, len);
    r.similarity = res.similarity;
    r.valid = res.valid;
    for (size_t k = 0; k < n; ++k) {
        r.presence[k] = res.note_presence[k];
        r.cents[k] = v.refined_cents(k);
    }
    return r;
}

void print_run(const char* tag, const CandRun& r, const float* midi, size_t n) {
    std::printf("  %s sim=%.3f valid=%d |", tag, double(r.similarity), r.valid ? 1 : 0);
    for (size_t k = 0; k < n; ++k) {
        std::printf(" %.0f:p%.2f:%+.0fc", double(midi[k]), double(r.presence[k]),
                    double(r.cents[k]));
    }
    std::printf("\n");
}

/// Independent evidence for one candidate: the nearest spectral line to its
/// f0 within +/-40 cents (position and level vs the band's strongest line).
void f0_line(const Spectrum& sp, float midi, bool& found, float& cents, float& db) {
    const float f0 = midi_to_hz(midi);
    const size_t k0 = size_t(f0 / sp.bin_hz);
    const size_t lo = k0 > 4 ? k0 - 4 : 1;
    const size_t hi = std::min(sp.mag.size() - 2, k0 + 5);
    size_t best = 0;
    for (size_t k = lo; k <= hi; ++k) {
        if (sp.mag[k] > sp.mag[best]) best = k;
    }
    float band_max = 0.0f;
    for (size_t k = size_t(kBandLo / sp.bin_hz); k <= size_t(kBandHi / sp.bin_hz); ++k) {
        band_max = std::max(band_max, sp.mag[k]);
    }
    const float a = sp.mag[best - 1], b = sp.mag[best], c = sp.mag[best + 1];
    const float shift = 0.5f * (a - c) / (a - 2.0f * b + c + 1e-20f);
    const float f = (float(best) + shift) * sp.bin_hz;
    cents = 1200.0f * std::log2(f / f0);
    db = 20.0f * std::log10(std::max(b, 1e-9f) / std::max(band_max, 1e-9f));
    found = std::fabs(cents) <= 40.0f && db > -35.0f;
}

/// Blind mono-vs-polyphonic separation summary: best single-threshold
/// accuracy per feature (mono called when value >= threshold; the peaks
/// feature is negated so the rule direction is uniform).
void print_separation(const std::vector<float>& vals, const std::vector<bool>& mono,
                      const char* name) {
    double best_acc = 0.0, best_t = 0.0;
    for (size_t i = 0; i <= vals.size(); ++i) {
        const double t = (i == 0) ? double(vals[0]) - 1.0
                                  : (i == vals.size() ? double(vals[i - 1]) + 1.0
                                      : (double(vals[i - 1]) + double(vals[i])) / 2.0);
        size_t ok = 0;
        for (size_t k = 0; k < vals.size(); ++k) {
            const bool call_mono = double(vals[k]) >= t;
            if (call_mono == mono[k]) ++ok;
        }
        const double acc = double(ok) / double(vals.size());
        if (acc > best_acc) { best_acc = acc; best_t = t; }
    }
    std::printf("  %s: best threshold %.3f -> accuracy %.2f (%zu mono / %zu poly)\n",
                name, best_t, best_acc,
                size_t(std::count(mono.begin(), mono.end(), true)),
                mono.size() - size_t(std::count(mono.begin(), mono.end(), true)));
}

void measure(const std::vector<float>& sig, float sr) {
    std::vector<float> cap_v, sim1_v, peak_v;
    std::vector<bool> mono_l;
    for (size_t a = 0; a < kAttackCount; ++a) {
        const auto& at = kAttacks[a];
        const char* label = at.n_sounding <= 1 ? "mono" : (at.n_sounding >= 3 ? "poly" : "duo");
        std::printf("== %s t=%.0fms label=%s sounding=%d controls=%d\n", at.id,
                    double(at.t_ms), label, at.n_sounding, at.n_controls);
        if (at.n_sounding == 0) {
            std::printf("  (no annotation yet)\n");
            continue;
        }

        float cand[8] = {0};
        for (int i = 0; i < at.n_sounding; ++i) cand[i] = float(at.sounding[i]);
        const size_t n_cand = std::min<size_t>(size_t(at.n_sounding),
                                               kan::HarmonicChordValidator::kMaxNotes);
        const size_t base = size_t(at.t_ms / 1000.0f * sr);

        // Gate-budget window (1024 = 21 ms) along the decay, then the
        // long-window comparison (8192 = 170 ms) at +200 ms.
        const size_t offs[] = {size_t(0.008f * sr), size_t(0.200f * sr),
                               size_t(0.800f * sr), size_t(1.500f * sr)};
        const char* off_tags[] = {"+8ms/1024", "+200ms/1024", "+800ms/1024", "+1500ms/1024"};
        for (size_t w = 0; w < 4; ++w) {
            if (base + offs[w] + 1024 > sig.size()) continue;
            print_run(off_tags[w], run_validator(sig, sr, base + offs[w], 1024, cand, n_cand),
                      cand, n_cand);
        }
        if (base + size_t(0.200f * sr) + 8192 <= sig.size()) {
            print_run("+200ms/8192",
                      run_validator(sig, sr, base + size_t(0.200f * sr), 8192, cand, n_cand),
                      cand, n_cand);
        }

        // Independent f0-line evidence per candidate (sounding + controls):
        // what the spectrum itself says at the candidate's fundamental.
        const size_t fstart = base + size_t(0.200f * sr);
        if (fstart + kSpecWin < sig.size()) {
            Spectrum sp;
            sp.sr = sr;
            sp.bin_hz = sr / float(kFft);
            sp.compute(sig.data(), sig.size(), fstart);
            std::printf("  evidence:");
            for (int i = 0; i < at.n_sounding; ++i) {
                bool found = false;
                float c = 0.0f, db = 0.0f;
                f0_line(sp, float(at.sounding[i]), found, c, db);
                std::printf(" %d:%s", at.sounding[i],
                            found ? "line" : "NONE");
                if (found) std::printf("%+.0fc@%.0fdB", double(c), double(db));
            }
            for (int i = 0; i < at.n_controls; ++i) {
                bool found = false;
                float c = 0.0f, db = 0.0f;
                f0_line(sp, float(at.controls[i]), found, c, db);
                std::printf(" ctl%d:%s", at.controls[i], found ? "line" : "NONE");
                if (found) std::printf("%+.0fc@%.0fdB", double(c), double(db));
            }
            std::printf("\n");
        }

        // Negative controls: candidates believed absent, measured the same
        // way (false presence), and mixed with the sounding set when it fits.
        if (at.n_controls > 0) {
            float ctrl[4] = {0};
            for (int i = 0; i < at.n_controls; ++i) ctrl[i] = float(at.controls[i]);
            const size_t n_ctrl = size_t(at.n_controls);
            print_run("ctrl/1024",
                      run_validator(sig, sr, base + size_t(0.200f * sr), 1024, ctrl, n_ctrl),
                      ctrl, n_ctrl);
            if (n_cand + n_ctrl <= kan::HarmonicChordValidator::kMaxNotes) {
                float mix[8] = {0};
                for (size_t i = 0; i < n_cand; ++i) mix[i] = cand[i];
                for (size_t i = 0; i < n_ctrl; ++i) mix[n_cand + i] = ctrl[i];
                print_run("mix/1024",
                          run_validator(sig, sr, base + size_t(0.200f * sr), 1024,
                                        mix, n_cand + n_ctrl),
                          mix, n_cand + n_ctrl);
            }
        }

        // Blind mono-vs-polyphonic features (no candidate input): top
        // spectral peaks, best single harmonic series, and the validator
        // driven by BLIND best-guess fundamentals only.
        if (fstart + kSpecWin < sig.size()) {
            Spectrum sp;
            sp.sr = sr;
            sp.bin_hz = sr / float(kFft);
            sp.compute(sig.data(), sig.size(), fstart);
            const auto peaks = top_peaks(sp, 64, -25.0f);
            double peak_energy = 0.0;
            for (const auto& p : peaks) peak_energy += double(p.mag) * double(p.mag);
            float bm[4] = {0}, bs[4] = {0};
            best_fundamentals(sp, bm, bs, 4);
            double series_energy = 0.0;
            for (int h = 1; h <= 6; ++h) {
                const float m = sp.at(midi_to_hz(bm[0]) * float(h));
                series_energy += double(m) * double(m);
            }
            const double capture = peak_energy > 0.0 ? series_energy / peak_energy : 0.0;
            float blind1[1] = {bm[0]};
            float blind2[2] = {bm[0], bm[1]};
            const CandRun r1 = run_validator(sig, sr, fstart, 8192, blind1, 1);
            const CandRun r2 = run_validator(sig, sr, fstart, 8192, blind2, 2);
            std::printf("  blind peaks=%zu capture=%.2f best=%.2f sim1=%.3f sim2=%.3f\n",
                        peaks.size(), capture, double(bm[0]), double(r1.similarity),
                        double(r2.similarity));
            if (at.n_sounding == 1 || at.n_sounding >= 3) {
                cap_v.push_back(float(capture));
                sim1_v.push_back(r1.similarity);
                peak_v.push_back(-float(peaks.size()));
                mono_l.push_back(at.n_sounding == 1);
            }
        }
    }

    // Blind mono-vs-polyphonic separation on the annotated labels.
    std::printf("\n== blind mono-vs-polyphonic separation (%zu attacks)\n", mono_l.size());
    if (!mono_l.empty()) {
        print_separation(cap_v, mono_l, "capture   ");
        print_separation(sim1_v, mono_l, "sim1      ");
        print_separation(peak_v, mono_l, "peaks(neg)");
    }
}

// ---------------------------------------------------------------------------
// --bench: validate() cost vs chord size and window length (the kMaxNotes
// design decision needs measured scaling, not claims). Deterministic
// synthetic chords at the expected pitches + fixed LCG noise.
void bench(float sr) {
    std::printf("# validate() cost, synthetic chords at expected pitches\n");
    std::printf("# n  window  mean_us  similarity\n");
    volatile float sink = 0.0f;
    for (size_t n : {size_t(1), size_t(2), size_t(3), size_t(4), size_t(6)}) {
        for (size_t len : {size_t(1024), size_t(8192)}) {
            std::vector<float> sig(len, 0.0f);
            float midi[6] = {0};
            for (size_t k = 0; k < n; ++k) midi[k] = 43.0f + 5.0f * float(k);
            for (size_t t = 0; t < len; ++t) {
                double s = 0.0;
                for (size_t k = 0; k < n; ++k) {
                    for (int h = 1; h <= 6; ++h) {
                        s += (0.3 / double(h)) * std::sin(2.0 * M_PI
                             * double(midi_to_hz(midi[k]) * float(h)) * double(t) / double(sr));
                    }
                }
                const uint32_t x = uint32_t(t) * 2654435761u;
                s += 0.01 * (double(int((x ^ (x >> 13)) % 2000)) / 1000.0 - 1.0);
                sig[t] = float(s);
            }
            kan::HarmonicChordValidator v(sr, 8192);
            v.set_expected_chord(midi, n);
            (void)v.validate(sig.data(), sig.size()); // warm up
            const int reps = 200;
            const auto t0 = std::chrono::steady_clock::now();
            float sim = 0.0f;
            for (int r = 0; r < reps; ++r) sim = v.validate(sig.data(), sig.size()).similarity;
            const auto t1 = std::chrono::steady_clock::now();
            const double us = std::chrono::duration<double, std::micro>(t1 - t0).count() / reps;
            sink += sim;
            std::printf("  %zu  %6zu  %7.1f  %.3f\n", n, len, us, double(sim));
        }
    }
    (void)sink;
}

} // namespace

int main(int argc, char** argv) {
    const char* mode = nullptr;
    const char* wav_path = nullptr;
    for (int i = 1; i < argc; ++i) {
        if (std::strcmp(argv[i], "--help") == 0) {
            std::printf("libkan_harmonic_probe — chord-validation evidence probe\n"
                        "  libkan_harmonic_probe --dump <wav>     spectral inspection\n"
                        "  libkan_harmonic_probe --measure <wav>  candidate measurements\n"
                        "  libkan_harmonic_probe --bench          validate() cost scaling\n");
            return 0;
        } else if (mode == nullptr) {
            mode = argv[i];
        } else {
            wav_path = argv[i];
        }
    }
    if (mode == nullptr) {
        std::fprintf(stderr, "[ERROR] usage: libkan_harmonic_probe --dump|--measure <wav> | --bench\n");
        return -1;
    }
    if (std::strcmp(mode, "--bench") == 0) {
        bench(48000.0f);
        return 0;
    }
    if (wav_path == nullptr) {
        std::fprintf(stderr, "[ERROR] usage: libkan_harmonic_probe --dump|--measure <wav> | --bench\n");
        return -1;
    }
    std::vector<float> sig;
    uint32_t sr = 0;
    if (!kan_test::read_wav(wav_path, sig, sr)) {
        std::fprintf(stderr, "[ERROR] cannot read %s\n", wav_path);
        return -1;
    }
    std::printf("# %s | %.1f s | %u Hz | %zu annotated attacks\n", wav_path,
                double(sig.size()) / double(sr), sr, kAttackCount);
    if (std::strcmp(mode, "--dump") == 0) {
        dump(sig, float(sr));
        return 0;
    }
    if (std::strcmp(mode, "--measure") == 0) {
        measure(sig, float(sr));
        return 0;
    }
    std::fprintf(stderr, "[ERROR] unknown mode %s\n", mode);
    return -1;
}
