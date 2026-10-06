#pragma once
// Score-informed alternative monophonic detector based on Cycfi Q (MIT):
// BACF (Bitstream AutoCorrelation Function) period detection via XOR
// bitstream operations — no FFT, very low per-sample cost.
//
// Per SPECIFICATION.md §2 stage 3: when the map expects a single note, the
// detector is bounded to ±2 semitones of the expected note (see
// set_expected_note()), which both speeds up confirmation and eliminates
// octave errors.
//
// Threading: process_block() runs on the consumer thread fed by the ring
// buffer (never the audio callback). It does not allocate in steady state;
// (re)configuration via set_expected_note()/clear_expected_note() may
// allocate and belongs to the setup path.
//
// Requires the `libkan_bacf` CMake target (Cycfi Q headers).

#include <q/pitch/pitch_detector.hpp>
#include <q/support/frequency.hpp>
#include <q/support/literals.hpp>

#include <algorithm>
#include <cmath>
#include <cstddef>
#include <memory>

#include "dsp_detector.hpp"

namespace kan {

/// Score-informed BACF monophonic pitch detector (Cycfi Q backend).
/// Alternative to HybridDetector::detect_pitch (YIN); A/B results in
/// docs/dsp/bacf-vs-yin.md.
class BacfDetector {
public:
    /// Default search range when no expected note is set (matches the YIN
    /// detector's 60 Hz - 1200 Hz span).
    static constexpr float kDefaultMinHz = 60.0f;
    static constexpr float kDefaultMaxHz = 1200.0f;

    explicit BacfDetector(float sample_rate = 48000.0f)
        : sample_rate_(sample_rate) {
        rebuild(kDefaultMinHz, kDefaultMaxHz);
    }

    /// Score-informed mode: bound the BACF search to `window_semitones`
    /// around `expected_hz` (spec default: ±2 semitones). Rebuilds the
    /// internal detector; call from the setup/control path, not per-block.
    void set_expected_note(float expected_hz, float window_semitones = 2.0f) {
        expected_hz_ = expected_hz;
        window_semitones_ = window_semitones;
        const float semitone = std::pow(2.0f, 1.0f / 12.0f);
        const float lo = expected_hz / std::pow(semitone, window_semitones);
        const float hi = expected_hz * std::pow(semitone, window_semitones);
        rebuild(lo, hi);
    }

    /// Back to blind full-range detection.
    void clear_expected_note() {
        expected_hz_ = 0.0f;
        rebuild(kDefaultMinHz, kDefaultMaxHz);
    }

    /// Blind mode with an instrument-bounded range (e.g. ocarina
    /// 480–1450 Hz). Pass the InstrumentProfile's min/max frequencies.
    /// Rebuilds the internal detector; setup path only.
    void set_range(float min_hz, float max_hz) {
        expected_hz_ = 0.0f;
        rebuild(std::max(20.0f, min_hz), std::max(min_hz * 1.01f, max_hz));
    }

    bool has_expected_note() const noexcept { return expected_hz_ > 0.0f; }

    /// Discard accumulated state (e.g. between notes / after a miss).
    void reset() noexcept {
        pd_->reset();
        good_streak_ = 0;
    }

    /// Minimum block RMS required before samples reach the autocorrelation
    /// (spurious-period guard vs noise). Default 0.008 matches the YIN path.
    void set_min_rms(float rms) noexcept { min_rms_ = rms; }

    /// Minimum consecutive in-range, periodic results before a PitchResult
    /// is emitted (default 2 ~ "2 cycles" confirmation per spec §1).
    void set_confirm_frames(int frames) noexcept { confirm_frames_ = frames > 0 ? frames : 1; }

    /// Feed one block of preconditioned audio (consumer thread). Returns a
    /// valid PitchResult the moment the pitch is confirmed; invalid results
    /// otherwise. Steady-state processing does not allocate.
    PitchResult process_block(const float* samples, size_t count) noexcept {
        PitchResult out;

        // Cheap energy gate: pure noise / silence must never reach the
        // bitstream autocorrelation (it happily finds spurious periods in
        // white noise). Mirrors the YIN detector's minimum-RMS check.
        double acc = 0.0;
        for (size_t i = 0; i < count; ++i) acc += double(samples[i]) * double(samples[i]);
        const float rms = float(std::sqrt(acc / double(count ? count : 1)));
        if (rms < min_rms_) {
            good_streak_ = 0;
            return out;
        }

        for (size_t i = 0; i < count; ++i) {
            const bool ready = (*pd_)(samples[i]);
            if (!ready) continue;

            const float f = pd_->get_frequency();
            if (f <= 0.0f || pd_->periodicity() < min_periodicity_) {
                good_streak_ = 0;
                continue;
            }
            // Score-informed bound: reject anything outside ±window of the
            // expected note (BACF range already biases the search; this
            // guards harmonic/subharmonic escapes in wide-range mode).
            if (expected_hz_ > 0.0f) {
                const float cents = 1200.0f * std::log2(f / expected_hz_);
                if (std::fabs(cents) > 100.0f * window_semitones_) {
                    good_streak_ = 0;
                    continue;
                }
            }
            if (++good_streak_ >= confirm_frames_) {
                out.valid = true;
                out.frequency_hz = f;
                quantize_to_note(f, out.note_name, out.cents_offset);
                // Keep the streak so subsequent blocks confirm immediately
                // while the note sustains; reset() clears it.
                break;
            }
        }
        return out;
    }

    /// Current periodicity confidence [0..1] of the underlying BACF.
    float periodicity() const noexcept { return pd_->periodicity(); }

    /// Zero-crossing-based frequency prediction (Cycfi Q), useful as a
    /// fallback estimate before full BACF confirmation. 0 = no prediction.
    float predict_frequency() { return pd_->predict_frequency(); }

    float sample_rate() const noexcept { return sample_rate_; }

private:
    void rebuild(float lo_hz, float hi_hz) {
        using namespace cycfi::q::literals;
        pd_ = std::make_unique<cycfi::q::pitch_detector>(
            cycfi::q::frequency{static_cast<double>(lo_hz)},
            cycfi::q::frequency{static_cast<double>(hi_hz)},
            sample_rate_, -45_dB);
        good_streak_ = 0;
    }

    float sample_rate_;
    float expected_hz_ = 0.0f;
    float window_semitones_ = 2.0f;
    float min_periodicity_ = 0.8f; ///< Cycfi Q default confidence floor
    float min_rms_ = 0.008f;       ///< Energy gate vs spurious noise periods
    int confirm_frames_ = 2;
    int good_streak_ = 0;
    std::unique_ptr<cycfi::q::pitch_detector> pd_;
};

} // namespace kan
