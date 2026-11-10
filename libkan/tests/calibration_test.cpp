// Offline test of the tuning-compensation layer (SPECIFICATION §2):
// the 2 s reference-note calibrator (Δc), the global + per-string
// compensation matrix, detector-target shifting, and the relative-tuning
// tonal-center tracker. Synthesized signals and deterministic readings,
// no microphone required.
#include "libkan/calibration.hpp"
#include "libkan/bounded_pitch.hpp"
#include "libkan/dsp_detector.hpp"

#include <cmath>
#include <cstdio>
#include <vector>

namespace {

const float SR = 48000.0f;

// Steady harmonic tone (no decay), the "long comfortable note" of the
// reference-note flow.
std::vector<float> steady_note(float hz, float seconds) {
    const size_t n = size_t(seconds * SR);
    std::vector<float> out(n);
    for (size_t i = 0; i < n; ++i) {
        const float t = float(i) / SR;
        out[i] = 0.5f * (std::sin(2.0f * float(M_PI) * hz * t)
                       + 0.2f * std::sin(2.0f * float(M_PI) * 2.0f * hz * t));
    }
    return out;
}

} // namespace

int main() {
    bool pass = true;

    // --- 1. Reference-note calibration: A4 +12 cents -> Δc ≈ +12 ----------
    {
        const float detune = 12.0f;
        const float true_hz = 440.0f * std::pow(2.0f, detune / 1200.0f);
        auto sig = steady_note(true_hz, 2.2f);

        kan::HybridDetector yin(SR);
        kan::DetuneCalibrator cal(2.0f);
        std::vector<float> rolling(2048, 0.0f);
        for (size_t off = 0; off + 128 <= sig.size(); off += 128) {
            std::move(rolling.begin() + 128, rolling.end(), rolling.begin());
            std::copy(sig.begin() + off, sig.begin() + off + 128, rolling.end() - 128);
            auto p = yin.detect_pitch(rolling);
            if (p.valid) cal.add_frequency(p.frequency_hz, float(off + 128) / SR);
        }
        auto m = cal.finish(69.0f); // guided: "play A4"
        printf("calibrate A4+12c: ready=%d readings=%zu Δc=%+.2f cents "
               "(measured %.2f Hz, spread %.2f)\n",
               int(cal.ready()), m.readings, m.delta_cents, m.measured_hz, m.spread_cents);
        pass &= m.valid && cal.ready() && std::fabs(m.delta_cents - 12.0f) < 3.0f;
        pass &= std::fabs(m.standard_hz - 440.0f) < 0.01f;

        // Unguided flow infers the nearest equal-tempered note.
        auto mu = cal.finish();
        printf("calibrate unguided: note=%s Δc=%+.2f cents\n",
               (mu.standard_midi == 69.0f ? "A4" : "?"), mu.delta_cents);
        pass &= mu.valid && mu.standard_midi == 69.0f;
    }

    // --- 2. Compensation matrix: global + per-string ----------------------
    {
        kan::TuningCompensation comp;
        comp.set_global(12.0f);
        const float target = comp.target_frequency(440.0f);
        printf("target_frequency(440, +12c) = %.3f Hz (expect ~443.05)\n", target);
        pass &= std::fabs(target - 443.053f) < 0.05f;

        const float strings[6] = {+5.0f, -3.0f, +8.0f, 0.0f, -6.0f, +2.0f};
        comp.set_strings(strings, 6);
        bool matrix_ok = comp.has_strings();
        for (int s = 0; s < 6; ++s) {
            const float want = 12.0f + strings[s];
            matrix_ok &= std::fabs(comp.correction_cents(s) - want) < 1e-3f;
        }
        matrix_ok &= std::fabs(comp.correction_cents(-1) - 12.0f) < 1e-3f;
        matrix_ok &= std::fabs(comp.correction_cents(99) - 12.0f) < 1e-3f;
        // Residual of a note that IS at the compensated target is ~0.
        const float target_s3 = comp.target_frequency(329.63f, 3);
        matrix_ok &= std::fabs(comp.residual_cents(target_s3, 329.63f, 3)) < 0.01f;
        printf("per-string matrix: %s\n", matrix_ok ? "ok" : "BROKEN");
        pass &= matrix_ok;
    }

    // --- 3. Detector-target integration: instrument +12 cents sharp -------
    // The chart expects A4 (440 Hz) but the instrument plays A4+12c. With
    // compensation the matcher's target shifts to 443.05 Hz, so the played
    // note confirms with ~0 residual.
    {
        kan::TuningCompensation comp;
        comp.set_global(12.0f);
        const float target_hz = comp.target_frequency(440.0f);

        kan::BoundedPitchMatcher matcher(SR);
        matcher.set_expected_note(target_hz);
        auto sig = steady_note(target_hz, 0.3f);

        const size_t need = matcher.window_samples();
        kan::PitchResult got;
        for (size_t off = 384; off + 128 <= sig.size(); off += 128) {
            const size_t count = std::min(off + 128 - 384, need);
            if (count < need) continue;
            got = matcher.match(&sig[off + 128 - need], need);
            if (got.valid) break;
        }
        const float residual = got.valid
            ? comp.residual_cents(got.frequency_hz, 440.0f) : 999.0f;
        printf("target-shifted match: valid=%d f=%.2f residual=%+.2f cents\n",
               int(got.valid), got.frequency_hz, residual);
        pass &= got.valid && std::fabs(residual) < 25.0f;

        // Without compensation the same note reads +12 cents; the judge
        // would penalize a correctly playing instrument.
        const float raw = got.valid ? 1200.0f * std::log2(got.frequency_hz / 440.0f) : 0.0f;
        printf("raw vs A440: %+.2f cents (the detune compensation removes)\n", raw);
        pass &= std::fabs(raw - 12.0f) < 3.0f;
    }

    // --- 4. Relative tuning: EMA tonal center, interval judgment ----------
    {
        kan::RelativeTuningTracker rel;
        // A fretless player holding +10 cents consistently: every interval is
        // in tune with the center, which converges toward +10.
        bool all_ok = true;
        kan::RelativeTuningTracker::IntervalResult r;
        for (int n = 0; n < 40; ++n) {
            r = rel.observe(60.0f + float(n % 12), 60.0f + float(n % 12) + 0.10f);
            all_ok &= r.interval_ok;
        }
        printf("relative (+10c consistent): all_ok=%d center=%+.1f cents\n",
               int(all_ok), rel.tonal_center_cents());
        pass &= all_ok && std::fabs(rel.tonal_center_cents() - 10.0f) < 2.0f;

        // A harmony-breaking note (+70 cents, i.e. +60 above the center)
        // must be flagged, and must NOT drag the center.
        r = rel.observe(64.0f, 64.6f);
        const float center_before = rel.tonal_center_cents();
        printf("relative (wrong note +60c): interval_ok=%d error=%+.1f\n",
               int(r.interval_ok), r.error_cents);
        pass &= !r.interval_ok && r.error_cents > 50.0f;

        // The next correct note is still judged against the old center.
        r = rel.observe(65.0f, 65.1f);
        printf("relative (recover): interval_ok=%d center=%+.1f\n",
               int(r.interval_ok), rel.tonal_center_cents());
        pass &= r.interval_ok && std::fabs(rel.tonal_center_cents() - center_before) < 1.0f;

        // Pure interval step report: +10 then +25 dev -> step +15 cents.
        kan::RelativeTuningTracker rel2;
        rel2.observe(60.0f, 60.10f);
        r = rel2.observe(64.0f, 64.25f);
        printf("relative (interval step): ΔΔ=%.1f cents (expect ~+15)\n",
               r.delta_interval_cents);
        pass &= std::fabs(r.delta_interval_cents - 15.0f) < 0.5f;
    }

    printf(pass ? "TEST PASS\n" : "TEST FAIL\n");
    return pass ? 0 : 1;
}
