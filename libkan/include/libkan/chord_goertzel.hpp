#pragma once
// Score-informed chord validation (SPECIFICATION.md §2 stage 3, polyphonic
// branch): a Goertzel filter bank aimed at the fundamentals + first three
// harmonics of the notes the chart expects, compared against the chart
// template by cosine similarity (threshold ≈ 0.80).
//
// Design notes:
// - The bank is *score-informed*: it measures only the frequencies the chart
//   asks about, never a blind pitch salience. Blind AMT needs 100-200 ms
//   windows and would kill the osu!-style feel; this confirms a 3-note
//   chord from ~21 ms of audio.
// - Energy at expected frequencies is normalized per harmonic against the
//   template's harmonic envelope, so a bright or dark instrument still
//   scores ~1.0 when it plays the right chord.
// - The similarity is a *pattern* comparison over the note set: a missing
//   or wrong note leaves its slots near zero and drags the score down,
//   which is what separates major / minor / one-wrong-string.
//
// Threading: validate() runs on the consumer thread and does not allocate
// (scratch is sized by set_expected_chord() on the setup path). The
// detector input is const and side-effect free, so the same buffer can feed
// a non-consuming kanfx tap.
#include <algorithm>
#include <cmath>
#include <cstddef>
#include <vector>

namespace kan {

/// Result of a score-informed chord validation over one analysis window.
struct ChordResult {
    bool valid = false;         ///< similarity >= threshold
    float similarity = 0.0f;    ///< cosine similarity vs the chart template [0, 1]
    /// Per-note presence in [0, 1]: how strongly each expected note sounds.
    /// Useful for judging "one wrong string" and for player feedback.
    static constexpr size_t kMaxResultNotes = 10;
    float note_presence[kMaxResultNotes] = {};
    size_t note_count = 0;
};

/// Goertzel-bank chord validator. Dependency-free (no FFT).
///
/// Feed it a CLEAN post-blanking window (chord strums have the same
/// inharmonic attack as single notes; see BoundedPitchMatcher for why
/// transient samples must stay out of correlation/DFT windows).
class GoertzelChordValidator {
public:
    /// Number of harmonics measured per expected note: the fundamental plus
    /// the first three harmonics (SPECIFICATION §2 stage 3).
    static constexpr size_t kHarmonics = 4;
    static constexpr size_t kMaxNotes = 6;

    explicit GoertzelChordValidator(float sample_rate = 48000.0f)
        : sample_rate_(sample_rate) {}

    /// Set the chord the chart expects, as MIDI note numbers (A4 = 69).
    /// Configures the bank; call from the setup path, not per-block.
    /// Returns false if `n` exceeds kMaxNotes.
    bool set_expected_chord(const float* midi_notes, size_t n) {
        if (n == 0 || n > kMaxNotes) return false;
        note_count_ = n;
        slots_ = n * kHarmonics;
        for (size_t i = 0; i < n; ++i) {
            const float f = midi_to_hz(midi_notes[i]);
            for (size_t h = 0; h < kHarmonics; ++h) {
                slot_freq_[i * kHarmonics + h] = f * float(h + 1);
            }
        }
        amps_.assign(slots_, 0.0f);
        return true;
    }

    size_t note_count() const noexcept { return note_count_; }

    /// Expected harmonic envelope (instrument-pack "sound-trick validation
    /// template", platform-architecture.md §5). Default is a decaying
    /// plucked-string envelope; normalized internally.
    void set_harmonic_envelope(const float* weights, size_t n) noexcept {
        for (size_t h = 0; h < kHarmonics; ++h) {
            harmonic_env_[h] = (h < n) ? std::max(0.0f, weights[h]) : 0.0f;
        }
        float sum = 0.0f;
        for (float w : harmonic_env_) sum += w;
        if (sum > 1e-6f) {
            for (float& w : harmonic_env_) w /= sum;
        }
    }

    /// Cosine-similarity threshold for a confirmed chord (spec ≈ 0.80).
    void set_threshold(float t) noexcept { threshold_ = t; }

    /// Minimum window RMS to attempt validation (skips silence / gated-out
    /// blocks). Matches the monophonic detectors' floor.
    void set_min_rms(float r) noexcept { min_rms_ = r; }

    /// Recommended analysis window (samples): ~21 ms at 48 kHz, which keeps
    /// blanking (8 ms) + window inside the <32 ms chord-verification gate.
    static size_t recommended_window(float sample_rate) noexcept {
        return size_t(1024.0f * sample_rate / 48000.0f);
    }

    /// Validate a window of clean audio against the expected chord.
    /// Allocation-free once set_expected_chord() has run.
    ChordResult validate(const float* samples, size_t count) const noexcept {
        ChordResult res;
        res.note_count = note_count_;
        if (note_count_ == 0 || count < 256 || amps_.empty()) return res;

        double acc = 0.0;
        for (size_t i = 0; i < count; ++i) acc += double(samples[i]) * double(samples[i]);
        const float rms = float(std::sqrt(acc / double(count)));
        if (rms < min_rms_) return res;

        // Goertzel magnitude at every expected slot (fundamental + 3 harmonics
        // per note). Cost: n*kHarmonics*count ~ 12*1024 = 12k multiply-adds.
        for (size_t s = 0; s < slots_; ++s) {
            amps_[s] = goertzel_magnitude(samples, count, slot_freq_[s]);
        }

        // Per-note presence: energy in this note's slots normalized by the
        // template envelope and by the strongest note, so an absent note
        // reads ~0 and a present one reads ~1 regardless of playing level.
        float note_energy[kMaxNotes] = {0};
        float max_energy = 1e-9f;
        for (size_t i = 0; i < note_count_; ++i) {
            float e = 0.0f;
            for (size_t h = 0; h < kHarmonics; ++h) {
                const float a = amps_[i * kHarmonics + h];
                const float w = std::max(harmonic_env_[h], 1e-3f);
                // Ratio against the expected envelope, capped: a note that is
                // present scores ~1 per harmonic; an absent one scores ~0.
                e += std::min(a / (w * rms * float(count) * 0.5f), 1.0f);
            }
            note_energy[i] = e / float(kHarmonics);
            max_energy = std::max(max_energy, note_energy[i]);
        }
        for (size_t i = 0; i < note_count_; ++i) {
            res.note_presence[i] = note_energy[i] / max_energy;
        }

        // Cosine similarity between the measured amplitude vector and the
        // chart template (envelope repeated per note). Both are non-negative,
        // so the similarity lands in [0, 1].
        double dot = 0.0, na = 0.0, nb = 0.0;
        for (size_t i = 0; i < note_count_; ++i) {
            for (size_t h = 0; h < kHarmonics; ++h) {
                const double a = amps_[i * kHarmonics + h];
                const double b = harmonic_env_[h];
                dot += a * b;
                na += a * a;
                nb += b * b;
            }
        }
        res.similarity = (na > 1e-18 && nb > 1e-18) ? float(dot / (std::sqrt(na) * std::sqrt(nb))) : 0.0f;
        res.valid = res.similarity >= threshold_;
        return res;
    }

    /// MIDI note number to frequency (A4 = 69 = 440 Hz).
    static float midi_to_hz(float midi) noexcept {
        return 440.0f * std::pow(2.0f, (midi - 69.0f) / 12.0f);
    }

private:
    /// Single-frequency Goertzel magnitude |X(f)| over `count` samples.
    float goertzel_magnitude(const float* x, size_t count, float freq) const noexcept {
        const double w = 2.0 * M_PI * double(freq) / double(sample_rate_);
        const double cr = std::cos(w), ci = -std::sin(w);
        double re = 1.0, im = 0.0;
        double xr = 0.0, xi = 0.0;
        for (size_t i = 0; i < count; ++i) {
            const double s = x[i];
            xr += s * re;
            xi += s * im;
            const double nre = re * cr - im * ci;
            im = re * ci + im * cr;
            re = nre;
        }
        return float(2.0 * std::sqrt(xr * xr + xi * xi) / double(count));
    }

    float sample_rate_;
    float threshold_ = 0.80f;
    float min_rms_ = 0.008f;
    float harmonic_env_[kHarmonics] = {0.533f, 0.267f, 0.133f, 0.067f}; // 1:(1/2):(1/4):(1/8)
    size_t note_count_ = 0;
    size_t slots_ = 0;
    float slot_freq_[kMaxNotes * kHarmonics] = {0};
    mutable std::vector<float> amps_;
};

} // namespace kan
