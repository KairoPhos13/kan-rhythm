// Shared synthesized-signal helpers for libkan offline tests (no microphone
// required; deterministic so CI is reproducible). Test-local, not part of the
// public library API.
#pragma once

#include <cmath>
#include <cstdint>
#include <cstddef>
#include <vector>

namespace kan_test {

inline constexpr float kSampleRate = 48000.0f;

/// Deterministic pseudo-noise sample in [-1, 1] (splitmix32-style hash).
inline float noise_sample(uint32_t seed, size_t i) {
    uint32_t h = uint32_t(i) * 2654435761u + seed * 40503u;
    h ^= h >> 16;
    h *= 0x7feb352du;
    h ^= h >> 15;
    h *= 0x846ca68bu;
    h ^= h >> 16;
    return float(int(h & 0xFFFF)) / 32767.5f - 1.0f;
}

/// One period of a harmonic waveform normalized to peak 1.0. `harmonic_amps`
/// holds relative amplitudes of partials 1..N (empty = pure sine).
inline float harmonic_wave(float phase_01, const float* amps, size_t n_amps) {
    double sum = 0.0;
    double norm = 0.0;
    for (size_t k = 0; k < n_amps; ++k) {
        sum += amps[k] * std::sin(2.0 * M_PI * double(k + 1) * phase_01);
        norm += amps[k];
    }
    if (n_amps == 0) {
        sum = std::sin(2.0 * M_PI * phase_01);
        norm = 1.0;
    }
    return float(sum / norm);
}

/// Synthesize a plucked-style note: 8 ms pick transient, then a decaying
/// harmonic tone at `freq_hz` detuned by `cents`. Phase-continuous so the
/// waveform has no step discontinuities (which would fake out pitch trackers).
inline std::vector<float> synth_note(float freq_hz, float cents, float seconds = 0.25f,
                                     uint32_t seed = 1) {
    const float f = freq_hz * std::pow(2.0f, cents / 1200.0f);
    const size_t n = size_t(seconds * kSampleRate);
    std::vector<float> out(n);
    const float amps[4] = {1.0f, 0.35f, 0.15f, 0.05f};
    for (size_t i = 0; i < n; ++i) {
        const float t = float(i) / kSampleRate;
        if (t < 0.008f) {
            out[i] = 0.15f * noise_sample(seed, i) * (1.0f - t / 0.008f);
        } else {
            const float decay = std::exp(-2.5f * (t - 0.008f));
            out[i] = 0.5f * decay * harmonic_wave(f * t, amps, 4);
        }
    }
    return out;
}

/// Synthesize a chord: sum of plucked-style notes, peak-normalized to 0.6.
inline std::vector<float> synth_chord(const float* freqs, size_t n_freqs,
                                      float seconds = 0.25f, uint32_t seed = 2) {
    const size_t n = size_t(seconds * kSampleRate);
    std::vector<float> out(n, 0.0f);
    for (size_t k = 0; k < n_freqs; ++k) {
        auto note = synth_note(freqs[k], 0.0f, seconds, seed + uint32_t(k));
        for (size_t i = 0; i < n; ++i) out[i] += note[i];
    }
    float peak = 1e-6f;
    for (float s : out) peak = std::max(peak, std::fabs(s));
    for (float& s : out) s *= 0.6f / peak;
    return out;
}

/// Pure white noise at the given RMS.
inline std::vector<float> synth_noise(float seconds, float rms, uint32_t seed = 3) {
    const size_t n = size_t(seconds * kSampleRate);
    std::vector<float> out(n);
    for (size_t i = 0; i < n; ++i) out[i] = rms * 1.7f * noise_sample(seed, i);
    return out;
}

/// Frequency (Hz) of an equal-tempered MIDI note number (A4 = 69 = 440 Hz).
inline float midi_to_hz(float midi) { return 440.0f * std::pow(2.0f, (midi - 69.0f) / 12.0f); }

/// Frequency (Hz) of a MIDI note detuned by cents.
inline float midi_to_hz(float midi, float cents) {
    return midi_to_hz(midi) * std::pow(2.0f, cents / 1200.0f);
}

} // namespace kan_test
