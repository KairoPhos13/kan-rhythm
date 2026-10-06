// Fixture generator for the loopback / regression harness: synthesizes the
// WAV set described in fixture_layout.hpp into a target directory.
// Run: libkan_fixture_gen <output_dir>   (ctest wires this automatically)
// Deterministic (fixed LCG noise): byte-identical output on every run.
#include "fixture_layout.hpp"
#include "wav_io.hpp"

#include <cmath>
#include <cstdio>
#include <cstring>
#include <string>
#include <sys/stat.h>
#include <sys/types.h>
#include <vector>

namespace {

using namespace kan_test;

uint32_t g_rng = 0x9E3779B9u;

void ensure_dir(const std::string& dir) {
    ::mkdir(dir.c_str(), 0755); // best effort: exists is fine
}
float noise1() {
    g_rng = g_rng * 1664525u + 1013904223u;
    return float(int(g_rng >> 8 & 0xFFFF)) / 32767.5f - 1.0f;
}

std::vector<float> silence(float seconds) {
    return std::vector<float>(size_t(seconds * kFixtureRate), 0.0f);
}

// Plucked-style note: pick-noise transient + decaying harmonic tone.
void add_pluck(std::vector<float>& out, size_t at, float freq_hz,
               float seconds, float amp = 0.5f) {
    const size_t n = size_t(seconds * kFixtureRate);
    for (size_t i = 0; at + i < out.size() && i < n; ++i) {
        const float t = float(i) / kFixtureRate;
        float s;
        if (t < 0.008f) {
            s = amp * 0.3f * noise1() * (1.0f - t / 0.008f);
        } else {
            const float d = std::exp(-2.5f * (t - 0.008f));
            s = amp * d * (std::sin(2.0f * float(M_PI) * freq_hz * t)
                         + 0.35f * std::sin(2.0f * float(M_PI) * 2.0f * freq_hz * t)
                         + 0.15f * std::sin(2.0f * float(M_PI) * 3.0f * freq_hz * t));
        }
        out[at + i] += s;
    }
}

void write(const std::string& dir, const char* name, const std::vector<float>& s) {
    const std::string path = dir + "/" + name;
    if (!write_wav(path, s, kFixtureRate)) {
        std::fprintf(stderr, "[fixture_gen] FAILED to write %s\n", path.c_str());
        std::exit(1);
    }
    std::printf("[fixture_gen] wrote %s (%.1f s)\n", name, float(s.size()) / kFixtureRate);
}

} // namespace

int main(int argc, char** argv) {
    const std::string dir = (argc > 1) ? argv[1] : "fixtures";
    ensure_dir(dir);
    g_rng = 0x9E3779B9u;

    // sine_sweep: logarithmic 80 -> 1200 Hz over 3 s.
    {
        auto s = silence(kSweepDuration);
        double phase = 0.0;
        const float T = kSweepDuration;
        for (size_t i = 0; i < s.size(); ++i) {
            const float t = float(i) / kFixtureRate;
            const float f = 80.0f * std::pow(1200.0f / 80.0f, t / T);
            phase += 2.0 * M_PI * double(f) / kFixtureRate;
            const float fade = std::min(1.0f, t / 0.01f) * std::min(1.0f, (T - t) / 0.01f);
            s[i] = fade * 0.5f * float(std::sin(phase));
        }
        write(dir, kFxSineSweep, s);
    }

    // detuned_notes: known onsets and detuned fundamentals.
    {
        auto s = silence(3.5f);
        for (const auto& e : kDetunedNotes) {
            add_pluck(s, size_t(e.t_seconds * kFixtureRate), e.freq_hz, 0.9f);
        }
        write(dir, kFxDetuned, s);
    }

    // chords: C major (60,64,67), C minor (60,63,67), C major with a wrong
    // fifth (60,64,68), strummed at 5 ms per string.
    {
        auto s = silence(5.0f);
        const int voicings[3][3] = {{60, 64, 67}, {60, 63, 67}, {60, 64, 68}};
        for (size_t c = 0; c < kChordCount; ++c) {
            const size_t at = size_t(kChordTimes[c] * kFixtureRate);
            for (int n = 0; n < 3; ++n) {
                const float f = 440.0f * std::pow(2.0f, (float(voicings[c][n]) - 69.0f) / 12.0f);
                add_pluck(s, at + size_t(n * 0.005f * kFixtureRate), f, 1.4f, 0.35f);
            }
        }
        write(dir, kFxChords, s);
    }

    // noise: room-level white noise (RMS ~0.002) — must produce ZERO onsets.
    {
        auto s = silence(kNoiseDuration);
        for (auto& v : s) v = 0.0025f * noise1();
        write(dir, kFxNoise, s);
    }

    // percussive: broadband bursts — onsets but no pitch.
    {
        auto s = silence(3.5f);
        for (const float t0 : kPercTimes) {
            const size_t at = size_t(t0 * kFixtureRate);
            const size_t n = size_t(0.03f * kFixtureRate);
            for (size_t i = 0; at + i < s.size() && i < n; ++i) {
                const float env = std::exp(-float(i) / (0.004f * kFixtureRate));
                s[at + i] += 0.5f * env * noise1();
            }
        }
        write(dir, kFxPercussive, s);
    }

    // onset_grid: 440 Hz bursts of 100 ms every second — the latency grid.
    {
        auto s = silence(kGridDuration);
        for (size_t g = 0; g < kGridCount; ++g) {
            const float t0 = kGridFirst + float(g) * kGridPeriod;
            const size_t at = size_t(t0 * kFixtureRate);
            const size_t n = size_t(0.1f * kFixtureRate);
            for (size_t i = 0; at + i < s.size() && i < n; ++i) {
                const float t = float(i) / kFixtureRate;
                const float attack = std::min(1.0f, t / 0.002f);
                s[at + i] = 0.5f * attack * std::sin(2.0f * float(M_PI) * 440.0f * t);
            }
        }
        write(dir, kFxOnsetGrid, s);
    }

    std::printf("[fixture_gen] done -> %s\n", dir.c_str());
    return 0;
}
