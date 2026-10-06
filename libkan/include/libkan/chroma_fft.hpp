#pragma once
// Score-informed chord validation, FFT backend (SPECIFICATION.md §2 stage 3):
// 12-bin chroma projection over the expected chord's fundamentals + first
// three harmonics, cosine similarity against the chart template
// (threshold ≈ 0.80). KissFFT (BSD-3) provides the real FFT; the projection
// itself is a fixed folding of |X(f)| into pitch classes.
//
// Complements GoertzelChordValidator (chord_goertzel.hpp, dependency-free):
// chroma is octave-invariant and cheap for wide templates, the Goertzel
// bank keeps per-note/per-harmonic resolution (better major-vs-minor
// separation in low registers). A/B numbers: libkan/tests/chord_test.cpp
// and docs/dsp/latency-measurements.md.
//
// Threading: the FFT config and all buffers are allocated at construction /
// set_expected_chord(); project() and validate() are allocation-free and
// run on the consumer thread. Input is const (non-consuming).
#include <kiss_fftr.h>

#include <algorithm>
#include <cmath>
#include <cstddef>
#include <cstdlib>
#include <vector>

#include "chord_goertzel.hpp" // ChordResult, midi_to_hz

namespace kan {

/// 12-bin chroma projector over a fixed-size real FFT (KissFFT).
class ChromaProjector {
public:
    /// `fft_size` must be even (rounded down if not). Frequency bins outside
    /// [f_lo, f_hi] are ignored (sub-bass rumble and hiss carry no chord
    /// identity).
    explicit ChromaProjector(float sample_rate = 48000.0f, size_t fft_size = 2048,
                             float f_lo = 65.0f, float f_hi = 2100.0f)
        : sample_rate_(sample_rate), fft_size_(fft_size & ~size_t(1)),
          f_lo_(f_lo), f_hi_(f_hi) {
        cfg_ = kiss_fftr_alloc(int(fft_size_), 0, nullptr, nullptr);
        time_buf_.assign(fft_size_, 0.0f);
        freq_buf_.assign(fft_size_ / 2 + 1, {0.0f, 0.0f});
        hann_.assign(fft_size_, 0.0f);
        for (size_t i = 0; i < fft_size_; ++i) {
            hann_[i] = 0.5f - 0.5f * std::cos(2.0f * float(M_PI) * float(i) / float(fft_size_ - 1));
        }
        // Precompute the pitch class of every bin (−1 = out of range).
        bin_pc_.assign(fft_size_ / 2 + 1, -1);
        for (size_t k = 0; k <= fft_size_ / 2; ++k) {
            const float f = float(k) * sample_rate_ / float(fft_size_);
            if (f < f_lo_ || f > f_hi_) continue;
            const float midi = 69.0f + 12.0f * std::log2(f / 440.0f);
            int pc = int(std::lround(midi)) % 12;
            if (pc < 0) pc += 12;
            bin_pc_[k] = pc;
        }
    }

    ~ChromaProjector() {
        if (cfg_) std::free(cfg_); // kiss_fftr_alloc uses malloc by default
    }

    ChromaProjector(const ChromaProjector&) = delete;
    ChromaProjector& operator=(const ChromaProjector&) = delete;
    ChromaProjector(ChromaProjector&&) = delete; // owns a raw kiss_fftr_cfg
    ChromaProjector& operator=(ChromaProjector&&) = delete;

    /// Project a window (<= fft_size samples; shorter windows are
    /// zero-padded) onto 12 chroma bins. `out` must hold 12 floats.
    /// Allocation-free.
    void project(const float* samples, size_t count, float* out) noexcept {
        std::fill(out, out + 12, 0.0f);
        const size_t n = std::min(count, fft_size_);
        for (size_t i = 0; i < fft_size_; ++i) {
            time_buf_[i] = (i < n) ? samples[i] * hann_[i] : 0.0f;
        }
        kiss_fftr(cfg_, time_buf_.data(), freq_buf_.data());
        for (size_t k = 0; k <= fft_size_ / 2; ++k) {
            const int pc = bin_pc_[k];
            if (pc < 0) continue;
            const float re = freq_buf_[k].r, im = freq_buf_[k].i;
            out[pc] += std::sqrt(re * re + im * im);
        }
    }

    size_t fft_size() const noexcept { return fft_size_; }

private:
    float sample_rate_;
    size_t fft_size_;
    float f_lo_, f_hi_;
    kiss_fftr_cfg cfg_ = nullptr;
    std::vector<float> time_buf_;
    std::vector<float> hann_;
    std::vector<kiss_fft_cpx> freq_buf_;
    std::vector<int> bin_pc_;
};

/// Score-informed chroma chord validator: builds the expected 12-bin chroma
/// template from the chart's chord (fundamentals + first three harmonics
/// folded into pitch classes) and validates a clean window by cosine
/// similarity (spec threshold ≈ 0.80).
class ChromaChordValidator {
public:
    static constexpr size_t kHarmonics = 4;
    static constexpr size_t kMaxNotes = 6;

    explicit ChromaChordValidator(float sample_rate = 48000.0f, size_t fft_size = 2048)
        : projector_(sample_rate, fft_size) {}

    /// Set the chord the chart expects (MIDI note numbers, A4 = 69). The
    /// template folds each note's fundamental + first three harmonics into
    /// pitch classes, weighted by the harmonic envelope. Returns false if
    /// `n` exceeds kMaxNotes.
    bool set_expected_chord(const float* midi_notes, size_t n) {
        if (n == 0 || n > kMaxNotes) return false;
        note_count_ = n;
        std::fill(template_, template_ + 12, 0.0f);
        for (size_t i = 0; i < n; ++i) {
            const float midi = midi_notes[i];
            for (size_t h = 0; h < kHarmonics; ++h) {
                const float midi_h = midi + 12.0f * std::log2(float(h + 1));
                int pc = int(std::lround(midi_h)) % 12;
                if (pc < 0) pc += 12;
                template_[pc] += harmonic_env_[h];
            }
        }
        // L2-normalize the template.
        double nb = 0.0;
        for (int p = 0; p < 12; ++p) nb += double(template_[p]) * template_[p];
        const float norm = float(std::sqrt(nb));
        if (norm > 1e-9f) {
            for (float& v : template_) v /= norm;
        }
        return true;
    }

    /// Expected harmonic envelope (instrument-pack template data);
    /// normalized internally. Default: decaying plucked-string series.
    /// Must be called BEFORE set_expected_chord() (the template is built
    /// from the envelope at chord-set time).
    void set_harmonic_envelope(const float* weights, size_t n) noexcept {
        float sum = 0.0f;
        for (size_t h = 0; h < kHarmonics; ++h) {
            harmonic_env_[h] = (h < n) ? std::max(0.0f, weights[h]) : 0.0f;
            sum += harmonic_env_[h];
        }
        if (sum > 1e-6f) {
            for (float& w : harmonic_env_) w /= sum;
        }
    }

    /// Cosine-similarity threshold for a confirmed chord (spec ≈ 0.80).
    void set_threshold(float t) noexcept { threshold_ = t; }

    size_t note_count() const noexcept { return note_count_; }

    /// Recommended analysis window (samples): ~21 ms at 48 kHz keeps
    /// blanking (8 ms) + window inside the <32 ms chord-verification gate.
    static size_t recommended_window(float sample_rate) noexcept {
        return GoertzelChordValidator::recommended_window(sample_rate);
    }

    /// Validate a window of clean audio. `ChordResult::note_presence` is
    /// not populated by the chroma backend (use the Goertzel validator for
    /// per-note presence). Allocation-free.
    ChordResult validate(const float* samples, size_t count) noexcept {
        ChordResult res;
        res.note_count = note_count_;
        if (note_count_ == 0 || count < 256) return res;

        double acc = 0.0;
        for (size_t i = 0; i < count; ++i) acc += double(samples[i]) * double(samples[i]);
        const float rms = float(std::sqrt(acc / double(count)));
        if (rms < min_rms_) return res;

        float chroma[12];
        projector_.project(samples, count, chroma);

        // L2-normalize the observed chroma, then cosine-dot against template.
        double na = 0.0, dot = 0.0;
        for (int p = 0; p < 12; ++p) {
            na += double(chroma[p]) * chroma[p];
            dot += double(chroma[p]) * template_[p];
        }
        if (na < 1e-18) return res;
        res.similarity = float(dot / std::sqrt(na)); // template_ is unit-norm
        res.valid = res.similarity >= threshold_;
        return res;
    }

private:
    ChromaProjector projector_;
    float threshold_ = 0.80f;
    float min_rms_ = 0.008f;
    float harmonic_env_[kHarmonics] = {0.533f, 0.267f, 0.133f, 0.067f};
    float template_[12] = {0};
    size_t note_count_ = 0;
};

} // namespace kan
