#pragma once
// Score-informed chord validation (SPECIFICATION.md §2 stage 3, polyphonic
// branch): joint harmonic least-squares fit of the chart's expected chord,
// scored by explained-energy ratio (threshold ≈ 0.80) plus a per-note
// presence floor.
//
// Method (per validate() call, two robust passes):
//   1. Joint weighted least squares over harmonic atoms (cos/sin pairs at
//      the fundamental + first five harmonics of every expected note), with
//      iterative one-vs-rest frequency refinement: each note's fundamental
//      is re-searched (±search_cents, 2.5-cent steps) against the residual
//      plus its own fitted component, so just-ratio collisions between
//      chord tones (G4's 2nd harmonic vs C4's 3rd) cannot bias the search.
//      This tracks per-string detuning — real instruments are never exactly
//      on equal temperament (the global / per-string calibrator of
//      SPECIFICATION §2 feeds the same corrections; the refinement absorbs
//      what is left).
//   2. IRLS re-weighting between the passes downweights broadband outliers
//      (pick transients), which no harmonic model can explain and which
//      would otherwise swamp the metric.
// The similarity is the weighted fraction of signal energy the chord model
// explains, c^T b / x^T W x in [0, 1]; a confirmed chord additionally
// requires every expected note to carry at least `presence_floor` of the
// explained energy — the musical statement of "no wrong/missing string".
//
// Why least squares over a marginal Goertzel bank: inside the <32 ms gate
// the window (~21 ms) resolves ~48 Hz, coarser than a semitone at chord
// fundamentals (E4 vs Eb4 are 18 Hz apart), so marginal magnitude bins
// smear across neighbors (measured macro-F1 ~0.71 — see chord_test.cpp).
// The joint fit exploits phase coherence across harmonics: a wrong note
// cannot be explained at ANY frequency within the search range and shows up
// as residual energy + a collapsed presence slot.
//
// Threading: validate() runs on the consumer thread; all scratch is
// preallocated in the constructor. One validation is a ~1-2 ms burst for a
// triad (search + fits over 1024 samples): call it per chord event (or
// every few blocks while a chord sustains), not every block. Input is
// const — safe for a non-consuming kanfx tap.
#include <algorithm>
#include <cmath>
#include <cstddef>
#include <vector>
#if defined(__AVX2__)
#include <immintrin.h>
#endif

#include "chord_goertzel.hpp" // midi_to_hz via GoertzelChordValidator, ChordResult

namespace kan {

namespace gram_detail {

/// Weighted dot products for the normal equations — the hot loop of the
/// conditioned fit (O(atoms²·window)). AVX2 processes 8 floats per
/// instruction with 2 independent accumulators (deterministic order);
/// the scalar path is the reference fallback for other ISAs (NEON is the
/// same pattern; the budget is already met on x86).
inline double wdot(const float* a, const float* w, const float* b, size_t n) noexcept {
#if defined(__AVX2__)
    __m256 acc0 = _mm256_setzero_ps();
    __m256 acc1 = _mm256_setzero_ps();
    size_t i = 0;
    for (; i + 16 <= n; i += 16) {
        const __m256 a0 = _mm256_loadu_ps(a + i);
        const __m256 w0 = _mm256_loadu_ps(w + i);
        const __m256 b0 = _mm256_loadu_ps(b + i);
        const __m256 a1 = _mm256_loadu_ps(a + i + 8);
        const __m256 w1 = _mm256_loadu_ps(w + i + 8);
        const __m256 b1 = _mm256_loadu_ps(b + i + 8);
        acc0 = _mm256_add_ps(acc0, _mm256_mul_ps(_mm256_mul_ps(a0, w0), b0));
        acc1 = _mm256_add_ps(acc1, _mm256_mul_ps(_mm256_mul_ps(a1, w1), b1));
    }
    alignas(32) float lane[8];
    _mm256_store_ps(lane, acc0);
    double acc = 0.0;
    for (int k = 0; k < 8; ++k) acc += double(lane[k]);
    _mm256_store_ps(lane, acc1);
    for (int k = 0; k < 8; ++k) acc += double(lane[k]);
    for (; i < n; ++i) acc += double(a[i]) * double(w[i]) * double(b[i]);
    return acc;
#else
    double acc = 0.0;
    for (size_t i = 0; i < n; ++i) acc += double(a[i]) * double(w[i]) * double(b[i]);
    return acc;
#endif
}

/// Unweighted dot (right-hand side with the pre-weighted x).
inline double dot(const float* a, const float* b, size_t n) noexcept {
#if defined(__AVX2__)
    __m256 acc = _mm256_setzero_ps();
    size_t i = 0;
    for (; i + 8 <= n; i += 8) {
        acc = _mm256_add_ps(acc, _mm256_mul_ps(_mm256_loadu_ps(a + i),
                                               _mm256_loadu_ps(b + i)));
    }
    alignas(32) float lane[8];
    _mm256_store_ps(lane, acc);
    double s = 0.0;
    for (int k = 0; k < 8; ++k) s += double(lane[k]);
    for (; i < n; ++i) s += double(a[i]) * double(b[i]);
    return s;
#else
    double s = 0.0;
    for (size_t i = 0; i < n; ++i) s += double(a[i]) * double(b[i]);
    return s;
#endif
}

} // namespace gram_detail

/// Joint harmonic least-squares chord validator (explained-energy metric).
class HarmonicChordValidator {
public:
    static constexpr size_t kMaxNotes = 10;   ///< expected chord tones (hard cap;
                                             ///< per-instrument caps come from
                                             ///< InstrumentProfile::max_simultaneous_notes)
    static constexpr size_t kHarmonics = 6;   ///< fundamental + first five
    static constexpr size_t kSearchHarmonics = 3; ///< used by the frequency search
    static constexpr size_t kAtomsPerNote = kHarmonics * 2;         // cos+sin
    static constexpr size_t kMaxAtoms = kMaxNotes * kAtomsPerNote; // 120

    /// `max_window` bounds the analysis length; scratch is sized once here.
    explicit HarmonicChordValidator(float sample_rate = 48000.0f, size_t max_window = 1536)
        : sample_rate_(sample_rate), max_window_(max_window) {
        atoms_.assign(kMaxAtoms * max_window_, 0.0f);
        gram_.assign(kMaxAtoms * kMaxAtoms, 0.0f);
        rhs_.assign(kMaxAtoms, 0.0f);
        sol_.assign(kMaxAtoms, 0.0f);
        weights_.assign(max_window_, 1.0f);
        residual_.assign(max_window_, 0.0f);
        abs_scratch_.assign(max_window_, 0.0f);
        fit_scratch_.assign(max_window_, 0.0);
        own_scratch_.assign(max_window_, 0.0);
    }

    /// Set the chord the chart expects (MIDI note numbers, A4 = 69). Setup
    /// path (may allocate); returns false if `n` exceeds kMaxNotes.
    bool set_expected_chord(const float* midi_notes, size_t n) {
        if (n == 0 || n > kMaxNotes) return false;
        note_count_ = n;
        for (size_t i = 0; i < n; ++i) expected_midi_[i] = midi_notes[i];
        presence_floor_ = 0.45f / float(n); // fair-share based; tolerates strum
        return true;
    }

    size_t note_count() const noexcept { return note_count_; }

    /// Explained-energy threshold for a confirmed chord (spec ≈ 0.80).
    void set_threshold(float t) noexcept { threshold_ = t; }

    /// Minimum explained-energy share every expected note must carry
    /// (default 0.45/note_count, set by set_expected_chord — tolerates the
    /// shortened in-window duration of strummed notes). Rejects missing or
    /// wrong strings that the shared notes would otherwise mask.
    void set_presence_floor(float f) noexcept { presence_floor_ = f; }

    /// Per-note fundamental search range in cents (default ±30).
    void set_search_range_cents(float cents) noexcept { search_cents_ = cents; }

    /// Minimum window RMS to attempt validation.
    void set_min_rms(float r) noexcept { min_rms_ = r; }

    /// IRLS robust-pass count (default 3). Transient-heavy windows (the
    /// attack) need all passes; clean sustain windows converge in one —
    /// 3x cheaper, same refinement rounds (cents stay searched).
    void set_passes(int n) noexcept { passes_ = (n < 1) ? 1 : (n > 3 ? 3 : n); }

    /// Fit+search round count (default 3: fit, search, fit, search, fit).
    /// 2 rounds still search the frequencies (cents stay real) at 2/3 cost.
    void set_refine_rounds(int n) noexcept {
        refine_rounds_ = (n < 2) ? 2 : (n > 3 ? 3 : n);
    }

    /// Refined detuning of expected note `k` from the last validate() call,
    /// in cents (diagnostics / judgment feedback).
    float refined_cents(size_t k) const noexcept {
        return (k < kMaxNotes) ? refined_cents_[k] : 0.0f;
    }

    /// Fitted component RMS of expected note `k` from the last validate()
    /// call (absolute amplitude of the note inside the window — the
    /// per-candidate "amplitude" measurement of the description stream).
    float note_rms(size_t k) const noexcept {
        return (k < kMaxNotes) ? note_rms_[k] : 0.0f;
    }

    /// Recommended analysis window (samples): ~21 ms at 48 kHz keeps
    /// blanking (8 ms) + window inside the <32 ms chord-verification gate.
    static size_t recommended_window(float sample_rate) noexcept {
        return size_t(1024.0f * sample_rate / 48000.0f);
    }

    /// Validate a window of audio against the expected chord. The window may
    /// contain strum transients (the IRLS pass downweights them). `count`
    /// must not exceed the constructor's max_window. Allocation-free.
    ChordResult validate(const float* samples, size_t count) noexcept {
        ChordResult res;
        res.note_count = note_count_;
        if (note_count_ == 0 || count < 256 || count > max_window_) return res;

        double acc = 0.0;
        for (size_t i = 0; i < count; ++i) acc += double(samples[i]) * double(samples[i]);
        const float rms = float(std::sqrt(acc / double(count)));
        if (rms < min_rms_ || acc <= 1e-12) return res;

        // Robust initialization: the first pass fits ONLY the window's
        // second half (strum transients and late-note silences live in the
        // first half by construction of the blanking offset). Fitting the
        // polluted half first would drag every coefficient and produce a
        // uniform residual in which no outlier stands out — the classic
        // IRLS initialization failure. Then two IRLS passes re-weight from
        // the (now good) fit's residual and use the full clean window.
        for (size_t t = 0; t < count; ++t) {
            weights_[t] = (t >= count / 2) ? 1.0f : 0.0f;
        }
        for (int pass = 0; pass < passes_; ++pass) {
            if (!joint_refine_fit(samples, count)) return res;
            if (pass < passes_ - 1) update_weights(samples, count);
        }

        // Metrics from the final joint weighted fit.
        double xx_w = 0.0;
        for (size_t t = 0; t < count; ++t) {
            xx_w += double(weights_[t]) * double(samples[t]) * double(samples[t]);
        }
        if (xx_w <= 1e-12) return res;
        double cb = 0.0;
        for (size_t a = 0; a < n_atoms_; ++a) cb += double(sol_[a]) * double(rhs_[a]);
        res.similarity = float(std::clamp(cb / xx_w, 0.0, 1.0));

        // Per-note presence: weighted energy share of each note's fitted
        // component (component norm — per-atom c*b terms can go negative
        // between coherent harmonics).
        double total_energy = 0.0;
        double note_energy[kMaxNotes] = {0};
        for (size_t k = 0; k < note_count_; ++k) {
            double e = 0.0;
            const float* base = &atoms_[k * kAtomsPerNote * max_window_];
            const float* c = &sol_[k * kAtomsPerNote];
            for (size_t t = 0; t < count; ++t) {
                double s = 0.0;
                for (size_t a = 0; a < kAtomsPerNote; ++a) {
                    s += double(c[a]) * double(base[a * max_window_ + t]);
                }
                e += double(weights_[t]) * s * s;
            }
            note_energy[k] = e;
            total_energy += e;
        }
        float min_presence = 1.0f;
        for (size_t k = 0; k < note_count_ && k < kMaxNotes; ++k) {
            res.note_presence[k] = (total_energy > 1e-18) ? float(note_energy[k] / total_energy) : 0.0f;
            note_rms_[k] = float(std::sqrt(note_energy[k] / double(count)));
            min_presence = std::min(min_presence, res.note_presence[k]);
        }

        res.valid = res.similarity >= threshold_ && min_presence >= presence_floor_;
        return res;
    }

private:
    /// Joint weighted LS fit with iterative one-vs-rest frequency
    /// refinement. Round structure: fit all notes at their current
    /// frequencies -> for each note, re-search its fundamental against the
    /// residual PLUS its own fitted component (other notes subtracted, so
    /// just-ratio partial collisions — e.g. G4's 2nd harmonic landing on
    /// C4's 3rd — cannot bias the search) -> refit. Three rounds converge
    /// for detunings inside the search range. Result: sol_/rhs_/gram_.
    bool joint_refine_fit(const float* x, size_t count) noexcept {
        n_atoms_ = note_count_ * kAtomsPerNote;
        for (size_t k = 0; k < note_count_; ++k) refined_cents_[k] = 0.0f;

        for (int round = 0; round < refine_rounds_; ++round) {
            for (size_t k = 0; k < note_count_; ++k) {
                const float f = GoertzelChordValidator::midi_to_hz(
                    expected_midi_[k] + refined_cents_[k] / 100.0f);
                materialize(&atoms_[k * kAtomsPerNote * max_window_], f, count);
            }
            build_normal_equations(x, count);
            const double lambda = 1e-3 * gram_trace() / double(n_atoms_) + 1e-12;
            if (!solve_cholesky(gram_.data(), kMaxAtoms, rhs_.data(), sol_.data(),
                                n_atoms_, lambda)) {
                return false;
            }
            if (round == refine_rounds_ - 1) break;

            // One-vs-rest frequency search per note.
            for (size_t k = 0; k < note_count_; ++k) {
                // residual_[t] = x(t) - fit(t) + comp_k(t)
                const float* base = &atoms_[k * kAtomsPerNote * max_window_];
                const float* c = &sol_[k * kAtomsPerNote];
                // Atom-major, time-inner: contiguous access (the time-inner
                // form above strides 8 KB per atom and thrashes the cache).
                // Per-time accumulation order stays atom-ascending ->
                // bit-identical results, deterministic.
                for (size_t t = 0; t < count; ++t) {
                    fit_scratch_[t] = 0.0;
                    own_scratch_[t] = 0.0;
                }
                for (size_t a = 0; a < n_atoms_; ++a) {
                    const float* at = &atoms_[a * max_window_];
                    const double sa = sol_[a];
                    for (size_t t = 0; t < count; ++t) {
                        fit_scratch_[t] += sa * double(at[t]);
                    }
                }
                for (size_t a = 0; a < kAtomsPerNote; ++a) {
                    const float* at = &base[a * max_window_];
                    const double sa = double(c[a]);
                    for (size_t t = 0; t < count; ++t) {
                        own_scratch_[t] += sa * double(at[t]);
                    }
                }
                for (size_t t = 0; t < count; ++t) {
                    residual_[t] = float(double(x[t]) - fit_scratch_[t] + own_scratch_[t]);
                }
                refined_cents_[k] = search_note_cents(k, count);
            }
        }
        return true;
    }

    /// Search the fundamental of note `k` within ±search_cents_ (2.5-cent
    /// steps) around the EXPECTED pitch, maximizing weighted harmonic
    /// capture on residual_ (which must hold note k's one-vs-rest signal).
    /// Phase-agnostic magnitude capture over the first kSearchHarmonics
    /// harmonics; the joint LS fits carry the precision.
    float search_note_cents(size_t k, size_t count) const noexcept {
        float best_cents = 0.0f;
        double best_capture = -1.0;
        const int steps = int(std::lround(search_cents_ / 2.5f));
        for (int s = -steps; s <= steps; ++s) {
            const float cents = 2.5f * float(s);
            const float f = GoertzelChordValidator::midi_to_hz(expected_midi_[k] + cents / 100.0f);
            double capture = 0.0;
            for (size_t h = 1; h <= kSearchHarmonics; ++h) {
                const double w = 2.0 * M_PI * double(f * float(h)) / double(sample_rate_);
                const double cr = std::cos(w), ci = -std::sin(w);
                double re = 1.0, im = 0.0, pc = 0.0, ps = 0.0;
                for (size_t t = 0; t < count; ++t) {
                    const double xr = double(weights_[t]) * double(residual_[t]);
                    pc += xr * re;
                    ps += xr * im;
                    const double nre = re * cr - im * ci;
                    im = re * ci + im * cr;
                    re = nre;
                }
                capture += pc * pc + ps * ps;
            }
            if (capture > best_capture) {
                best_capture = capture;
                best_cents = cents;
            }
        }
        return best_cents;
    }

    /// IRLS weights from the joint fit's residual: w(t) = min(1, (3σ/|r|)²)
    /// with σ = 1.4826 · median(|r|) over the window's second half (strum
    /// transients live in the first half; the median is robust even while
    /// the fit itself is still transient-polluted).
    void update_weights(const float* x, size_t count) noexcept {
        const size_t half = count / 2;
        // Residual over the full window (atom-major, time-inner; see
        // joint_refine_fit for the cache rationale and order preservation).
        for (size_t t = 0; t < count; ++t) fit_scratch_[t] = 0.0;
        for (size_t a = 0; a < n_atoms_; ++a) {
            const float* at = &atoms_[a * max_window_];
            const double sa = sol_[a];
            for (size_t t = 0; t < count; ++t) fit_scratch_[t] += sa * double(at[t]);
        }
        for (size_t t = 0; t < count; ++t) {
            residual_[t] = float(std::fabs(double(x[t]) - fit_scratch_[t]));
        }
        // Median of the second half via nth_element on scratch.
        const size_t m = count - half;
        std::copy(residual_.begin() + half, residual_.begin() + count, abs_scratch_.begin());
        auto mid = abs_scratch_.begin() + m / 2;
        std::nth_element(abs_scratch_.begin(), mid, abs_scratch_.begin() + m);
        const double sigma = std::max(1.4826 * double(*mid), 1e-3);
        const double cut = 3.0 * sigma;
        for (size_t t = 0; t < count; ++t) {
            const double r = residual_[t];
            weights_[t] = (r <= cut) ? 1.0f : float((cut * cut) / (r * r));
        }
    }

    /// Materialize one note's atom block (kAtomsPerNote rows of `count`
    /// samples): cos/sin pairs at harmonics 1..kHarmonics of f.
    void materialize(float* base, float f, size_t count) const noexcept {
        for (size_t h = 1; h <= kHarmonics; ++h) {
            const double w = 2.0 * M_PI * double(f * float(h)) / double(sample_rate_);
            const double cr = std::cos(w), ci = -std::sin(w);
            double re = 1.0, im = 0.0;
            float* cos_row = base + (h - 1) * 2 * max_window_;
            float* sin_row = cos_row + max_window_;
            for (size_t t = 0; t < count; ++t) {
                cos_row[t] = float(re);
                sin_row[t] = float(im);
                const double nre = re * cr - im * ci;
                im = re * ci + im * cr;
                re = nre;
            }
        }
    }

    /// G = A^T W A, b = A^T W x over the joint basis (atoms_, n_atoms_).
    void build_normal_equations(const float* x, size_t count) noexcept {
        // Hot loop of the conditioned fit — vectorized (gram_detail::wdot).
        for (size_t i = 0; i < n_atoms_; ++i) {
            const float* ai = &atoms_[i * max_window_];
            rhs_[i] = float(gram_detail::wdot(ai, weights_.data(), x, count));
            for (size_t j = i; j < n_atoms_; ++j) {
                const float* aj = &atoms_[j * max_window_];
                const double g = gram_detail::wdot(ai, weights_.data(), aj, count);
                gram_[i * kMaxAtoms + j] = float(g);
                gram_[j * kMaxAtoms + i] = float(g);
            }
        }
    }

    double gram_trace() const noexcept {
        double trace = 0.0;
        for (size_t i = 0; i < n_atoms_; ++i) trace += gram_[i * kMaxAtoms + i];
        return trace;
    }

    /// Generic Cholesky solve of (G + lambda*I) c = b; L stored in the LOWER
    /// triangle of G. False if not positive definite.
    static bool solve_cholesky(float* G, size_t stride, const float* b, float* c,
                               size_t n, double lambda) noexcept {
        for (size_t i = 0; i < n; ++i) G[i * stride + i] += float(lambda);
        for (size_t i = 0; i < n; ++i) {
            for (size_t j = 0; j <= i; ++j) {
                double s = G[i * stride + j];
                for (size_t k = 0; k < j; ++k) {
                    s -= double(G[i * stride + k]) * double(G[j * stride + k]);
                }
                if (i == j) {
                    if (s <= 1e-18) return false;
                    G[i * stride + i] = float(std::sqrt(s));
                } else {
                    G[i * stride + j] = float(s / double(G[j * stride + j]));
                }
            }
        }
        for (size_t i = 0; i < n; ++i) { // forward: L y = b
            double s = double(b[i]);
            for (size_t k = 0; k < i; ++k) s -= double(G[i * stride + k]) * double(c[k]);
            c[i] = float(s / double(G[i * stride + i]));
        }
        for (size_t i = n; i-- > 0;) { // back: L^T c = y
            double s = double(c[i]);
            for (size_t k = i + 1; k < n; ++k) s -= double(G[k * stride + i]) * double(c[k]);
            c[i] = float(s / double(G[i * stride + i]));
        }
        return true;
    }

    float sample_rate_;
    size_t max_window_;
    float threshold_ = 0.80f;
    float presence_floor_ = 0.2f;
    float min_rms_ = 0.008f;
    float search_cents_ = 30.0f;
    int passes_ = 3;
    int refine_rounds_ = 3;

    size_t note_count_ = 0;
    size_t n_atoms_ = 0;

    float expected_midi_[kMaxNotes] = {0};
    float refined_cents_[kMaxNotes] = {0};
    float note_rms_[kMaxNotes] = {0};

    std::vector<float> atoms_;       ///< kMaxAtoms * max_window_ (joint basis)
    std::vector<float> gram_;        ///< kMaxAtoms^2
    std::vector<float> rhs_;
    std::vector<float> sol_;
    std::vector<float> weights_;     ///< max_window_
    std::vector<float> residual_;    ///< max_window_
    std::vector<float> abs_scratch_; ///< max_window_ (median selection)
    std::vector<double> fit_scratch_; ///< max_window_ (atom-major residual)
    std::vector<double> own_scratch_; ///< max_window_ (atom-major own part)
};

} // namespace kan
