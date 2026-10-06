// Offline test of the backing-track pitch-shift primitive (Signalsmith
// Stretch, MIT): shifting a tone by +Δc must move what the detector hears
// by exactly that amount, and time-stretching must preserve pitch (BPM
// changes, tuning does not). No audio device required.
#include "libkan/pitch_shift.hpp"
#include "libkan/dsp_detector.hpp"

#include <cmath>
#include <cstdio>
#include <vector>

namespace {

const float SR = 48000.0f;

// Feed a steady tone through the shifter and measure the output pitch.
float measure_shifted(float freq, float detune_cents, float stretch_ratio) {
    kan::PitchShifter shifter(SR);
    shifter.set_detune_cents(detune_cents);

    std::vector<float> out;
    const size_t in_block = 128;
    const size_t out_block = size_t(float(in_block) * stretch_ratio);
    std::vector<float> ibuf(in_block), obuf(out_block > 0 ? out_block : 1);

    double phase = 0.0;
    for (int b = 0; b < 400; ++b) { // ~1 s of audio
        for (size_t i = 0; i < in_block; ++i) {
            phase += 2.0 * M_PI * double(freq) / SR;
            ibuf[i] = 0.5f * float(std::sin(phase));
        }
        shifter.process(ibuf.data(), in_block, obuf.data(), out_block);
        out.insert(out.end(), obuf.begin(), obuf.end());
    }

    // Measure the steady tail (past latency + settling).
    kan::HybridDetector det(SR);
    det.set_expected_note(freq * std::pow(2.0f, detune_cents / 1200.0f));
    std::vector<float> rolling(2048, 0.0f);
    const size_t start = out.size() / 2;
    for (size_t off = start; off + 128 <= out.size(); off += 128) {
        std::move(rolling.begin() + 128, rolling.end(), rolling.begin());
        std::copy(out.begin() + off, out.begin() + off + 128, rolling.end() - 128);
        auto p = det.detect_pitch(rolling);
        if (p.valid) return p.frequency_hz;
    }
    return 0.0f;
}

} // namespace

int main() {
    bool pass = true;

    // 1. Consonance shift: A4 + 50 cents -> 452.9 Hz (BPM unchanged).
    {
        const float measured = measure_shifted(440.0f, 50.0f, 1.0f);
        const float expected = 440.0f * std::pow(2.0f, 50.0f / 1200.0f);
        printf("shift +50c: measured %.2f Hz (expect %.2f)\n", double(measured), double(expected));
        pass &= measured > 0.0f && std::fabs(measured - expected) < 8.0f;
    }

    // 2. Negative shift (flat instrument): A4 - 35 cents -> 431.1 Hz.
    {
        const float measured = measure_shifted(440.0f, -35.0f, 1.0f);
        const float expected = 440.0f * std::pow(2.0f, -35.0f / 1200.0f);
        printf("shift -35c: measured %.2f Hz (expect %.2f)\n", double(measured), double(expected));
        pass &= measured > 0.0f && std::fabs(measured - expected) < 8.0f;
    }

    // 3. Time-stretch 1.5x at zero detune: pitch preserved (BPM moves).
    {
        const float measured = measure_shifted(440.0f, 0.0f, 1.5f);
        printf("stretch 1.5x: measured %.2f Hz (expect 440)\n", double(measured));
        pass &= measured > 0.0f && std::fabs(measured - 440.0f) < 8.0f;
    }

    printf(pass ? "TEST PASS\n" : "TEST FAIL\n");
    return pass ? 0 : 1;
}
