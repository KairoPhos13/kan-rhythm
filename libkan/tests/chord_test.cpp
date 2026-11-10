// Offline test of score-informed chord validation: major vs minor vs
// one-wrong-string discrimination over a synthesized strummed-triad battery.
// Gate (docs/ROADMAP.md Milestone 1): F1 > 94%, verification < 32 ms.
// Carried by HarmonicChordValidator (joint least-squares unmixing); the
// marginal GoertzelChordValidator is exercised as the spec's primitive and
// asserted only for correct-chord verification (its classification limit at
// 21 ms windows is documented in harmonic_chord.hpp / docs/dsp/).
// No microphone required.
#include "libkan/chord_goertzel.hpp"
#include "libkan/harmonic_chord.hpp"
#include "chord_suite.hpp"

#include <cmath>
#include <cstdio>
#include <cstring>
#include <vector>

namespace {

struct F1Meter {
    int tp[3] = {0}, fp[3] = {0}, fn[3] = {0};

    void add(int truth, int pred) {
        if (pred == truth) ++tp[truth];
        else { ++fp[pred]; ++fn[truth]; }
    }

    float macro_f1() const {
        float sum = 0.0f;
        for (int c = 0; c < 3; ++c) {
            const float prec = (tp[c] + fp[c]) ? float(tp[c]) / float(tp[c] + fp[c]) : 0.0f;
            const float rec = (tp[c] + fn[c]) ? float(tp[c]) / float(tp[c] + fn[c]) : 0.0f;
            sum += (prec + rec > 0.0f) ? 2.0f * prec * rec / (prec + rec) : 0.0f;
        }
        return sum / 3.0f;
    }
};

int candidate_to_class(int cand_idx) {
    return (cand_idx >= 2) ? 2 : cand_idx; // cand 2/3 are the wrong-string variants
}

} // namespace

int main() {
    const float SR = 48000.0f;
    bool pass = true;

    float roots[] = {48, 50, 52, 55, 57, 60, 62, 64, 67, 69}; // C3..A4
    auto trials = kan_test::build_chord_suite(roots, sizeof(roots) / sizeof(roots[0]));

    // Gate-compliant window: evaluation starts at the end of the 8 ms
    // blanking (384 samples) and spans 1024 samples (~21.3 ms) -> chord
    // verified 29.3 ms after the first string, inside the <32 ms gate even
    // with the 5 ms strum spread of the suite.
    const size_t blank = 384;
    const size_t W = kan::HarmonicChordValidator::recommended_window(SR); // 1024
    const float verify_ms = 1000.0f * float(blank + W) / SR;
    printf("verification completes at %.1f ms (gate < 32 ms), %zu trials\n",
           verify_ms, trials.size());
    pass &= verify_ms < 32.0f;

    // ---------------- Harmonic LS validator (the F1 gate) ----------------
    kan::HarmonicChordValidator ls_validator(SR, blank + W + 64);
    F1Meter meter;
    int argmax_ok = 0;
    float sim_sum_true = 0.0f, sim_min_true = 1.0f;
    float sim_sum_false = 0.0f, sim_max_false = 0.0f;
    int true_valid = 0, false_valid = 0, false_total = 0;
    int true_valid_hi = 0, true_total_hi = 0, false_valid_hi = 0, false_total_hi = 0;

    for (const auto& t : trials) {
        auto sig = kan_test::synth_chord_strum(t.notes, t.detune_c, 3, 0.30f, 5.0f);
        if (sig.size() < blank + W) { printf("FAIL: signal too short\n"); return 1; }

        float sims[4];
        bool valids[4];
        int best = 0;
        for (int c = 0; c < 4; ++c) {
            ls_validator.set_expected_chord(t.cand[c], 3);
            auto r = ls_validator.validate(&sig[blank], W);
            sims[c] = r.similarity;
            valids[c] = r.valid;
            if (sims[c] > sims[best]) best = c;
        }

        int truth_cand = 0;
        for (int c = 0; c < 4; ++c) {
            if (std::memcmp(t.cand[c], t.notes, 3 * sizeof(float)) == 0) truth_cand = c;
        }
        const bool hi_register = t.root_midi >= 60.0f; // C4 and above
        sim_sum_true += sims[truth_cand];
        sim_min_true = std::min(sim_min_true, sims[truth_cand]);
        if (valids[truth_cand]) ++true_valid;
        if (hi_register) {
            ++true_total_hi;
            if (valids[truth_cand]) ++true_valid_hi;
        }
        for (int c = 0; c < 4; ++c) {
            if (c != truth_cand) {
                sim_sum_false += sims[c];
                sim_max_false = std::max(sim_max_false, sims[c]);
                ++false_total;
                if (valids[c]) ++false_valid;
                if (hi_register) {
                    ++false_total_hi;
                    if (valids[c]) ++false_valid_hi;
                }
            }
        }
        if (best == truth_cand) ++argmax_ok;
        meter.add(int(t.category), candidate_to_class(best));
    }

    const float f1 = meter.macro_f1();
    const size_t n = trials.size();
    printf("[LS]  argmax correct: %d/%zu\n", argmax_ok, n);
    printf("[LS]  true-template similarity: avg %.3f min %.3f\n", sim_sum_true / float(n), sim_min_true);
    printf("[LS]  false-template similarity: avg %.3f max %.3f\n",
           sim_sum_false / float(3 * n), sim_max_false);
    printf("[LS]  macro F1 (major/minor/wrong-string): %.4f (gate > 0.94)\n", f1);
    printf("[LS]  threshold verdicts: true valid %d/%zu, false valid %d/%d "
           "(C4+ register: true %d/%d, false %d/%d)\n",
           true_valid, n, false_valid, false_total,
           true_valid_hi, true_total_hi, false_valid_hi, false_total_hi);

    // Gate: discrimination F1 via argmax over the confusion set.
    pass &= f1 > 0.94f;
    // True chords must verify at the spec threshold (0.80) + presence floor.
    pass &= true_valid >= int(0.9f * float(n));
    // Separation margin: true templates must clearly outscore false ones.
    pass &= sim_min_true > 0.90f;
    pass &= (sim_sum_true / float(n)) > (sim_sum_false / float(3 * n)) + 0.05f;
    // Threshold verdicts in the C4+ register (where a 21 ms window resolves
    // the major/minor third, see docs/dsp/chord-validation.md): few false
    // acceptances. Low-register absolute thresholds are a documented
    // physical limitation; argmax discrimination still holds there.
    pass &= true_valid_hi >= int(0.9f * float(true_total_hi));
    pass &= false_valid_hi <= int(0.25f * float(false_total_hi));

    // ---------------- Goertzel bank (spec primitive, marginal) -------------
    // Correct chords must verify at the 0.80 threshold; classification is a
    // documented non-goal at this window length (kernel mainlobe 94 Hz vs
    // 18.5 Hz semitone spacing at C4 register).
    kan::GoertzelChordValidator goertzel(SR);
    int goertzel_verified = 0;
    float g_sim_sum = 0.0f;
    for (const auto& t : trials) {
        auto sig = kan_test::synth_chord_strum(t.notes, t.detune_c, 3, 0.30f, 5.0f);
        goertzel.set_expected_chord(t.notes, 3); // the chart's actual chord
        auto r = goertzel.validate(&sig[blank], W);
        g_sim_sum += r.similarity;
        if (r.valid) ++goertzel_verified;
    }
    printf("[Goertzel] correct chords verified: %d/%zu (avg sim %.3f)\n",
           goertzel_verified, n, g_sim_sum / float(n));
    pass &= goertzel_verified >= int(0.9f * float(n));

    printf(pass ? "TEST PASS\n" : "TEST FAIL\n");
    return pass ? 0 : 1;
}
