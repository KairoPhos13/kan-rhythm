// Shared chord-suite helpers for libkan offline tests: strummed synthesized
// triads and the major / minor / one-wrong-string discrimination battery
// used by the Milestone-1 chord gate (F1 > 94%). Deterministic (fixed
// detuning patterns), no microphone required.
#pragma once

#include "test_signals.hpp"

#include <cmath>
#include <cstddef>
#include <vector>

namespace kan_test {

/// Synthesize a strummed chord: each note is a plucked-string note (8 ms
/// pick transient + 6 harmonics with a bright 1/h pluck envelope, decaying)
/// starting `strum_ms` apart. The bright envelope is realistic for plucked
/// strings and carries the high-harmonic content that semitone discrimination
/// at short windows depends on.
inline std::vector<float> synth_chord_strum(const float* midis, const float* cents,
                                            size_t n, float seconds, float strum_ms = 10.0f,
                                            uint32_t seed = 11) {
    const size_t total = size_t(seconds * kSampleRate);
    std::vector<float> out(total, 0.0f);
    const float amps[6] = {1.0f, 0.5f, 0.333f, 0.25f, 0.2f, 0.167f}; // 1/h pluck
    for (size_t k = 0; k < n; ++k) {
        const size_t onset = size_t(strum_ms / 1000.0f * kSampleRate) * k;
        const float f = midi_to_hz(midis[k], cents[k]);
        for (size_t i = onset; i < total; ++i) {
            const float t = float(i - onset) / kSampleRate;
            float s;
            if (t < 0.008f) {
                s = 0.15f * noise_sample(seed + uint32_t(k), i) * (1.0f - t / 0.008f);
            } else {
                s = 0.5f * std::exp(-2.5f * (t - 0.008f)) * harmonic_wave(f * t, amps, 6);
            }
            out[i] += s;
        }
    }
    // Normalize so more notes / louder strums do not change the RMS scale.
    float peak = 1e-6f;
    for (float s : out) peak = std::max(peak, std::fabs(s));
    for (float& s : out) s *= 0.55f / peak;
    return out;
}

/// The three-way discrimination categories of the ROADMAP chord gate.
enum class ChordCategory { Major, Minor, WrongString };

struct ChordTrial {
    ChordCategory category;
    float root_midi;                 ///< e.g. 48 = C3
    float notes[3];                  ///< played chord (MIDI)
    float detune_c[3];               ///< per-note detuning in cents
    /// Candidate templates for the classification argmax, same order as
    /// names: {major, minor, wrong5up, wrong5down}.
    float cand[4][3];
};

/// Build the trial battery: for each root, play the major triad, the minor
/// triad, and two one-wrong-string variants (fifth ±1 semitone on the major
/// triad). Candidates are always the four same-root templates, mirroring
/// gameplay where the chart expects one specific chord and the confusions
/// are the plausible near-misses.
inline std::vector<ChordTrial> build_chord_suite(float roots[], size_t n_roots,
                                                 uint32_t /*seed*/ = 77) {
    std::vector<ChordTrial> trials;
    // Deterministic small detunings in ±15 cents (a real player's spread).
    const float detune_pattern[6][3] = {
        {+8, -5, +12}, {-11, +6, -3}, {+4, +14, -9},
        {-14, +2, +7}, {+10, -12, +5}, {-6, +9, +13},
    };
    size_t p = 0;
    for (size_t r = 0; r < n_roots; ++r) {
        const float root = roots[r];
        for (int cat = 0; cat < 4; ++cat) {
            ChordTrial t{};
            t.root_midi = root;
            const float major3[3] = {root, root + 4, root + 7};
            const float minor3[3] = {root, root + 3, root + 7};
            const float wrong_up[3] = {root, root + 4, root + 8};
            const float wrong_dn[3] = {root, root + 4, root + 6};
            std::copy(&major3[0], &major3[0] + 3, t.cand[0]);
            std::copy(&minor3[0], &minor3[0] + 3, t.cand[1]);
            std::copy(&wrong_up[0], &wrong_up[0] + 3, t.cand[2]);
            std::copy(&wrong_dn[0], &wrong_dn[0] + 3, t.cand[3]);

            switch (cat) {
            case 0: t.category = ChordCategory::Major; std::copy(&major3[0], &major3[0] + 3, t.notes); break;
            case 1: t.category = ChordCategory::Minor; std::copy(&minor3[0], &minor3[0] + 3, t.notes); break;
            case 2: t.category = ChordCategory::WrongString; std::copy(&wrong_up[0], &wrong_up[0] + 3, t.notes); break;
            default: t.category = ChordCategory::WrongString; std::copy(&wrong_dn[0], &wrong_dn[0] + 3, t.notes); break;
            }
            for (int i = 0; i < 3; ++i) {
                t.detune_c[i] = detune_pattern[p % 6][i];
                ++p;
            }
            trials.push_back(t);
        }
    }
    return trials;
}

} // namespace kan_test
