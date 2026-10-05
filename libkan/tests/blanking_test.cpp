// Offline test of stage-2 transient blanking and instrument profiles:
// a synthesized plucked-string note (pick noise transient, then steady tone)
// must have its pitch evaluation frozen during the blanking window and must
// resolve to the correct note afterwards. No microphone required.
#include "libkan/instrument_profile.hpp"
#include "libkan/dsp_detector.hpp"

#include <cmath>
#include <cstdio>
#include <random>
#include <vector>

int main() {
    const float SR = 48000.0f;
    bool pass = true;

    // --- 1. Profile presets and lookup ------------------------------------
    auto plucked = kan::InstrumentProfile::plucked();
    auto wind = kan::InstrumentProfile::wind_bowed();
    auto keys = kan::InstrumentProfile::keyboard();
    printf("blanking: plucked=%.0f ms wind/bowed=%.0f ms keyboard=%.0f ms\n",
           plucked.blanking_ms, wind.blanking_ms, keys.blanking_ms);
    pass &= plucked.blanking_ms == 8.0f && wind.blanking_ms == 15.0f
            && keys.blanking_ms == 6.0f;

    kan::InstrumentProfile parsed;
    pass &= kan::InstrumentProfile::from_name("plucked", parsed) && parsed.blanking_ms == 8.0f;
    pass &= kan::InstrumentProfile::from_name("wind", parsed) && parsed.blanking_ms == 15.0f;
    pass &= kan::InstrumentProfile::from_name("keyboard", parsed) && parsed.blanking_ms == 6.0f;
    pass &= !kan::InstrumentProfile::from_name("kazoo", parsed);

    // --- 2. Blanking window logic ------------------------------------------
    kan::HybridDetector det(SR);
    det.set_profile(plucked);
    printf("plucked blanking: t=4ms->%d t=9ms->%d (expect 1, 0)\n",
           int(det.in_transient_blanking(4.0f)), int(det.in_transient_blanking(9.0f)));
    pass &= det.in_transient_blanking(4.0f) && !det.in_transient_blanking(9.0f);

    det.set_profile(wind);
    printf("wind blanking: t=10ms->%d t=16ms->%d (expect 1, 0)\n",
           int(det.in_transient_blanking(10.0f)), int(det.in_transient_blanking(16.0f)));
    pass &= det.in_transient_blanking(10.0f) && !det.in_transient_blanking(16.0f);
    det.set_profile(plucked);

    // --- 3. Synthesized plucked note: 8 ms pick noise, then steady D4 ------
    // D4 = 293.66 Hz with two harmonics and a gentle decay.
    std::mt19937 rng(7);
    std::normal_distribution<float> noise(0.0f, 0.25f);
    const float F0 = 293.66f;
    const int total = static_cast<int>(0.20f * SR);
    std::vector<float> note(total);
    for (int i = 0; i < total; ++i) {
        const float t = float(i) / SR;
        const float decay = std::exp(-3.0f * t);
        float s = noise(rng) * std::exp(-t * 900.0f); // pick transient (~8 ms)
        if (t > 0.008f) {
            s += 0.5f * decay * (std::sin(2 * float(M_PI) * F0 * t)
                               + 0.3f * std::sin(2 * float(M_PI) * 2 * F0 * t)
                               + 0.1f * std::sin(2 * float(M_PI) * 3 * F0 * t));
        }
        note[i] = s;
    }

    // 3a. Pitch on a window covering ONLY the transient must not produce a
    //     confident D4 (this is what blanking protects against).
    std::vector<float> transient_window(note.begin(), note.begin() + 1024);
    auto p_transient = det.detect_pitch(transient_window);
    printf("transient-only window: valid=%d note=%s (must not be D4)\n",
           int(p_transient.valid), p_transient.note_name.c_str());
    pass &= !(p_transient.valid && p_transient.note_name == "D4");

    // 3b. Pitch on the steady-state window (post-blanking) must resolve D4
    //     within a few cents.
    std::vector<float> steady_window(note.begin() + 2048, note.begin() + 2048 + 2048);
    auto p_steady = det.detect_pitch(steady_window);
    printf("steady window: valid=%d note=%s freq=%.2f Hz cents=%+.1f\n",
           int(p_steady.valid), p_steady.note_name.c_str(),
           p_steady.frequency_hz, p_steady.cents_offset);
    pass &= p_steady.valid && p_steady.note_name == "D4"
            && std::fabs(p_steady.frequency_hz - F0) < 3.0f;

    // --- 4. Profile range bounding rejects out-of-range fundamentals -------
    // A2 (110 Hz) is below the wind/bowed profile range (min 190 Hz): the
    // bounded search must refuse to report it as a confident wind note.
    std::vector<float> low(2048);
    for (int i = 0; i < 2048; ++i) {
        const float t = float(i) / SR;
        low[i] = 0.5f * std::sin(2 * float(M_PI) * 110.0f * t)
               + 0.2f * std::sin(2 * float(M_PI) * 220.0f * t);
    }
    det.set_profile(wind);
    auto p_low = det.detect_pitch(low);
    printf("A2 under wind profile (min %.0f Hz): valid=%d freq=%.1f (expect rejected)\n",
           wind.min_freq_hz, int(p_low.valid), p_low.frequency_hz);
    pass &= !p_low.valid || p_low.frequency_hz >= 185.0f;

    printf(pass ? "TEST PASS\n" : "TEST FAIL\n");
    return pass ? 0 : 1;
}
