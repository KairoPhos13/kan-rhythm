#pragma once
// Score-informed bounded pitch matcher (SPECIFICATION.md §2 stage 3).
//
// When the chart says which note is expected, blind pitch detection is
// overkill: the search collapses to a narrow lag band around the expected
// fundamental (±2 semitones per spec). This matcher computes a normalized
// autocorrelation (NSDF-style, 2*sum(x_i * x_i+tau) / (sum x_i^2 +
// sum x_i+tau^2)) ONLY over that lag band, so it confirms a note from a
// short window (the longest candidate lag plus ~1/4 period of overlap)
// instead of the 2048-sample windows blind YIN needs. Sub-sample parabolic
// interpolation over the peak lag yields cents-grade frequency.
//
// Why autocorrelation and not a DFT/Goertzel grid: at these window lengths
// (~1 period for low notes) the negative-frequency image of a real signal
// sits only ~2 bins from the analysis band and its Dirichlet tail tilts the
// whole ±200-cent grid monotonically, no interior peak. The autocorrelation
// of a real signal is image-free by construction.
//
// Confirmation latency floor = blanking + tau_max + min_pairs (+ block
// quantization): C4 ~14.7 ms, E2 ~22.7 ms with the plucked profile at
// 64-sample blocks, inside the SPECIFICATION §2 harmonic-validation budget
// (<35 ms) for every note. ROADMAP per-band gates and the physical-floor
// analysis: docs/dsp/latency-measurements.md.
//
// Role in the pipeline: FAST confirmation for the rhythmic/harmonic gates.
// Sustained intonation tracking keeps using the full-accuracy detectors
// (YIN window / Cycfi Q BACF) on the same audio.
//
// Threading: match() runs on the consumer thread and does not allocate
// (scratch is sized by set_expected_note() on the setup path).
#include <algorithm>
#include <cmath>
#include <cstddef>
#include <vector>

#include "dsp_detector.hpp"

namespace kan {

/// Bounded-lag NSDF pitch matcher around a score-expected note.
///
/// Feed it CLEAN (post-blanking) audio only: a window that starts at the
/// end of the transient-blanking period and grows until confirmation. Pick
/// transients are deterministic broadband bursts; letting them into the
/// correlation pairs injects structured interference that can beat the true
/// peak on short windows.
class BoundedPitchMatcher {
public:
    explicit BoundedPitchMatcher(float sample_rate = 48000.0f)
        : sample_rate_(sample_rate) {}

    /// Set the expected note; the lag search is bounded to ±window_semitones
    /// (spec default ±2). Configures internal scratch; call from the setup
    /// path, not per-block.
    void set_expected_note(float expected_hz, float window_semitones = 2.0f) {
        expected_hz_ = expected_hz;
        window_semitones_ = window_semitones;
        const float factor = std::pow(2.0f, window_semitones / 12.0f);
        const float f_min = expected_hz / factor;
        const float f_max = expected_hz * factor;
        tau_min_ = std::max<size_t>(2, static_cast<size_t>(sample_rate_ / f_max));
        tau_max_ = static_cast<size_t>(std::ceil(sample_rate_ / f_min)) + 1;
        // Correlation overlap beyond the longest candidate lag: half a
        // period (clamped); short overlaps left the NSDF peak broad enough
        // for 30-50 cent ambiguity at low notes (range_test). Half a period
        // costs ~2 ms of confirmation latency below ~150 Hz, where no
        // latency gate exists anyway (docs/dsp/range-coverage.md).
        const float period = sample_rate_ / expected_hz;
        min_pairs_ = std::clamp(size_t(std::round(period / 2.0f)), size_t(64), size_t(512));
        nsdf_.assign(tau_max_ + 2, 0.0f);
    }

    bool has_expected_note() const noexcept { return expected_hz_ > 0.0f; }

    /// Minimum correlation overlap beyond the longest candidate lag
    /// (default: half a period of the expected note, clamped to [64, 512]).
    /// Measured: sub-cent accuracy on C4..A5, ~15 cents worst-case on E2 at
    /// 64 pairs; half a period holds <25 cents across the full piano range
    /// (range_test).
    void set_min_pairs(size_t pairs) noexcept { min_pairs_ = pairs > 0 ? pairs : 1; }

    /// Recommended clean-window length for the current expected note:
    /// longest candidate lag plus the minimum overlap. Longer windows are
    /// accepted and improve accuracy.
    size_t window_samples() const noexcept { return tau_max_ + min_pairs_; }

    /// NSDF peak threshold for confirmation (default 0.5). Periodic signals
    /// peak near 1.0; uncorrelated noise stays well below.
    void set_threshold(float t) noexcept { threshold_ = t; }

    /// Match a window of CLEAN audio (post-blanking). `count` must be at
    /// least window_samples(). Returns a valid PitchResult on confirmation.
    /// The analysis loop performs no allocation (nsdf_ scratch is
    /// preallocated by set_expected_note); note that PitchResult carries a
    /// std::string note_name, so returning a result performs one small
    /// heap allocation. Run on the consumer thread, not the audio thread.
    PitchResult match(const float* samples, size_t count) const noexcept {
        PitchResult out;
        if (!has_expected_note() || count < window_samples() || nsdf_.empty()) {
            return out;
        }

        const size_t pairs = count - tau_max_; // correlation terms per lag
        size_t best_tau = 0;
        float best_val = -2.0f;

        for (size_t tau = tau_min_; tau <= tau_max_; ++tau) {
            double m = 0.0, a = 0.0, b = 0.0;
            for (size_t i = 0; i < pairs; ++i) {
                const double x = samples[i];
                const double y = samples[i + tau];
                m += x * y;
                a += x * x;
                b += y * y;
            }
            const double denom = a + b;
            const float val = (denom > 1e-12) ? float(2.0 * m / denom) : 0.0f;
            nsdf_[tau] = val;
            if (val > best_val) {
                best_val = val;
                best_tau = tau;
            }
        }

        if (best_val < threshold_) return out;

        // Parabolic interpolation for sub-sample lag (cents precision).
        float refined_tau = float(best_tau);
        if (best_tau > tau_min_ && best_tau < tau_max_) {
            const float s0 = nsdf_[best_tau - 1];
            const float s1 = nsdf_[best_tau];
            const float s2 = nsdf_[best_tau + 1];
            const float denom = 2.0f * (2.0f * s1 - s2 - s0);
            if (std::fabs(denom) > 1e-9f) {
                const float delta = std::clamp((s2 - s0) / denom, -0.5f, 0.5f);
                refined_tau = float(best_tau) + delta;
            }
        }

        const float f = sample_rate_ / refined_tau;
        const float cents_off = 1200.0f * std::log2(f / expected_hz_);
        // Reject if the peak sits at the edge of the allowed band: the true
        // note is likely outside ±window_semitones (wrong string / wrong note).
        if (std::fabs(cents_off) > window_semitones_ * 100.0f - 1.0f) return out;

        out.valid = true;
        out.frequency_hz = f;
        quantize_to_note(f, out.note_name, out.cents_offset);
        return out;
    }

private:
    float sample_rate_;
    float expected_hz_ = 0.0f;
    float window_semitones_ = 2.0f;
    float threshold_ = 0.5f;
    size_t tau_min_ = 2;
    size_t tau_max_ = 2;
    size_t min_pairs_ = 64;
    mutable std::vector<float> nsdf_;
};

} // namespace kan
