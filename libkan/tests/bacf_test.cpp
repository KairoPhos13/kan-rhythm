// Offline test of the Cycfi Q (BACF) score-informed monophonic detector:
// synthesized steady tones with harmonics, known input -> expected output.
// No microphone required.
#include "libkan/bacf_detector.hpp"
#include "test_signals.hpp"

#include <cmath>
#include <cstdio>
#include <vector>

namespace {

const float SR = 48000.0f;

// Feed the signal in 128-sample blocks; return the first valid result and
// the confirmation latency in ms from the start of the signal.
struct RunResult {
    kan::PitchResult pitch;
    float latency_ms = -1.0f;
    int blocks = 0;
};

RunResult run(kan::BacfDetector& det, const std::vector<float>& sig) {
    RunResult r;
    const size_t B = 128;
    for (size_t off = 0; off + B <= sig.size(); off += B) {
        ++r.blocks;
        auto p = det.process_block(sig.data() + off, B);
        if (p.valid && !r.pitch.valid) {
            r.pitch = p;
            r.latency_ms = 1000.0f * float(off + B) / SR;
        }
    }
    return r;
}

} // namespace

int main() {
    bool pass = true;

    // --- 1. Score-informed: expected A4, input A4 +10 cents ---------------
    {
        kan::BacfDetector det(SR);
        det.set_expected_note(440.0f); // ±2 semitones per spec §2 stage 3
        auto sig = kan_test::synth_note(440.0f, +10.0f);
        auto r = run(det, sig);
        const float err_cents = r.pitch.valid
            ? 1200.0f * std::log2(r.pitch.frequency_hz / (440.0f * std::pow(2.0f, 10.0f / 1200.0f)))
            : 999.0f;
        printf("expected A4, input A4+10c: valid=%d note=%s freq=%.2f err=%+.1f cents "
               "latency=%.1f ms\n",
               int(r.pitch.valid), r.pitch.note_name.c_str(), r.pitch.frequency_hz,
               err_cents, r.latency_ms);
        pass &= r.pitch.valid && r.pitch.note_name == "A4"
                && std::fabs(err_cents) < 15.0f && r.latency_ms < 40.0f;
    }

    // --- 2. Range bounding: expected A4, input A3 (octave below) -----------
    {
        kan::BacfDetector det(SR);
        det.set_expected_note(440.0f);
        auto sig = kan_test::synth_note(220.0f, 0.0f);
        auto r = run(det, sig);
        printf("expected A4, input A3: valid=%d (expect rejected)\n", int(r.pitch.valid));
        pass &= !r.pitch.valid;
    }

    // --- 3. Range bounding: expected A4, input C4 (out of ±2 st) ----------
    {
        kan::BacfDetector det(SR);
        det.set_expected_note(440.0f);
        auto sig = kan_test::synth_note(261.63f, 0.0f);
        auto r = run(det, sig);
        printf("expected A4, input C4: valid=%d (expect rejected)\n", int(r.pitch.valid));
        pass &= !r.pitch.valid;
    }

    // --- 4. Blind mode (no expected note): D4 must be found ---------------
    {
        kan::BacfDetector det(SR);
        auto sig = kan_test::synth_note(293.66f, -5.0f);
        auto r = run(det, sig);
        printf("blind mode, input D4-5c: valid=%d note=%s freq=%.2f latency=%.1f ms\n",
               int(r.pitch.valid), r.pitch.note_name.c_str(), r.pitch.frequency_hz,
               r.latency_ms);
        pass &= r.pitch.valid && r.pitch.note_name == "D4"
                && std::fabs(r.pitch.frequency_hz - 293.66f * std::pow(2.0f, -5.0f / 1200.0f)) < 4.0f;
    }

    // --- 5. Low E2 (82.41 Hz, guitar low string), score-informed ----------
    {
        kan::BacfDetector det(SR);
        det.set_expected_note(82.41f);
        auto sig = kan_test::synth_note(82.41f, +8.0f, 0.35f);
        auto r = run(det, sig);
        printf("expected E2, input E2+8c: valid=%d freq=%.2f latency=%.1f ms "
               "(gate: <22 ms for >82 Hz)\n",
               int(r.pitch.valid), r.pitch.frequency_hz, r.latency_ms);
        pass &= r.pitch.valid && std::fabs(r.pitch.frequency_hz - 82.41f) < 2.0f;
    }

    // --- 6. Room-level noise must not confirm --------------------------------
    // (In the production pipeline the stage-0 noise gate silences room noise
    // before the detector; this exercises the detector's own RMS guard.)
    {
        kan::BacfDetector det(SR);
        det.set_expected_note(440.0f);
        auto noise = kan_test::synth_noise(0.25f, 0.005f);
        auto r = run(det, noise);
        printf("room noise: valid=%d (expect rejected)\n", int(r.pitch.valid));
        pass &= !r.pitch.valid;
    }

    printf(pass ? "TEST PASS\n" : "TEST FAIL\n");
    return pass ? 0 : 1;
}
