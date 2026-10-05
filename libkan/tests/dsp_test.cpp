// Offline test of the DSP pipeline: ring buffer integrity + onset + pitch,
// driven by a synthesized signal (no microphone required).
#include "libkan/ring_buffer.hpp"
#include "libkan/dsp_detector.hpp"

#include <cstdio>
#include <cmath>
#include <vector>

int main() {
    // 1. SPSC ring buffer round-trip
    kan::LockFreeRingBuffer<float, 1024> rb;
    float src[128], dst[128];
    for (int i = 0; i < 128; ++i) src[i] = i * 0.01f;
    bool ok_push = rb.push(src, 128);
    bool ok_pop  = rb.pop(dst, 128);
    bool data_ok = true;
    for (int i = 0; i < 128; ++i) if (src[i] != dst[i]) data_ok = false;
    printf("ring buffer: push=%d pop=%d data_ok=%d\n", ok_push, ok_pop, data_ok);

    // 2. Detector: 50 ms of silence followed by a 200 ms A4 tone @ 48 kHz,
    //    detuned +10 cents (442.54 Hz) with a second harmonic.
    const float SR = 48000.0f;
    kan::HybridDetector det(SR);
    std::vector<float> rolling(2048, 0.0f);

    bool onset_seen = false;
    kan::PitchResult final_pitch;
    int blocks_since_onset = 0;

    for (int b = 0; b < static_cast<int>(0.25f * SR / 128); ++b) {
        float block[128];
        for (int i = 0; i < 128; ++i) {
            float t = (b * 128 + i) / SR;
            float amp = (t < 0.05f) ? 0.0f : 0.5f; // silence, then tone
            block[i] = amp * (0.8f * sinf(2 * (float)M_PI * 442.54f * t)
                            + 0.2f * sinf(2 * (float)M_PI * 885.08f * t));
        }
        std::move(rolling.begin() + 128, rolling.end(), rolling.begin());
        std::copy(block, block + 128, rolling.end() - 128);

        if (det.process_onset_block(block, 128) && !onset_seen) {
            onset_seen = true;
            blocks_since_onset = 0;
            printf("onset detected at block %d (t=%.1f ms)\n", b, b * 128 * 1000.0f / SR);
        }
        if (onset_seen) {
            blocks_since_onset++;
            if (blocks_since_onset >= 8) {
                auto p = det.detect_pitch(rolling);
                // Keep updating until the window is fully steady-state.
                if (p.valid) final_pitch = p;
            }
        }
    }

    printf("onset_seen=%d\n", onset_seen);
    printf("pitch: valid=%d note=%s freq=%.2f Hz cents=%+.1f\n",
           final_pitch.valid, final_pitch.note_name.c_str(),
           final_pitch.frequency_hz, final_pitch.cents_offset);

    bool pass = ok_push && ok_pop && data_ok && onset_seen && final_pitch.valid
                && final_pitch.note_name == "A4"
                && fabsf(final_pitch.frequency_hz - 442.54f) < 3.0f;
    printf(pass ? "TEST PASS\n" : "TEST FAIL\n");
    return pass ? 0 : 1;
}
