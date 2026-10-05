#pragma once
#include <cmath>
#include <vector>
#include <string>
#include <algorithm>

namespace kan {

/// Result of a pitch detection over an analysis window.
struct PitchResult {
    bool valid = false;
    float frequency_hz = 0.0f;
    std::string note_name = "-";
    float cents_offset = 0.0f; ///< Deviation in cents relative to equal temperament (A4 = 440 Hz)
};

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

        // Search range: 60 Hz (B1) to 1200 Hz (D6)
        const size_t tau_min = static_cast<size_t>(sample_rate_ / 1200.0f);
        const size_t tau_max = std::min(W, static_cast<size_t>(sample_rate_ / 60.0f));

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

    /// Converts a frequency to the nearest equal-tempered note name and the
    /// deviation in cents relative to that note (A4 = 440 Hz reference).
    static void quantize_to_note(float freq, std::string& out_name, float& out_cents) noexcept {
        static const char* NAMES[12] = {"C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"};
        float midi_exact = 69.0f + 12.0f * std::log2(freq / 440.0f);
        int midi_rounded = static_cast<int>(std::round(midi_exact));
        out_cents = (midi_exact - static_cast<float>(midi_rounded)) * 100.0f;

        int octave = (midi_rounded / 12) - 1;
        int note_idx = ((midi_rounded % 12) + 12) % 12;
        out_name = std::string(NAMES[note_idx]) + std::to_string(octave);
    }
};

} // namespace kan
