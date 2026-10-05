#pragma once
#include <cstddef>
#include <cmath>
#include <algorithm>

namespace kan {

/// Stage 0 preconditioning (SPECIFICATION.md §2): a zero-latency high-pass
/// filter (default 35 Hz) that kills mic-stand / floor thumps, plus an
/// adaptive noise gate calibrated to the room's silence.
///
/// Real-time safety: the process paths below do not allocate, block, lock,
/// or perform I/O — they are safe to call from the consumer thread fed by
/// the ring buffer (and, being allocation-free, also from the audio
/// callback if a caller ever needs preconditioning pre-tap).

/// Single second-order IIR section (Direct Form I).
/// Output depends only on present and past samples: zero algorithmic
/// latency (no lookahead, no FFT, no delay line).
class Biquad {
public:
    Biquad() = default;

    Biquad(float b0, float b1, float b2, float a1, float a2)
        : b0_(b0), b1_(b1), b2_(b2), a1_(a1), a2_(a2) {}

    /// Process one sample. RT-safe.
    float process(float x) noexcept {
        const float y = b0_ * x + b1_ * x1_ + b2_ * x2_ - a1_ * y1_ - a2_ * y2_;
        x2_ = x1_;
        x1_ = x;
        y2_ = y1_;
        y1_ = y;
        return y;
    }

    void reset() noexcept { x1_ = x2_ = y1_ = y2_ = 0.0f; }

private:
    float b0_ = 1.0f, b1_ = 0.0f, b2_ = 0.0f, a1_ = 0.0f, a2_ = 0.0f;
    float x1_ = 0.0f, x2_ = 0.0f, y1_ = 0.0f, y2_ = 0.0f;
};

/// Fourth-order Butterworth high-pass filter (two cascaded second-order
/// sections, Q = 0.5412 / 1.3066). Zero latency, fixed-point-free,
/// allocation-free. Default cutoff 35 Hz per SPECIFICATION.md §2 stage 0.
class HighPassFilter {
public:
    HighPassFilter(float sample_rate = 48000.0f, float cutoff_hz = 35.0f) {
        configure(sample_rate, cutoff_hz);
    }

    /// (Re)design the filter for a new sample rate / cutoff. Not RT-safe
    /// (uses tanf); call from a setup thread only.
    void configure(float sample_rate, float cutoff_hz) noexcept {
        const float k = std::tan(static_cast<float>(M_PI) * cutoff_hz / sample_rate);
        sections_[0] = design_section(0.54119610f, k); // 4th-order Butterworth
        sections_[1] = design_section(1.30656300f, k); // pole Q values
    }

    /// Process one sample in place. RT-safe.
    float process(float x) noexcept {
        return sections_[1].process(sections_[0].process(x));
    }

    /// Process a block; `in` and `out` may alias. RT-safe.
    void process_block(const float* in, float* out, size_t count) noexcept {
        for (size_t i = 0; i < count; ++i) out[i] = process(in[i]);
    }

    void reset() noexcept {
        sections_[0].reset();
        sections_[1].reset();
    }

private:
    static Biquad design_section(float q, float k) noexcept {
        const float k2 = k * k;
        const float norm = 1.0f / (1.0f + k / q + k2);
        return Biquad(/*b0=*/norm, /*b1=*/-2.0f * norm, /*b2=*/norm,
                      /*a1=*/2.0f * (k2 - 1.0f) * norm,
                      /*a2=*/(1.0f - k / q + k2) * norm);
    }

    Biquad sections_[2];
};

/// Adaptive noise gate calibrated to the room's silence.
///
/// Usage: feed a short window of room silence through calibrate() (the
/// "background-noise calibration" of SPECIFICATION.md §3), then call
/// process_block() on every incoming block. The gate opens when the block
/// RMS rises `open_db` above the learned noise floor and closes with
/// hysteresis plus a smooth release so it never chops attacks or clicks.
/// The noise floor keeps adapting while the gate is closed, tracking slow
/// room changes (HVAC, crowds).
///
/// RT-safe: process paths never allocate, block, or do I/O.
class NoiseGate {
public:
    struct Config {
        float open_db = 9.0f;      ///< dB above noise floor at which the gate opens
        float close_db = 4.0f;     ///< dB above noise floor at which it closes (hysteresis)
        float attack_ms = 1.0f;    ///< gain ramp-up time once opened
        float release_ms = 25.0f;  ///< gain ramp-down time once closed
        float adapt_rate = 0.02f;  ///< noise-floor EMA rate while closed (per block)
        float fallback_floor = 0.002f; ///< RMS used before calibration
        Config() {}
    };

    explicit NoiseGate(float sample_rate = 48000.0f, const Config& cfg = Config())
        : sample_rate_(sample_rate), cfg_(cfg) {
        noise_floor_ = cfg_.fallback_floor;
    }

    /// Learn the room's silence level from a buffer of background noise.
    /// Call from a setup/calibration thread (not RT-critical).
    void calibrate(const float* samples, size_t count) noexcept {
        if (count == 0) return;
        double acc = 0.0;
        for (size_t i = 0; i < count; ++i) acc += static_cast<double>(samples[i]) * samples[i];
        const float rms = static_cast<float>(std::sqrt(acc / static_cast<double>(count)));
        // Never let calibration raise the floor unboundedly; EMA toward it.
        noise_floor_ = calibrated_ ? 0.5f * noise_floor_ + 0.5f * rms : rms;
        calibrated_ = true;
    }

    /// True once calibrate() has seen real room silence.
    bool is_calibrated() const noexcept { return calibrated_; }

    /// Current noise-floor estimate (linear RMS).
    float noise_floor_rms() const noexcept { return noise_floor_; }

    /// True while the gate is (partially) open.
    bool is_open() const noexcept { return gain_ > 0.5f; }

    /// Apply the gate to a block in place (`in` and `out` may alias).
    /// Returns true if the gate is open at the end of the block. RT-safe.
    bool process_block(const float* in, float* out, size_t count) noexcept {
        if (count == 0) return gain_ > 0.5f;

        // Block RMS (cheap; count is a fixed block size in practice).
        double acc = 0.0;
        for (size_t i = 0; i < count; ++i) acc += static_cast<double>(in[i]) * in[i];
        const float rms = static_cast<float>(std::sqrt(acc / static_cast<double>(count)));

        const float open_lin = noise_floor_ * db_to_lin(cfg_.open_db);
        const float close_lin = noise_floor_ * db_to_lin(cfg_.close_db);

        bool want_open;
        if (gain_ > 0.0f) {
            want_open = rms > close_lin; // already open: hysteresis on close
        } else {
            want_open = rms > open_lin;
        }

        // Adapt the floor to slow room drift while confidently closed.
        if (!want_open && gain_ <= 0.0f && rms > 0.0f) {
            noise_floor_ = (1.0f - cfg_.adapt_rate) * noise_floor_ + cfg_.adapt_rate * rms;
        }

        // Smooth gain transitions to avoid clicks / chopped attacks.
        const float attack_coef = time_coef(cfg_.attack_ms, count);
        const float release_coef = time_coef(cfg_.release_ms, count);
        const float target = want_open ? 1.0f : 0.0f;
        const float coef = (target > gain_) ? attack_coef : release_coef;
        gain_ += (target - gain_) * coef;
        if (gain_ < 1e-4f) gain_ = 0.0f;
        if (gain_ > 1.0f - 1e-4f) gain_ = 1.0f;

        const float g = gain_;
        for (size_t i = 0; i < count; ++i) out[i] = in[i] * g;
        return g > 0.5f;
    }

    void reset() noexcept {
        gain_ = 0.0f;
        noise_floor_ = calibrated_ ? noise_floor_ : cfg_.fallback_floor;
    }

private:
    static float db_to_lin(float db) noexcept { return std::pow(10.0f, db / 20.0f); }

    /// Fraction of the way to the target gain reached over one block.
    float time_coef(float ms, size_t count) const noexcept {
        if (ms <= 0.0f) return 1.0f;
        const float block_ms = 1000.0f * static_cast<float>(count) / sample_rate_;
        // Exponential approach: reach ~63% after `ms` milliseconds.
        return 1.0f - std::exp(-block_ms / ms);
    }

    float sample_rate_;
    Config cfg_;
    float noise_floor_ = 0.002f;
    float gain_ = 0.0f;
    bool calibrated_ = false;
};

/// Convenience: the full stage-0 chain (HPF + gate) in one object.
class Preconditioner {
public:
    explicit Preconditioner(float sample_rate = 48000.0f, float hpf_cutoff_hz = 35.0f)
        : hpf_(sample_rate, hpf_cutoff_hz), gate_(sample_rate) {}

    HighPassFilter& highpass() noexcept { return hpf_; }
    NoiseGate& gate() noexcept { return gate_; }

    /// Feed room-silence samples to the gate calibrator.
    void calibrate(const float* samples, size_t count) noexcept { gate_.calibrate(samples, count); }

    /// Process one block; `in` and `out` may alias (sample-by-sample
    /// in-place is safe for both stages). RT-safe.
    /// Returns true if the gate is open at the end of the block.
    bool process_block(const float* in, float* out, size_t count) noexcept {
        hpf_.process_block(in, out, count);
        return gate_.process_block(out, out, count);
    }

private:
    HighPassFilter hpf_;
    NoiseGate gate_;
};

} // namespace kan
