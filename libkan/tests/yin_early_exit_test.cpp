// HybridDetector::detect_pitch_into stops building the YIN cmndf at the
// first qualifying lag (issue 40). This checks, on known signals, that the
// estimate is exactly the one the full pass gave: the full pass is kept
// here as the reference, and every result must match bit for bit.
#include "libkan/dsp_detector.hpp"
#include "libkan/instrument_profile.hpp"

#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <random>
#include <vector>

namespace {

// The detector before issue 40: the whole cmndf, then the search.
kan::PitchEstimate full_pass(const float* window, size_t count, float sample_rate,
                             float f_min, float f_max, float threshold, std::vector<float>& cmndf) {
    kan::PitchEstimate est;
    const size_t W = count / 2;
    float rms = 0.0f;
    for (size_t i = 0; i < count; ++i) rms += window[i] * window[i];
    rms = std::sqrt(rms / static_cast<float>(count));
    if (rms < 0.008f) return est;
    const size_t tau_min = std::max<size_t>(1, static_cast<size_t>(sample_rate / f_max));
    const size_t tau_max = std::min(W, static_cast<size_t>(sample_rate / f_min) + 2);
    if (tau_max > cmndf.size() || tau_max < 2) return est;
    cmndf[0] = 1.0f;
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
    size_t best_tau = 0;
    for (size_t tau = tau_min; tau < tau_max - 1; ++tau) {
        if (cmndf[tau] < threshold && cmndf[tau] < cmndf[tau + 1]) {
            best_tau = tau;
            break;
        }
    }
    if (best_tau == 0) return est;
    float s0 = cmndf[best_tau - 1], s1 = cmndf[best_tau], s2 = cmndf[best_tau + 1];
    float refined_tau = static_cast<float>(best_tau) + (s2 - s0) / (2.0f * (2.0f * s1 - s2 - s0));
    est.valid = true;
    est.frequency_hz = sample_rate / refined_tau;
    kan::quantize_midi(est.frequency_hz, est.midi, est.cents_offset);
    return est;
}

bool same(const kan::PitchEstimate& a, const kan::PitchEstimate& b) {
    return a.valid == b.valid && std::memcmp(&a.frequency_hz, &b.frequency_hz, sizeof(float)) == 0
        && std::memcmp(&a.midi, &b.midi, sizeof(float)) == 0
        && std::memcmp(&a.cents_offset, &b.cents_offset, sizeof(float)) == 0;
}

} // namespace

int main() {
    const float SR = 48000.0f;
    const size_t N = 2048;
    std::vector<float> window(N), scratch(N), reference(N);
    std::mt19937 rng(40);
    std::normal_distribution<float> noise(0.0f, 1.0f);

    kan::InstrumentProfile wide = kan::InstrumentProfile::wind_bowed();
    wide.min_freq_hz = 27.0f; // the game's live profile (kan_forge_api.cpp profile_for)
    wide.max_freq_hz = 4300.0f;

    int checked = 0, valid = 0, mismatches = 0;

    auto check = [&](kan::HybridDetector& det, float f_min, float f_max, const char* what) {
        const kan::PitchEstimate got = det.detect_pitch_into(window.data(), N, scratch.data(), scratch.size());
        const kan::PitchEstimate want = full_pass(window.data(), N, SR, f_min, f_max, 0.15f, reference);
        ++checked;
        if (want.valid) ++valid;
        if (!same(got, want)) {
            ++mismatches;
            std::printf("MISMATCH %s: got %d %.6f Hz, want %d %.6f Hz\n", what, got.valid, got.frequency_hz, want.valid, want.frequency_hz);
        }
    };

    kan::HybridDetector det(SR);
    det.set_profile(wide);

    // Tones with overtones across the whole range, at several levels and
    // phases; with noise; chirps; noise alone; near-silence.
    for (float hz = 30.0f; hz < 4300.0f; hz *= 1.0293f) {
        for (float level : {0.01f, 0.1f, 0.6f}) {
            for (float snr : {0.0f, 0.05f, 0.3f}) {
                const float phase = noise(rng);
                for (size_t i = 0; i < N; ++i) {
                    const float t = static_cast<float>(i) / SR;
                    window[i] = level * (0.7f * std::sin(2.0f * float(M_PI) * hz * t + phase)
                                         + 0.2f * std::sin(4.0f * float(M_PI) * hz * t)
                                         + 0.1f * std::sin(6.0f * float(M_PI) * hz * t))
                                + level * snr * noise(rng);
                }
                check(det, wide.min_freq_hz, wide.max_freq_hz, "tone");
            }
        }
    }
    for (int k = 0; k < 40; ++k) {
        const float f0 = 60.0f * std::pow(2.0f, k / 8.0f), f1 = f0 * 1.5f;
        for (size_t i = 0; i < N; ++i) {
            const float t = static_cast<float>(i) / SR;
            window[i] = 0.4f * std::sin(2.0f * float(M_PI) * (f0 * t + (f1 - f0) * t * t * SR / (2.0f * N)));
        }
        check(det, wide.min_freq_hz, wide.max_freq_hz, "chirp");
    }
    for (int k = 0; k < 40; ++k) {
        for (auto& s : window) s = 0.2f * noise(rng);
        check(det, wide.min_freq_hz, wide.max_freq_hz, "noise");
        for (auto& s : window) s = 0.001f * noise(rng);
        check(det, wide.min_freq_hz, wide.max_freq_hz, "near silence");
    }

    // Bounded around an expected note (stage 3).
    kan::HybridDetector bounded(SR);
    bounded.set_profile(wide);
    bounded.set_expected_note(523.25f);
    const float factor = std::pow(2.0f, 2.0f / 12.0f);
    for (float hz = 400.0f; hz < 700.0f; hz *= 1.01f) {
        for (size_t i = 0; i < N; ++i)
            window[i] = 0.3f * std::sin(2.0f * float(M_PI) * hz * static_cast<float>(i) / SR) + 0.02f * noise(rng);
        check(bounded, 523.25f / factor, 523.25f * factor, "bounded");
    }

    std::printf("yin early exit: %d windows, %d pitched, %d mismatches\n", checked, valid, mismatches);
    return mismatches == 0 && valid > checked / 3 ? 0 : 1;
}
