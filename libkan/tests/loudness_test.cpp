// Programme loudness (ITU-R BS.1770-4): offline tests with known results.
#include "libkan/loudness.hpp"

#include <cmath>
#include <cstdio>
#include <vector>

namespace {

bool g_pass = true;

void check(bool ok, const char* what) {
    std::printf("  [%s] %s\n", ok ? " ok " : "FAIL", what);
    if (!ok) g_pass = false;
}

constexpr double kSr = 48000.0;

std::vector<float> tone(double seconds, double hz, double amplitude, double sr = kSr) {
    std::vector<float> x(static_cast<size_t>(seconds * sr));
    for (size_t i = 0; i < x.size(); ++i)
        x[i] = static_cast<float>(amplitude * std::sin(2.0 * M_PI * hz * static_cast<double>(i) / sr));
    return x;
}

void append(std::vector<float>& a, const std::vector<float>& b) { a.insert(a.end(), b.begin(), b.end()); }

void the_k_weighting_is_the_standards() {
    std::printf("the K-weighting at 48 kHz has BS.1770's coefficients\n");
    auto s = kan::loudness_detail::shelf(48000.0);
    auto h = kan::loudness_detail::high_pass(48000.0);
    check(std::fabs(s.b0 - 1.53512485958697) < 1e-9 && std::fabs(s.b1 + 2.69169618940638) < 1e-9
              && std::fabs(s.b2 - 1.19839281085285) < 1e-9 && std::fabs(s.a1 + 1.69065929318241) < 1e-9
              && std::fabs(s.a2 - 0.73248077421585) < 1e-9,
          "pre-filter (high shelf)");
    check(std::fabs(h.a1 + 1.99004745483398) < 1e-9 && std::fabs(h.a2 - 0.99007225036621) < 1e-9, "RLB high-pass");
}

void a_sine_reads_as_its_level() {
    std::printf("a 997 Hz sine of peak A in one channel reads 20 log A - 3.01 LUFS\n");
    for (double a : {1.0, 0.1, 0.01}) {
        auto x = tone(5.0, 997.0, a);
        double lufs = kan::integrated_loudness(x.data(), x.size(), kSr);
        char what[96];
        std::snprintf(what, sizeof(what), "peak %.2f: %.2f LUFS (expected %.2f)", a, lufs, 20 * std::log10(a) - 3.01);
        check(std::fabs(lufs - (20 * std::log10(a) - 3.01)) < 0.05, what);
    }

    auto x = tone(5.0, 997.0, 0.1, 44100.0);
    double lufs = kan::integrated_loudness(x.data(), x.size(), 44100.0);
    check(std::fabs(lufs + 23.01) < 0.05, "the same at 44.1 kHz");
}

void silence_and_quiet_parts_are_gated_out() {
    std::printf("silence and much quieter parts do not lower the loudness\n");
    auto loud = tone(5.0, 997.0, 0.1);

    // The tone's 47 whole blocks pass, and so do the three that straddle its
    // end (3/4, 1/2 and 1/4 of its energy), as the standard has it:
    // 48.5 / 50 of the tone's mean square, -0.13 dB.
    const double expected = 20 * std::log10(0.1) - 3.01 + 10 * std::log10(48.5 / 50.0);

    auto with_silence = loud;
    append(with_silence, std::vector<float>(static_cast<size_t>(5.0 * kSr), 0.0f));
    double a = kan::integrated_loudness(with_silence.data(), with_silence.size(), kSr);
    check(std::fabs(a - expected) < 0.02, "5 s of silence after: absolute gate");

    auto with_quiet = loud;
    append(with_quiet, tone(5.0, 997.0, 0.001));
    double b = kan::integrated_loudness(with_quiet.data(), with_quiet.size(), kSr);
    check(std::fabs(b - expected) < 0.02, "5 s 40 dB quieter after: relative gate (ungated it would read -26)");

    std::vector<float> nothing(static_cast<size_t>(2.0 * kSr), 0.0f);
    check(std::isinf(kan::integrated_loudness(nothing.data(), nothing.size(), kSr)), "silence: minus infinity");
    check(std::isinf(kan::integrated_loudness(loud.data(), 1000, kSr)), "shorter than a block: minus infinity");
}

void the_gain_reaches_the_target_within_the_ceiling() {
    std::printf("the gain brings a signal to the target, its peak held under the ceiling\n");
    const double ceiling = std::pow(10.0, -1.0 / 20.0);
    check(std::fabs(kan::loudness_gain(-30.0, 0.05f, -14.0, ceiling) - std::pow(10.0, 16.0 / 20.0)) < 1e-9,
          "a quiet track comes up 16 dB");
    check(std::fabs(kan::loudness_gain(-8.0, 0.99f, -14.0, ceiling) - std::pow(10.0, -6.0 / 20.0)) < 1e-9,
          "a loud one goes down 6 dB");
    check(std::fabs(kan::loudness_gain(-30.0, 0.5f, -14.0, ceiling) - ceiling / 0.5) < 1e-9,
          "a quiet one with sharp peaks comes up only as far as they allow");
    check(kan::loudness_gain(-std::numeric_limits<double>::infinity(), 0.0f, -14.0, ceiling) == 1.0, "silence: unchanged");

    auto x = tone(5.0, 997.0, 0.01);
    double before = kan::integrated_loudness(x.data(), x.size(), kSr);
    double g = kan::loudness_gain(before, kan::sample_peak(x.data(), x.size()), -14.0, ceiling);
    for (auto& s : x) s = static_cast<float>(s * g);
    check(std::fabs(kan::integrated_loudness(x.data(), x.size(), kSr) + 14.0) < 0.05, "applied: -14 LUFS");
}

} // namespace

int main() {
    the_k_weighting_is_the_standards();
    a_sine_reads_as_its_level();
    silence_and_quiet_parts_are_gated_out();
    the_gain_reaches_the_target_within_the_ceiling();
    std::printf(g_pass ? "ALL PASS\n" : "FAILURES\n");
    return g_pass ? 0 : 1;
}
