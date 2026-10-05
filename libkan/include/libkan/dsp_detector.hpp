#pragma once
#include <cmath>
#include <vector>
#include <string>
#include <algorithm>

#include "instrument_profile.hpp"

namespace kan {

/// Result of a pitch detection over an analysis window.
struct PitchResult {
    bool valid = false;
    float frequency_hz = 0.0f;
    std::string note_name = "-";
    float cents_offset = 0.0f; ///< Deviation in cents relative to equal temperament (A4 = 440 Hz)
};

/// Converts a frequency to the nearest equal-tempered note name and the
/// deviation in cents relative to that note (A4 = 440 Hz reference).
/// Shared by all libkan pitch detectors.
inline void quantize_to_note(float freq, std::string& out_name, float& out_cents) noexcept {
    static const char* NAMES[12] = {"C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"};
    float midi_exact = 69.0f + 12.0f * std::log2(freq / 440.0f);
    int midi_rounded = static_cast<int>(std::round(midi_exact));
    out_cents = (midi_exact - static_cast<float>(midi_rounded)) * 100.0f;

    int octave = (midi_rounded / 12) - 1;
    int note_idx = ((midi_rounded % 12) + 12) % 12;
    out_name = std::string(NAMES[note_idx]) + std::to_string(octave);
}

/// Hybrid detection pipeline: a fast onset path (~2.6 ms per block) plus a
/// harmonic pitch path (~20 ms) reporting note name and cents offset.
class HybridDetector {
public:
    explicit HybridDetector(float sample_rate = 48000.0f)
        : sample_rate_(sample_rate) {}

    /// Set the absolute energy floor (mean squared amplitude) an onset must
    /// exceed. Feed this from a NoiseGate calibration so the onset detector
    /// shares the learned room-silence level instead of the built-in default.
    void set_noise_floor(float energy) noexcept { noise_floor_ = energy; }

    /// Current onset energy floor (mean squared amplitude).
    float noise_floor() const noexcept { return noise_floor_; }

    /// Select the instrument profile driving stage-2 transient blanking and
    /// the pitch search range. Call from a setup thread.
    void set_profile(const InstrumentProfile& profile) noexcept { profile_ = profile; }

    /// Active instrument profile.
    const InstrumentProfile& profile() const noexcept { return profile_; }

    /// STAGE 2 — transient blanking: true while pitch evaluation must stay
    /// frozen because `ms_since_onset` is inside the profile's blanking
    /// window (attack transients are inharmonic noise that fools pitch
    /// algorithms). RT-safe, side-effect free.
    bool in_transient_blanking(float ms_since_onset) const noexcept {
        return ms_since_onset < profile_.blanking_ms;
    }

    /// STAGE 3 — score-informed mode: bound the YIN search to
    /// `window_semitones` around the note the chart expects (spec §2:
    /// ±2 semitones for monophonic hits). This removes octave/subharmonic
    /// errors and shortens confirmation. Pass `expected_hz <= 0` to return
    /// to blind detection bounded only by the instrument profile range.
    void set_expected_note(float expected_hz, float window_semitones = 2.0f) noexcept {
        if (expected_hz <= 0.0f) {
            expected_hz_ = 0.0f;
            return;
        }
        expected_hz_ = expected_hz;
        const float factor = std::pow(2.0f, window_semitones / 12.0f);
        bound_min_hz_ = expected_hz / factor;
        bound_max_hz_ = expected_hz * factor;
    }

    /// True while score-informed bounding is active.
    bool has_expected_note() const noexcept { return expected_hz_ > 0.0f; }

    /// Convenience combining stage 2 + stage 3: returns an invalid result
    /// while `ms_since_onset` is inside the profile's transient-blanking
    /// window, otherwise runs detect_pitch(window).
    PitchResult detect_pitch_blanked(const std::vector<float>& window,
                                     float ms_since_onset) const noexcept {
        if (in_transient_blanking(ms_since_onset)) return {};
        return detect_pitch(window);
    }

    /// FAST PATH (~2.6 ms): detects the physical attack before knowing the note.
    /// Measures the abrupt jump of the energy derivative against the noise floor.
    /// Must run on the consumer thread (never inside the audio callback).
    bool process_onset_block(const float* samples, size_t count) noexcept {
        float energy = 0.0f;
        float hfc_derivative = 0.0f; // High-Frequency Slope

        for (size_t i = 1; i < count; ++i) {
            energy += samples[i] * samples[i];
            float diff = samples[i] - samples[i - 1];
            hfc_derivative += diff * diff;
        }
        energy /= static_cast<float>(count);
        hfc_derivative /= static_cast<float>(count);

        // Abrupt-jump detection against the exponential moving average
        bool is_onset = false;
        if (cooldown_blocks_ == 0) {
            if (energy > noise_floor_ && hfc_derivative > prev_hfc_ * 3.2f) {
                is_onset = true;
                cooldown_blocks_ = 25; // ~65 ms debounce between hits
            }
        } else {
            cooldown_blocks_--;
        }

        // Update energy memory with fast decay
        prev_hfc_ = 0.75f * prev_hfc_ + 0.25f * hfc_derivative + 1e-7f;
        return is_onset;
    }

    /// HARMONIC PATH (~20 ms): lightweight YIN algorithm with parabolic
    /// interpolation for sub-sample accuracy (cents precision).
    /// `window` should hold steady-state audio; attack transients degrade accuracy.
    PitchResult detect_pitch(const std::vector<float>& window) const noexcept {
        PitchResult res;
        const size_t W = window.size() / 2;

        // Minimum energy check to skip silence
        float rms = 0.0f;
        for (float s : window) rms += s * s;
        rms = std::sqrt(rms / window.size());
        if (rms < 0.008f) return res;

        // Search range bounded by the instrument profile (defaults 60-1200 Hz);
        // when the score provides an expected note (stage 3), the search is
        // further bounded to ±window_semitones around it, eliminating octave
        // and subharmonic errors.
        const float f_min = has_expected_note() ? bound_min_hz_ : profile_.min_freq_hz;
        const float f_max = has_expected_note() ? bound_max_hz_ : profile_.max_freq_hz;
        const size_t tau_min = std::max<size_t>(1, static_cast<size_t>(sample_rate_ / f_max));
        const size_t tau_max = std::min(W, static_cast<size_t>(sample_rate_ / f_min));

        std::vector<float> cmndf(tau_max, 1.0f);
        float running_sum = 0.0f;

        for (size_t tau = 1; tau < tau_max; ++tau) {
            float diff_sum = 0.0f;
            for (size_t i = 0; i < W; ++i) {
                float d = window[i] - window[i + tau];
                diff_sum += d * d;
            }
            running_sum += diff_sum;
            cmndf[tau] = (running_sum > 0.0f) ? (diff_sum * tau / running_sum) : 1.0f;
        }

        // Strict YIN periodicity threshold
        constexpr float threshold = 0.15f;
        size_t best_tau = 0;
        for (size_t tau = tau_min; tau < tau_max - 1; ++tau) {
            if (cmndf[tau] < threshold && cmndf[tau] < cmndf[tau + 1]) {
                best_tau = tau;
                break;
            }
        }

        if (best_tau == 0) return res;

        // Parabolic interpolation for sub-sample precision (cents accuracy)
        float s0 = cmndf[best_tau - 1];
        float s1 = cmndf[best_tau];
        float s2 = cmndf[best_tau + 1];
        float refined_tau = static_cast<float>(best_tau) + (s2 - s0) / (2.0f * (2.0f * s1 - s2 - s0));

        res.valid = true;
        res.frequency_hz = sample_rate_ / refined_tau;
        quantize_to_note(res.frequency_hz, res.note_name, res.cents_offset);
        return res;
    }

private:
    float sample_rate_;
    float noise_floor_ = 0.0005f;
    float prev_hfc_ = 0.0001f;
    int cooldown_blocks_ = 0;
    InstrumentProfile profile_{};
    float expected_hz_ = 0.0f;
    float bound_min_hz_ = 60.0f;
    float bound_max_hz_ = 1200.0f;
};

} // namespace kan
