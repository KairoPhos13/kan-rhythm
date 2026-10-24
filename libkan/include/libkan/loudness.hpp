#pragma once
// Programme loudness (ITU-R BS.1770-4), the measure streaming services and
// broadcasters level music by. A level's audio is mixed from tracks of any
// loudness: peak protection alone left one level near full scale and the
// next one far under it (owner 2026-10-23: "when I open the game my ears
// burst, then I have to turn it up because I can barely hear the songs").
// Levelling every render to one loudness is the fix; this measures it.
//
// Offline only (a whole file at a time): not for the audio callback path.
#include <algorithm>
#include <cmath>
#include <cstddef>
#include <limits>
#include <vector>

namespace kan {

namespace loudness_detail {

/// A biquad section (direct form II transposed), in double precision.
struct Biquad {
    double b0 = 1, b1 = 0, b2 = 0, a1 = 0, a2 = 0;
    double z1 = 0, z2 = 0;

    double run(double x) noexcept {
        const double y = b0 * x + z1;
        z1 = b1 * x - a1 * y + z2;
        z2 = b2 * x - a2 * y;
        return y;
    }
};

// The K-weighting's two stages, derived for any sample rate from the
// analogue prototypes BS.1770 specifies at 48 kHz (as libebur128 does):
// a high shelf of about +4 dB (the head's acoustic effect), then the
// revised low-frequency B-curve high-pass.

inline Biquad shelf(double sample_rate) {
    const double f0 = 1681.974450955533, gain_db = 3.999843853973347, q = 0.7071752369554196;
    const double k = std::tan(M_PI * f0 / sample_rate);
    const double vh = std::pow(10.0, gain_db / 20.0);
    const double vb = std::pow(vh, 0.4996667741545416);
    const double a0 = 1.0 + k / q + k * k;
    Biquad f;
    f.b0 = (vh + vb * k / q + k * k) / a0;
    f.b1 = 2.0 * (k * k - vh) / a0;
    f.b2 = (vh - vb * k / q + k * k) / a0;
    f.a1 = 2.0 * (k * k - 1.0) / a0;
    f.a2 = (1.0 - k / q + k * k) / a0;
    return f;
}

inline Biquad high_pass(double sample_rate) {
    const double f0 = 38.13547087602444, q = 0.5003270373238773;
    const double k = std::tan(M_PI * f0 / sample_rate);
    const double a0 = 1.0 + k / q + k * k;
    Biquad f;
    f.b0 = 1.0;
    f.b1 = -2.0;
    f.b2 = 1.0;
    f.a1 = 2.0 * (k * k - 1.0) / a0;
    f.a2 = (1.0 - k / q + k * k) / a0;
    return f;
}

} // namespace loudness_detail

/// Integrated loudness (LUFS) of mono samples, ITU-R BS.1770-4: the
/// K-weighted mean square over 400 ms blocks overlapping by 75 %, gated
/// at -70 LUFS, then at 10 LU under the mean of what passed. Minus
/// infinity when nothing passes (silence, or shorter than one block).
inline double integrated_loudness(const float* samples, size_t count, double sample_rate) {
    constexpr double minus_inf = -std::numeric_limits<double>::infinity();
    const size_t hop = static_cast<size_t>(sample_rate * 0.1);
    if (samples == nullptr || hop == 0 || count < 4 * hop) return minus_inf;

    // Energy of each 100 ms step of the K-weighted signal; a block is four steps.
    auto shelf = loudness_detail::shelf(sample_rate);
    auto hp = loudness_detail::high_pass(sample_rate);
    std::vector<double> steps(count / hop, 0.0);
    for (size_t i = 0; i < steps.size() * hop; ++i) {
        const double y = hp.run(shelf.run(samples[i]));
        steps[i / hop] += y * y;
    }

    std::vector<double> blocks;
    blocks.reserve(steps.size());
    for (size_t s = 0; s + 4 <= steps.size(); ++s)
        blocks.push_back((steps[s] + steps[s + 1] + steps[s + 2] + steps[s + 3]) / static_cast<double>(4 * hop));

    auto lufs_of = [](double mean_square) { return -0.691 + 10.0 * std::log10(mean_square); };
    auto gated_mean = [&](double gate_lufs) {
        double sum = 0;
        size_t n = 0;
        for (double z : blocks) {
            if (z > 0 && lufs_of(z) > gate_lufs) {
                sum += z;
                ++n;
            }
        }
        return n > 0 ? sum / static_cast<double>(n) : 0.0;
    };

    const double absolute = gated_mean(-70.0);
    if (absolute <= 0) return minus_inf;

    const double relative = gated_mean(lufs_of(absolute) - 10.0);
    return relative > 0 ? lufs_of(relative) : minus_inf;
}

/// The largest sample magnitude.
inline float sample_peak(const float* samples, size_t count) {
    float peak = 0.0f;
    for (size_t i = 0; i < count; ++i) peak = std::max(peak, std::fabs(samples[i]));
    return peak;
}

/// The linear gain that brings a signal of loudness `lufs` to `target_lufs`,
/// held so its `peak` stays at or under `ceiling` (linear): a quiet track
/// with sharp peaks comes up only as far as its peaks allow. 1 for silence.
inline double loudness_gain(double lufs, float peak, double target_lufs, double ceiling) {
    if (!std::isfinite(lufs) || peak <= 0.0f) return 1.0;

    const double gain = std::pow(10.0, (target_lufs - lufs) / 20.0);
    return std::min(gain, ceiling / static_cast<double>(peak));
}

} // namespace kan
