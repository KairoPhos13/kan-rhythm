#pragma once
// Dynamic tuning compensation (SPECIFICATION.md §2 "Dynamic consonance
// module"): the 2-second reference-note calibrator (Δc = 1200·log2(f_measured
// / f_standard)), the global + per-string compensation matrix (Δc1..Δc6),
// and the relative-tuning mode (fretless/bowed/voice) that judges intervals
// against a moving tonal center of gravity (EMA).
//
// Role in the pipeline: calibration runs BEFORE play (setup/calibration
// thread or the consumer thread between notes); the resulting
// TuningCompensation is immutable while playing and read by the detection
// layer to shift detector target frequencies (and by kanfx to shift the
// backing track by the same amount; the instrument and the song end up in
// tune with each other while scoring stays strict).
//
// Real-time safety: observe()/target_*()/correction_cents() are trivial
// arithmetic (no allocation, no locks) and may run anywhere. DetuneCalibrator
// collects readings into fixed storage (no heap); finish() works on stack
// copies only.
#include <algorithm>
#include <cmath>
#include <cstddef>
#include <limits>

namespace kan {

/// MIDI note number to frequency (A4 = 69 = 440 Hz). Same convention as the
/// detector headers.
inline float calibration_midi_to_hz(float midi) noexcept {
    return 440.0f * std::pow(2.0f, (midi - 69.0f) / 12.0f);
}

/// Result of a 2-second reference-note calibration.
struct DetuneMeasurement {
    bool valid = false;
    float delta_cents = 0.0f;    ///< Δc = 1200·log2(f_measured / f_standard)
    float measured_hz = 0.0f;    ///< robust median of the collected readings
    float standard_hz = 0.0f;    ///< reference (expected or nearest ET note)
    float standard_midi = 0.0f;  ///< reference note number (fractional)
    float spread_cents = 0.0f;   ///< robust scatter of readings (1.4826·MAD)
    size_t readings = 0;
};

/// Collects steady pitch readings from the harmonic path over a ~2 s
/// reference note ("play any long comfortable note", SPECIFICATION §2) and
/// computes the instrument's global detuning Δc in cents.
///
/// Usage: feed one reading per confirmed block via add_frequency(); when
/// ready() is true call finish() with the expected note (guided flow) or
/// without it (nearest equal-tempered note is inferred).
class DetuneCalibrator {
public:
    static constexpr size_t kMaxReadings = 512;

    explicit DetuneCalibrator(float window_seconds = 2.0f,
                              size_t min_readings = 20) noexcept
        : window_seconds_(window_seconds), min_readings_(min_readings) {}

    void reset() noexcept {
        count_ = 0;
        start_t_ = 0.0f;
        last_t_ = 0.0f;
        started_ = false;
    }

    /// Add one pitch reading (Hz) at `t_seconds` (any monotonic clock).
    /// Ignored once the window is complete. Allocation-free.
    void add_frequency(float hz, float t_seconds) noexcept {
        if (ready() || !(hz > 0.0f)) return;
        if (!started_) {
            started_ = true;
            start_t_ = t_seconds;
        }
        last_t_ = t_seconds;
        if (count_ < kMaxReadings) {
            readings_[count_++] = hz;
        }
    }

    /// True once ~window_seconds of steady readings are collected.
    bool ready() const noexcept {
        return count_ >= min_readings_ && (last_t_ - start_t_) >= window_seconds_;
    }

    /// Progress of the reference-note flow in [0, 1] (time-based).
    float progress() const noexcept {
        if (!started_ || window_seconds_ <= 0.0f) return 0.0f;
        return std::min(1.0f, (last_t_ - start_t_) / window_seconds_);
    }

    size_t readings() const noexcept { return count_; }

    /// Finalize: compute Δc against `expected_midi` (guided flow), or
    /// against the nearest equal-tempered note when left as NaN.
    DetuneMeasurement finish(float expected_midi = std::numeric_limits<float>::quiet_NaN()) const noexcept {
        DetuneMeasurement m;
        m.readings = count_;
        if (count_ == 0) return m;

        // Median of the readings (robust against attack stragglers).
        float sorted[kMaxReadings];
        for (size_t i = 0; i < count_; ++i) sorted[i] = readings_[i];
        std::nth_element(sorted, sorted + count_ / 2, sorted + count_);
        const float median = sorted[count_ / 2];
        if (!(median > 0.0f)) return m;

        // Robust scatter: 1.4826 * median absolute deviation.
        float dev[kMaxReadings];
        for (size_t i = 0; i < count_; ++i) dev[i] = std::fabs(readings_[i] - median);
        std::nth_element(dev, dev + count_ / 2, dev + count_);
        m.spread_cents = 1.4826f * dev[count_ / 2] * 1200.0f / (median * 0.6931472f + 1e-9f);
        // (d_f cents ≈ 1200/ln2 · df/f)

        m.measured_hz = median;
        const float measured_midi = 69.0f + 12.0f * std::log2(median / 440.0f);
        if (std::isfinite(expected_midi)) {
            m.standard_midi = expected_midi;
        } else {
            m.standard_midi = std::round(measured_midi);
        }
        m.standard_hz = calibration_midi_to_hz(m.standard_midi);
        m.delta_cents = 1200.0f * std::log2(median / m.standard_hz);
        m.valid = true;
        return m;
    }

private:
    float window_seconds_;
    size_t min_readings_;
    float readings_[kMaxReadings] = {0};
    size_t count_ = 0;
    float start_t_ = 0.0f;
    float last_t_ = 0.0f;
    bool started_ = false;
};

/// Global + per-string compensation matrix (Δc1..Δc6, SPECIFICATION §2).
/// Plain data; computed once during calibration, immutable while playing.
///
/// Semantics: the correction is what the DETECTOR TARGETS (and the backing
/// track) must be shifted by so that an instrument playing "in tune with
/// itself" is judged in tune. A +12-cent-sharp instrument gets target
/// frequencies 12 cents up; the player's residual cents readout then shows
/// deviation from their own tuning, not from A440.
struct TuningCompensation {
    static constexpr size_t kMaxStrings = 6;

    float global_cents = 0.0f;
    float string_cents[kMaxStrings] = {0, 0, 0, 0, 0, 0}; ///< Δc1..Δc6
    size_t string_count = 0;                              ///< 0 = global only

    /// Set the global detuning from a reference-note calibration.
    void set_global(float cents) noexcept { global_cents = cents; }

    /// Per-string matrix from the 3 s guided open-string strum: one Δc per
    /// string, low string first (Δc1..Δc6). Cleared by passing n = 0.
    void set_strings(const float* delta_cents, size_t n) noexcept {
        string_count = std::min(n, kMaxStrings);
        for (size_t i = 0; i < string_count; ++i) string_cents[i] = delta_cents[i];
    }

    bool has_strings() const noexcept { return string_count > 0; }

    /// Effective correction for a note played on `string_index` (0-based,
    /// low string first). -1 or no matrix: global only. Strings beyond the
    /// matrix fall back to global.
    float correction_cents(int string_index = -1) const noexcept {
        if (string_index >= 0 && size_t(string_index) < string_count) {
            return global_cents + string_cents[size_t(string_index)];
        }
        return global_cents;
    }

    /// Detector target for a standard frequency: f_standard shifted by the
    /// effective correction. Feed this to set_expected_note() etc.
    float target_frequency(float standard_hz, int string_index = -1) const noexcept {
        return standard_hz * std::pow(2.0f, correction_cents(string_index) / 1200.0f);
    }

    /// Detector target as a (fractional) MIDI note number.
    float target_midi(float standard_midi, int string_index = -1) const noexcept {
        return standard_midi + correction_cents(string_index) / 100.0f;
    }

    /// Residual cents of a measured frequency vs the compensated target,
    /// what the judge (and the player) should see.
    float residual_cents(float measured_hz, float standard_hz,
                         int string_index = -1) const noexcept {
        return 1200.0f * std::log2(measured_hz / target_frequency(standard_hz, string_index));
    }
};

/// Relative-tuning mode for fretless/bowed/voice instruments
/// (SPECIFICATION §2): intervals (ΔNote_n − ΔNote_{n−1}) are evaluated
/// against a moving tonal center of gravity (exponential moving average);
/// only harmony-breaking deviations are punished; the instrument's fixed
/// (or slowly drifting) detune is absorbed by the center instead of being
/// penalized note by note.
class RelativeTuningTracker {
public:
    struct Config {
        float ema_alpha = 0.15f;                ///< center adaptation per accepted note
        float interval_tolerance_cents = 35.0f; ///< |deviation from center| allowed
        float center_capture_cents = 50.0f;     ///< |deviation| that still updates the center
        Config() {}
    };

    struct IntervalResult {
        bool interval_ok = false;        ///< interval vs center within tolerance
        float error_cents = 0.0f;        ///< deviation of this note from the center
        float delta_interval_cents = 0.0f; ///< (ΔNote_n − ΔNote_{n−1}): pure interval step
        float center_cents = 0.0f;       ///< tonal center after this note (relative reference)
    };

    explicit RelativeTuningTracker(const Config& cfg = Config()) noexcept : cfg_(cfg) {}

    void reset() noexcept {
        center_cents_ = 0.0f;
        have_prev_ = false;
        prev_dev_cents_ = 0.0f;
    }

    /// Observe one confirmed note: `expected_midi` is what the chart asks,
    /// `measured_midi` is the detector's fractional reading. Returns the
    /// interval evaluation and updates the center when the note is in tune
    /// with it. Allocation-free.
    IntervalResult observe(float expected_midi, float measured_midi) noexcept {
        IntervalResult r;
        const float dev_cents = (measured_midi - expected_midi) * 100.0f;

        r.error_cents = dev_cents - center_cents_;
        if (have_prev_) {
            r.delta_interval_cents = dev_cents - prev_dev_cents_;
        } else {
            r.delta_interval_cents = 0.0f;
        }
        r.interval_ok = std::fabs(r.error_cents) <= cfg_.interval_tolerance_cents;

        // Update the tonal center only from notes that are in tune with it,
        // so one wrong note cannot drag the reference and excuse itself.
        if (std::fabs(r.error_cents) <= cfg_.center_capture_cents) {
            center_cents_ += cfg_.ema_alpha * (dev_cents - center_cents_);
        }
        prev_dev_cents_ = dev_cents;
        have_prev_ = true;

        r.center_cents = center_cents_;
        return r;
    }

    /// Current tonal center of gravity (deviation from the charted pitches,
    /// in cents).
    float tonal_center_cents() const noexcept { return center_cents_; }

    const Config& config() const noexcept { return cfg_; }

private:
    Config cfg_;
    float center_cents_ = 0.0f;
    float prev_dev_cents_ = 0.0f;
    bool have_prev_ = false;
};

} // namespace kan
