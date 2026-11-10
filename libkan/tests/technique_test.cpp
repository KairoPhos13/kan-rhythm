// Offline test of the technique validators (platform-architecture.md §5):
// vibrato trajectory analysis (incl. wide/slow Japanese-style vibrato and
// straight-tone passages), bend/slide curves, articulation envelope shapes,
// and spectral signature templates. Synthesized signals and synthetic
// trajectories, no microphone required.
#include "libkan/technique.hpp"
#include "libkan/dsp_detector.hpp"

#include <cmath>
#include <cstdio>
#include <vector>

namespace {

const float SR = 48000.0f;

// Trajectory helper: sinusoidal cents modulation (vibrato) or a ramp (bend).
kan::TrajectoryBuffer make_vibrato_traj(float rate_hz, float depth_cents,
                                        float duration_ms, float center_cents,
                                        float hop_ms = 5.0f) {
    kan::TrajectoryBuffer t;
    for (float ms = 0.0f; ms <= duration_ms; ms += hop_ms) {
        const float osc = std::sin(2.0f * float(M_PI) * rate_hz * ms / 1000.0f);
        t.add(ms, center_cents + depth_cents * osc);
    }
    return t;
}

kan::TrajectoryBuffer make_bend_traj(float start_c, float end_c, float duration_ms,
                                     kan::CurveShape shape, float hop_ms = 5.0f) {
    kan::TrajectoryBuffer t;
    for (float ms = 0.0f; ms <= duration_ms + 0.01f; ms += hop_ms) {
        float x = ms / duration_ms;
        if (x > 1.0f) x = 1.0f;
        const float s = (shape == kan::CurveShape::EaseOut) ? 1.0f - (1.0f - x) * (1.0f - x) : x;
        t.add(ms, start_c + (end_c - start_c) * s);
    }
    return t;
}

// Synthesized FM tone for the integration case: A4 with vibrato.
std::vector<float> fm_note(float f0, float rate_hz, float depth_cents, float seconds) {
    const size_t n = size_t(seconds * SR);
    std::vector<float> out(n);
    double phase = 0.0;
    for (size_t i = 0; i < n; ++i) {
        const float t = float(i) / SR;
        const float f = f0 * std::pow(2.0f, depth_cents * std::sin(2.0f * float(M_PI) * rate_hz * t) / 1200.0f);
        phase += 2.0 * M_PI * double(f) / double(SR);
        out[i] = 0.5f * float(std::sin(phase));
    }
    return out;
}

} // namespace

int main() {
    bool pass = true;

    // --- 1. Vibrato: rate/depth measurement + Japanese-style spec ---------
    {
        kan::VibratoAnalyzer vib;
        // Wide/slow "yuri-like" vibrato: 4 Hz, ±40 cents, 800 ms
        // (>= 3 cycles are required to claim periodicity; rate physics).
        auto traj = make_vibrato_traj(4.0f, 40.0f, 800.0f, 5.0f);
        auto m = vib.measure(traj);
        printf("vibrato 4Hz/40c: periodic=%d rate=%.2f Hz depth=%.1f c\n",
               int(m.periodic), m.rate_hz, m.depth_cents);
        pass &= m.periodic && std::fabs(m.rate_hz - 4.0f) < 0.4f
                && std::fabs(m.depth_cents - 40.0f) < 8.0f;

        kan::VibratoSpec yuri; // pack-style expectation
        yuri.rate_min_hz = 2.0f; yuri.rate_max_hz = 6.0f;
        yuri.min_depth_cents = 25.0f; yuri.max_depth_cents = 120.0f;
        yuri.min_duration_ms = 150.0f;
        auto r = vib.check(m, yuri);
        printf("yuri spec check: ok=%d score=%.2f\n", int(r.ok), r.score);
        pass &= r.ok;

        // Classical narrow vibrato spec rejects the wide/slow execution.
        kan::VibratoSpec classical;
        classical.rate_min_hz = 5.5f; classical.rate_max_hz = 7.5f;
        classical.min_depth_cents = 15.0f; classical.max_depth_cents = 45.0f;
        r = vib.check(m, classical);
        printf("classical spec vs yuri execution: ok=%d (expect 0)\n", int(r.ok));
        pass &= !r.ok;

        // Straight-tone passage judged with expect_straight.
        auto flat = make_vibrato_traj(5.0f, 0.5f, 300.0f, 3.0f);
        auto mf = vib.measure(flat);
        kan::VibratoSpec straight;
        straight.expect_straight = true;
        r = vib.check(mf, straight);
        printf("straight tone: periodic=%d ok=%d (expect 1,1)\n",
               int(mf.periodic), int(r.ok));
        pass &= !mf.periodic && r.ok;

        // A vibrato played where a straight tone is expected must fail.
        r = vib.check(m, straight);
        printf("vibrato where straight expected: ok=%d (expect 0)\n", int(r.ok));
        pass &= !r.ok;

        // Trill-like over-wide oscillation is not vibrato.
        auto trill = make_vibrato_traj(5.0f, 200.0f, 800.0f, 0.0f);
        r = vib.check(vib.measure(trill), yuri);
        printf("200c oscillation vs yuri spec: ok=%d (expect 0)\n", int(r.ok));
        pass &= !r.ok;
    }

    // --- 2. Bend / slide curves -------------------------------------------
    {
        kan::BendAnalyzer bend;
        auto traj = make_bend_traj(0.0f, 200.0f, 250.0f, kan::CurveShape::Linear);
        auto m = bend.measure(traj);
        printf("linear bend 0->200c: mono=%d start=%.1f end=%.1f fit=%.1f\n",
               int(m.monotonic), m.start_cents, m.end_cents, m.fit_error_cents);
        pass &= m.monotonic && std::fabs(m.start_cents) < 1.0f
                && std::fabs(m.end_cents - 200.0f) < 1.0f && m.fit_error_cents < 2.0f;

        kan::BendSpec slide;
        slide.start_cents = 0.0f; slide.end_cents = 200.0f;
        slide.duration_ms = 250.0f; slide.shape = kan::CurveShape::Linear;
        auto r = bend.check(m, slide, traj);
        printf("linear spec check: ok=%d score=%.2f\n", int(r.ok), r.score);
        pass &= r.ok;

        // Ease-out bend (string bend settling into the target).
        auto eot = make_bend_traj(0.0f, 200.0f, 250.0f, kan::CurveShape::EaseOut);
        auto me = bend.measure(eot);
        kan::BendSpec bend_spec = slide;
        bend_spec.shape = kan::CurveShape::EaseOut;
        r = bend.check(me, bend_spec, eot);
        printf("ease-out vs ease-out spec: ok=%d fit_err=%.1f\n",
               int(r.ok), me.fit_error_cents);
        pass &= r.ok;

        // The same execution against a strict linear spec must fail the fit.
        kan::BendSpec strict = slide;
        strict.fit_tolerance_cents = 15.0f;
        r = bend.check(me, strict, eot);
        printf("ease-out vs strict linear spec: ok=%d (expect 0)\n", int(r.ok));
        pass &= !r.ok;

        // Non-monotonic wobble fails a monotonic spec.
        kan::TrajectoryBuffer wobble;
        for (float ms = 0.0f; ms <= 250.0f; ms += 5.0f) {
            wobble.add(ms, 200.0f * ms / 250.0f + 30.0f * std::sin(ms / 12.0f));
        }
        r = bend.check(bend.measure(wobble), slide, wobble);
        printf("wobble vs monotonic spec: ok=%d (expect 0)\n", int(r.ok));
        pass &= !r.ok;
    }

    // --- 3. Articulation envelope shapes ----------------------------------
    {
        kan::EnvelopeAnalyzer env;
        const float hop = 5.0f;

        // Legato: 20 ms attack, 400 ms sustain, 40 ms release.
        std::vector<float> legato;
        for (float ms = 0.0f; ms <= 480.0f; ms += hop) {
            float v;
            if (ms < 20.0f) v = ms / 20.0f;
            else if (ms < 420.0f) v = 1.0f;
            else v = std::max(0.0f, 1.0f - (ms - 420.0f) / 60.0f);
            legato.push_back(v);
        }
        auto ml = env.measure(legato.data(), legato.size(), hop);
        kan::EnvelopeSpec spec;
        spec.articulation = kan::Articulation::Legato;
        spec.min_duration_ms = 300.0f; spec.min_sustain_ratio = 0.5f;
        auto r = env.check(ml, spec);
        printf("legato: dur=%.0f sus=%.2f ok=%d\n",
               ml.duration_ms, ml.sustain_ratio, int(r.ok));
        pass &= r.ok;

        // Staccato: quick attack, fast decay (~20 ms) -> low sustain ratio.
        std::vector<float> stacc;
        for (float ms = 0.0f; ms <= 120.0f; ms += hop) {
            const float v = (ms < 5.0f) ? ms / 5.0f : std::exp(-(ms - 5.0f) / 20.0f);
            stacc.push_back(v);
        }
        auto ms_ = env.measure(stacc.data(), stacc.size(), hop);
        kan::EnvelopeSpec stac_spec;
        stac_spec.articulation = kan::Articulation::Staccato;
        stac_spec.min_duration_ms = 50.0f; stac_spec.max_duration_ms = 250.0f;
        stac_spec.max_release_ms = 60.0f; stac_spec.min_sustain_ratio = 0.5f;
        r = env.check(ms_, stac_spec);
        printf("staccato: dur=%.0f rel=%.0f sus=%.2f ok=%d\n",
               ms_.duration_ms, ms_.release_ms, ms_.sustain_ratio, int(r.ok));
        pass &= r.ok;
        // The same envelope must NOT pass as legato.
        r = env.check(ms_, spec);
        printf("staccato vs legato spec: ok=%d (expect 0)\n", int(r.ok));
        pass &= !r.ok;

        // Mute: sharp peak, decay to -20 dB in ~100 ms.
        std::vector<float> mute;
        for (float ms = 0.0f; ms <= 160.0f; ms += hop) {
            mute.push_back(std::exp(-ms / 30.0f));
        }
        auto mm = env.measure(mute.data(), mute.size(), hop);
        kan::EnvelopeSpec mute_spec;
        mute_spec.articulation = kan::Articulation::Mute;
        mute_spec.min_accent_peak = 0.3f; mute_spec.mute_decay_ms = 150.0f;
        r = env.check(mm, mute_spec);
        printf("mute: decay20=%.0f sus=%.2f ok=%d\n",
               mm.decay_to_20db_ms, mm.sustain_ratio, int(r.ok));
        pass &= r.ok;
    }

    // --- 4. Spectral signature templates (extended techniques) ------------
    {
        // String-harmonic-like tone: weak fundamental, strong octave.
        const float f = 440.0f;
        std::vector<float> harm(size_t(0.1f * SR));
        for (size_t i = 0; i < harm.size(); ++i) {
            const float t = float(i) / SR;
            harm[i] = 0.5f * (0.1f * std::sin(2.0f * float(M_PI) * f * t)
                            + 1.0f * std::sin(2.0f * float(M_PI) * 2.0f * f * t)
                            + 0.2f * std::sin(2.0f * float(M_PI) * 3.0f * f * t));
        }
        // Normal pluck for contrast (strong fundamental).
        std::vector<float> pluck(size_t(0.1f * SR));
        for (size_t i = 0; i < pluck.size(); ++i) {
            const float t = float(i) / SR;
            pluck[i] = 0.5f * (1.0f * std::sin(2.0f * float(M_PI) * f * t)
                             + 0.5f * std::sin(2.0f * float(M_PI) * 2.0f * f * t)
                             + 0.33f * std::sin(2.0f * float(M_PI) * 3.0f * f * t));
        }

        kan::SpectralSignature sig;
        sig.slot_hz[0] = f;     sig.weight[0] = 0.1f;  // weak fundamental
        sig.slot_hz[1] = 2.0f * f; sig.weight[1] = 1.0f; // strong octave
        sig.slot_hz[2] = 3.0f * f; sig.weight[2] = 0.2f;
        sig.n_slots = 3;
        sig.threshold = 0.80f;

        kan::SpectralSignatureValidator ssv;
        ssv.set_signature(sig);
        auto mh = ssv.measure(harm.data(), harm.size(), SR);
        auto rh = ssv.check(mh);
        auto mp = ssv.measure(pluck.data(), pluck.size(), SR);
        auto rp = ssv.check(mp);
        printf("harmonic signature: harm=%.3f ok=%d | pluck=%.3f ok=%d\n",
               mh.similarity, int(rh.ok), mp.similarity, int(rp.ok));
        pass &= rh.ok && !rp.ok;
    }

    // --- 5. Integration: YIN trajectory of a true FM vibrato --------------
    {
        const float rate = 5.0f, depth = 35.0f;
        auto sig = fm_note(440.0f, rate, depth, 1.5f);
        kan::HybridDetector yin(SR);
        kan::TrajectoryBuffer traj;
        std::vector<float> window(1024, 0.0f);
        for (size_t off = 0; off + 128 <= sig.size(); off += 128) {
            std::move(window.begin() + 128, window.end(), window.begin());
            std::copy(sig.begin() + off, sig.begin() + off + 128, window.end() - 128);
            auto p = yin.detect_pitch(window);
            if (p.valid) {
                traj.add(1000.0f * float(off) / SR, p.cents_offset);
            }
        }
        kan::VibratoAnalyzer vib;
        auto m = vib.measure(traj);
        printf("integration YIN-FM: points=%zu periodic=%d rate=%.2f Hz depth=%.1f c\n",
               traj.size(), int(m.periodic), m.rate_hz, m.depth_cents);
        // The 21 ms YIN window attenuates depth; assert the rate, which is
        // the discriminator the game needs ("was vibrato performed at all").
        pass &= m.periodic && std::fabs(m.rate_hz - rate) < 1.0f;
    }

    // --- 6. Dynamics tiers: soft vs loud synthesized passage (Phase 3) ----
    {
        auto tone = [](float amp, float secs) {
            std::vector<float> s(size_t(secs * SR), 0.0f);
            for (size_t i = 0; i < s.size(); ++i) {
                const float t = float(i) / SR;
                s[i] = amp * std::exp(-t) * std::sin(2.0f * float(M_PI) * 440.0f * t);
            }
            return s;
        };
        auto peak = [](const std::vector<float>& s) {
            float p = 0.0f;
            for (float v : s) p = std::max(p, std::fabs(v));
            return p;
        };
        const float soft = peak(tone(0.10f, 0.5f)); // quiet passage note
        const float loud = peak(tone(0.80f, 0.5f)); // forte passage note
        const float mid = std::sqrt(soft * loud);

        // Calibration mode: soft/loud anchors fix the range ends.
        kan::DynamicsClassifier cal;
        const bool cal_ok = cal.calibrate(soft, loud);
        const auto t_soft = cal.observe(soft);
        const auto t_mid = cal.observe(mid);
        const auto t_loud = cal.observe(loud);
        printf("dynamics calibrated: ok=%d soft=%s mid=%s loud=%s\n", int(cal_ok),
               kan::dynamics_tier_name(t_soft), kan::dynamics_tier_name(t_mid),
               kan::dynamics_tier_name(t_loud));
        pass &= cal_ok && t_soft == kan::DynamicsTier::Pp
                && t_loud == kan::DynamicsTier::Ff && t_mid == kan::DynamicsTier::Mf;

        // Session mode: warming up until enough notes, then soft < loud and
        // the extremes land on pp / ff as the anchors converge.
        kan::DynamicsClassifier ses;
        bool warm = true;
        for (int i = 0; i < 8; ++i) {
            warm &= ses.observe(soft) == kan::DynamicsTier::WarmingUp;
        }
        int soft_max = 0, loud_min = 100;
        kan::DynamicsTier last_soft = kan::DynamicsTier::WarmingUp;
        kan::DynamicsTier last_loud = kan::DynamicsTier::WarmingUp;
        for (int i = 0; i < 4; ++i) {
            last_soft = ses.observe(soft);
            last_loud = ses.observe(loud);
            soft_max = std::max(soft_max, int(last_soft));
            loud_min = std::min(loud_min, int(last_loud));
        }
        printf("dynamics session: warmup=%d soft max=%s loud min=%s last soft=%s "
               "last loud=%s\n", int(warm), kan::dynamics_tier_name(
                   kan::DynamicsTier(soft_max)),
               kan::dynamics_tier_name(kan::DynamicsTier(loud_min)),
               kan::dynamics_tier_name(last_soft), kan::dynamics_tier_name(last_loud));
        pass &= warm && soft_max < loud_min && last_soft == kan::DynamicsTier::Pp
                && last_loud == kan::DynamicsTier::Ff;
    }

    printf(pass ? "TEST PASS\n" : "TEST FAIL\n");
    return pass ? 0 : 1;
}
