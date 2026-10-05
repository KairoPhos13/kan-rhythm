// Offline test of stage-0 preconditioning: 35 Hz high-pass attenuation and
// adaptive noise-gate behavior, driven by synthesized signals (no microphone).
#include "libkan/precondition.hpp"

#include <cmath>
#include <cstdio>
#include <random>
#include <vector>

// RMS of the last `tail` samples of a filtered run.
static float tail_rms(const std::vector<float>& v, size_t tail) {
    double acc = 0.0;
    const size_t start = v.size() - tail;
    for (size_t i = start; i < v.size(); ++i) acc += double(v[i]) * double(v[i]);
    return float(std::sqrt(acc / double(tail)));
}

// Run a sine through the filter and return passband/stopband RMS in dB.
static float filtered_db(float freq, float sr, float amp, kan::HighPassFilter& hpf, size_t n = 4800) {
    std::vector<float> out(n);
    for (size_t i = 0; i < n; ++i) {
        const float x = amp * std::sin(2.0f * float(M_PI) * freq * float(i) / sr);
        out[i] = hpf.process(x);
    }
    const float in_rms = amp / std::sqrt(2.0f);
    return 20.0f * std::log10(tail_rms(out, n / 2) / in_rms);
}

int main() {
    const float SR = 48000.0f;
    bool pass = true;

    // --- 1. High-pass: 440 Hz passes ~unity, 20 Hz strongly attenuated ----
    kan::HighPassFilter hpf(SR, 35.0f);
    const float db_440 = filtered_db(440.0f, SR, 0.5f, hpf);
    hpf.reset();
    const float db_20 = filtered_db(20.0f, SR, 0.5f, hpf);
    hpf.reset();
    const float db_10 = filtered_db(10.0f, SR, 0.5f, hpf);
    printf("hpf 440 Hz: %+.2f dB (expect > -1 dB)\n", db_440);
    printf("hpf  20 Hz: %+.2f dB (expect < -15 dB)\n", db_20);
    printf("hpf  10 Hz: %+.2f dB (expect < -28 dB)\n", db_10);
    pass &= (db_440 > -1.0f) && (db_20 < -15.0f) && (db_10 < -28.0f);

    // Zero-latency check: first output sample responds to first input sample.
    hpf.reset();
    const float first = hpf.process(1.0f);
    printf("hpf impulse first sample: %.4f (must be non-zero => zero latency)\n", first);
    pass &= (std::fabs(first) > 1e-6f);

    // --- 2. Noise gate: calibrated to synthetic room noise ---------------
    std::mt19937 rng(42); // fixed seed: deterministic CI
    std::normal_distribution<float> noise(0.0f, 0.002f); // quiet room, RMS ~0.002

    kan::NoiseGate gate(SR);
    // 0.5 s of room silence for calibration.
    std::vector<float> silence(24000);
    for (auto& s : silence) s = noise(rng);
    gate.calibrate(silence.data(), silence.size());
    printf("gate calibrated=%d noise_floor=%.5f (true 0.002)\n",
           int(gate.is_calibrated()), gate.noise_floor_rms());
    pass &= gate.is_calibrated()
            && std::fabs(gate.noise_floor_rms() - 0.002f) < 0.0005f;

    // Continued room noise must keep the gate closed (output ~silenced).
    std::vector<float> block(128);
    bool any_open = false;
    for (int b = 0; b < 100; ++b) {
        for (auto& s : block) s = noise(rng);
        any_open |= gate.process_block(block.data(), block.data(), block.size());
    }
    const float noise_out_rms = tail_rms(block, block.size());
    printf("gate on room noise: open=%d out_rms=%.6f (expect closed, < 0.0005)\n",
           int(any_open), noise_out_rms);
    pass &= !any_open && noise_out_rms < 0.0005f;

    // A loud tone must open the gate within one block and pass it.
    bool opened = false;
    for (int b = 0; b < 8 && !opened; ++b) {
        for (size_t i = 0; i < block.size(); ++i) {
            block[i] = 0.3f * std::sin(2.0f * float(M_PI) * 440.0f * float(b * 128 + i) / SR);
        }
        opened = gate.process_block(block.data(), block.data(), block.size());
    }
    const float tone_out_rms = tail_rms(block, block.size());
    printf("gate on tone: opened=%d out_rms=%.4f (expect open, > 0.1)\n",
           int(opened), tone_out_rms);
    pass &= opened && tone_out_rms > 0.1f;

    // After the tone stops, the gate must close again (release).
    bool closed_again = true;
    for (int b = 0; b < 200; ++b) {
        for (auto& s : block) s = noise(rng);
        gate.process_block(block.data(), block.data(), block.size());
        if (b > 100 && gate.is_open()) closed_again = false;
    }
    printf("gate recloses after tone: %d\n", int(closed_again));
    pass &= closed_again;

    // --- 3. Mic-stand thump rejection --------------------------------------
    // 3a. HPF alone: a loud 25 Hz stand thump is attenuated by > 10 dB.
    kan::HighPassFilter thump_hpf(SR, 35.0f);
    std::vector<float> thump(2400); // 50 ms thump burst
    for (size_t i = 0; i < thump.size(); ++i) {
        thump[i] = 0.5f * std::sin(2.0f * float(M_PI) * 25.0f * float(i) / SR);
    }
    std::vector<float> thump_out(thump.size());
    thump_hpf.process_block(thump.data(), thump_out.data(), thump.size());
    const float thump_db = 20.0f * std::log10(tail_rms(thump_out, 1200) / tail_rms(thump, 1200));
    printf("hpf on 25 Hz thump: %+.2f dB (expect < -10 dB)\n", thump_db);
    pass &= thump_db < -10.0f;

    // 3b. Full chain: room noise + a residual low-frequency thump stays
    //     below the gate open threshold -> output silenced, no ghost onsets.
    kan::Preconditioner pre(SR);
    pre.calibrate(silence.data(), silence.size());
    std::vector<float> scratch(128);
    float max_out = 0.0f;
    for (int b = 0; b < 400; ++b) {
        for (size_t i = 0; i < scratch.size(); ++i) {
            const size_t n = b * 128 + i;
            // Room noise plus a weak periodic 25 Hz mic-stand thump.
            scratch[i] = noise(rng) + 0.01f * std::sin(2.0f * float(M_PI) * 25.0f * float(n) / SR);
        }
        pre.process_block(scratch.data(), scratch.data(), scratch.size());
        for (float s : scratch) max_out = std::max(max_out, std::fabs(s));
    }
    printf("preconditioner on noise+weak thumps: max_out=%.5f (expect < 0.001)\n", max_out);
    pass &= max_out < 0.001f;

    printf(pass ? "TEST PASS\n" : "TEST FAIL\n");
    return pass ? 0 : 1;
}
