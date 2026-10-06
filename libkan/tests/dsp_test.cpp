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

    // 3. Ghost-onset regression: a decaying 3-note chord whose partials beat
    //    (C3's 3rd harmonic vs G3's 2nd, detuned by 0.4 Hz) must produce
    //    exactly ONE onset at its attack — tail fluctuations must not
    //    retrigger — and a melody attack 500 ms later must still be seen
    //    (a ghost onset's debounce would swallow it). Harmonics decay at
    //    piano-like rates (higher harmonics die first); with a constant
    //    relative decay the HF beating can exceed the x3.2 jump test and
    //    retrigger — that extreme case is documented as a limitation in
    //    docs/dsp/README.md, not asserted here.
    {
        kan::HybridDetector det2(SR);
        int onsets = 0;
        float onset_t[8] = {0};
        const int total = int(1.6f * SR / 128.0f);
        for (int b = 0; b < total; ++b) {
            float block[128];
            for (int i = 0; i < 128; ++i) {
                const int n = b * 128 + i;
                const float t = n / SR;
                float s = 0.0f;
                if (t >= 0.3f) {
                    const float tc = t - 0.3f;
                    const float chord[3] = {130.81f, 164.81f, 196.00f};
                    for (float f : chord) {
                        for (int h = 1; h <= 5; ++h) {
                            s += 0.30f * (0.5f / h) * expf(-(1.2f + 0.25f * h) * tc)
                               * sinf(2.0f * float(M_PI) * f * h * tc);
                        }
                    }
                }
                if (t >= 0.8f && t < 1.4f) {
                    const float tm = t - 0.8f;
                    for (int h = 1; h <= 5; ++h) {
                        s += 0.60f * (0.5f / h) * expf(-(1.0f + 0.25f * h) * tm)
                           * sinf(2.0f * float(M_PI) * 440.0f * h * tm);
                    }
                }
                block[i] = s;
            }
            if (det2.process_onset_block(block, 128) && onsets < 8) {
                onset_t[onsets++] = b * 128.0f / SR * 1000.0f;
            }
        }
        printf("beating-tail onsets: %d (t = %.1f", onsets, onset_t[0]);
        for (int k = 1; k < onsets; ++k) printf(", %.1f", onset_t[k]);
        printf(" ms)\n");
        const bool ghost_ok = onsets == 2
            && fabsf(onset_t[0] - 300.0f) < 10.0f   // chord attack
            && fabsf(onset_t[1] - 800.0f) < 10.0f;  // melody attack (not swallowed)
        printf(ghost_ok ? "ghost-onset regression ok\n" : "ghost-onset regression FAIL\n");
        pass = pass && ghost_ok;
    }

    printf(pass ? "TEST PASS\n" : "TEST FAIL\n");
    return pass ? 0 : 1;
}
