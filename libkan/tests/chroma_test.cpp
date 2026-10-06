// Offline test of the KissFFT chroma backend: projector sanity (a pure note
// must peak on its pitch class) and the chord battery as a secondary
// validator (the HarmonicChordValidator carries the F1 gate; chroma is
// octave-invariant template machinery for instrument packs — see
// docs/dsp/chord-validation.md). No microphone required.
#include "libkan/chroma_fft.hpp"
#include "chord_suite.hpp"

#include <cmath>
#include <cstdio>
#include <cstring>
#include <vector>

int main() {
    const float SR = 48000.0f;
    bool pass = true;

    // --- 1. Projector sanity: pure C4 peaks on pitch class 0 --------------
    kan::ChromaProjector proj(SR, 2048);
    std::vector<float> sine(2048);
    const float f_c4 = kan::GoertzelChordValidator::midi_to_hz(60.0f);
    for (size_t i = 0; i < sine.size(); ++i) {
        sine[i] = 0.5f * std::sin(2.0f * float(M_PI) * f_c4 * float(i) / SR);
    }
    float chroma[12];
    proj.project(sine.data(), sine.size(), chroma);
    int peak = 0;
    for (int p = 1; p < 12; ++p) if (chroma[p] > chroma[peak]) peak = p;
    printf("pure C4 chroma peak: pc=%d (expect 0)\n", peak);
    pass &= peak == 0;

    // --- 2. Chord battery: true chords verify; report discrimination ------
    float roots[] = {48, 52, 55, 60, 64, 69};
    auto trials = kan_test::build_chord_suite(roots, sizeof(roots) / sizeof(roots[0]));
    const size_t blank = 384;
    const size_t W = kan::ChromaChordValidator::recommended_window(SR);

    kan::ChromaChordValidator validator(SR, 2048);
    int true_valid = 0, argmax_ok = 0, false_valid = 0;
    float true_sim_sum = 0.0f;
    for (const auto& t : trials) {
        auto sig = kan_test::synth_chord_strum(t.notes, t.detune_c, 3, 0.30f, 5.0f);
        if (sig.size() < blank + W) { printf("FAIL: signal too short\n"); return 1; }
        float sims[4];
        int best = 0, truth = 0;
        for (int c = 0; c < 4; ++c) {
            validator.set_expected_chord(t.cand[c], 3);
            auto r = validator.validate(&sig[blank], W);
            sims[c] = r.similarity;
            if (r.valid && c == 0) { /* counted below for truth only */ }
            if (sims[c] > sims[best]) best = c;
            if (std::memcmp(t.cand[c], t.notes, 3 * sizeof(float)) == 0) truth = c;
        }
        validator.set_expected_chord(t.notes, 3);
        auto rt = validator.validate(&sig[blank], W);
        if (rt.valid) ++true_valid;
        true_sim_sum += rt.similarity;
        // false acceptance check against each non-true candidate
        for (int c = 0; c < 4; ++c) {
            if (c != truth) {
                validator.set_expected_chord(t.cand[c], 3);
                if (validator.validate(&sig[blank], W).valid) ++false_valid;
            }
        }
        if (best == truth) ++argmax_ok;
    }
    const size_t n = trials.size();
    printf("chroma: true verified %d/%zu (avg sim %.3f), argmax %d/%zu, false valid %d/%zu\n",
           true_valid, n, true_sim_sum / float(n), argmax_ok, n, false_valid, 3 * n);

    // Regression bounds for the secondary validator. The F1 gate is carried
    // by HarmonicChordValidator (chord_test.cpp); chroma's octave-folded
    // 12-bin template at 21 ms windows accepts few true chords (measured
    // ~8%, avg sim 0.61) but rejects confusions outright (0/72 false
    // acceptances) — documented in docs/dsp/chord-validation.md.
    pass &= true_valid >= 1;
    pass &= argmax_ok >= int(0.5f * float(n));
    pass &= false_valid == 0;

    printf(pass ? "TEST PASS\n" : "TEST FAIL\n");
    return pass ? 0 : 1;
}
