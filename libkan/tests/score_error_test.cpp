// Counterexample suite: when the player makes mistakes, the measurement
// must FAIL LOUDLY (never smooth the error away). Score-anchored
// verification (score_verify.hpp) is tested against players that:
//   1. play correctly          (control: everything must verify)
//   2. play a wrong pitch      (B3 where the score says C4)
//   3. skip a note             (G3 never played)
//   4. add an extra note       (E4 where the score says silence)
//   5. play late               (C4 arrives 200 ms after its written onset)
//   6. hold too long           (G3 held past its written end)
// Every mistake must leave a measurable signature — that is what makes the
// tool usable for judging: silence where there should be a note, or energy
// where the score has none.
#include "libkan/score_verify.hpp"

#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <vector>

namespace {

constexpr float kSr = 48000.0f;
bool g_pass = true;

void check(bool ok, const char* what) {
    std::printf("  [%s] %s\n", ok ? " ok " : "FAIL", what);
    if (!ok) g_pass = false;
}

/// Harmonic note with a short attack transient (deterministic, realistic:
/// uncorrelated partial phases, per-harmonic decay).
void add_note(std::vector<float>& sig, float t0, float dur, float midi,
              float amp, uint32_t seed) {
    const float f = 440.0f * std::pow(2.0f, (midi - 69.0f) / 12.0f);
    const size_t at = size_t(t0 / 1000.0f * kSr);
    const size_t n = size_t(dur / 1000.0f * kSr);
    for (size_t i = 0; at + i < sig.size() && i < n; ++i) {
        const float t = float(i) / kSr;
        float s = 0.0f;
        if (t < 0.005f) {
            const uint32_t h = uint32_t(i) * 2654435761u + seed;
            s = 0.25f * (float(int((h ^ (h >> 13)) % 2000)) / 1000.0f - 1.0f)
              * (1.0f - t / 0.005f);
        }
        for (int h = 1; h <= 5; ++h) {
            const float decay = std::exp(-(1.2f + 0.25f * float(h)) * t);
            const uint32_t ph = seed * 2654435761u + uint32_t(h) * 40503u;
            const float phase = float((ph >> 8) % 6283u) / 1000.0f;
            s += (0.5f / float(h)) * decay
               * std::sin(2.0f * float(M_PI) * f * float(h) * t + phase);
        }
        sig[at + i] += amp * s;
    }
}

void add_noise_floor(std::vector<float>& sig, float amp, uint32_t seed) {
    uint32_t rng = seed;
    for (size_t i = 0; i < sig.size(); ++i) {
        rng = rng * 1664525u + 1013904223u;
        sig[i] += amp * (float(int(rng >> 8 & 0xFFFF)) / 32767.5f - 1.0f);
    }
}

/// The written score: A3 quarter, C4 quarter, G3 half (quarter = 500 ms).
std::vector<kan::score_verify::ScoreNote> make_score() {
    std::vector<kan::score_verify::ScoreNote> notes(3);
    notes[0].onset_ms = 0.0f;    notes[0].dur_ms = 500.0f;  notes[0].midi = 57;
    notes[1].onset_ms = 500.0f;  notes[1].dur_ms = 500.0f;  notes[1].midi = 60;
    notes[2].onset_ms = 1000.0f; notes[2].dur_ms = 1000.0f; notes[2].midi = 55;
    return notes;
}

float sim_of(const kan::score_verify::ScoreNote& n) {
    return n.sim_w > 0.0f ? n.sim_wsum / n.sim_w : 0.0f;
}

void dump(const std::vector<kan::score_verify::ScoreNote>& notes) {
    for (const auto& n : notes) {
        const float meas = (n.measured_end_ms > 0.0f) ? n.measured_end_ms - n.onset_ms : 0.0f;
        std::printf("    [row] onset %.0f midi %d | sim %.2f cents %+.0f amp %.4f "
                    "meas_dur %.0f t_peak %.0f\n",
                    double(n.onset_ms), n.midi, double(sim_of(n)),
                    n.sim_w > 0.0f ? double(n.cents_wsum / n.sim_w) : 0.0,
                    double(n.amp_peak), double(meas),
                    double(n.peak_ms - n.onset_ms));
    }
}

} // namespace

int main() {
    std::printf("=== score_error_test: mistakes must fail loudly ===\n");

    // 1. Control: correct playing -> everything verifies.
    {
        std::printf("control: correct playing\n");
        std::vector<float> sig(size_t(2.2f * kSr), 0.0f);
        add_note(sig, 0.0f, 500.0f, 57, 0.5f, 1u);
        add_note(sig, 500.0f, 500.0f, 60, 0.5f, 2u);
        add_note(sig, 1000.0f, 1000.0f, 55, 0.5f, 3u);
        add_noise_floor(sig, 0.005f, 99u);
        auto notes = make_score();
        std::vector<float> extras;
        kan::score_verify::measure_score(sig, kSr, notes, &extras);
        dump(notes);
        bool all_found = true;
        for (const auto& n : notes) {
            if (sim_of(n) < 0.80f || std::fabs(n.cents_wsum / n.sim_w) > 30.0f) {
                all_found = false;
            }
        }
        check(all_found, "all 3 written notes verify (sim >= 0.8, |cents| <= 30)");
        check(extras.empty(), "no extra strikes reported");
    }

    // 2. Wrong pitch: B3 where the score says C4.
    {
        std::printf("mistake: wrong pitch (B3 instead of C4)\n");
        std::vector<float> sig(size_t(2.2f * kSr), 0.0f);
        add_note(sig, 0.0f, 500.0f, 57, 0.5f, 1u);
        add_note(sig, 500.0f, 500.0f, 59, 0.5f, 2u);   // WRONG: B3
        add_note(sig, 1000.0f, 1000.0f, 55, 0.5f, 3u);
        add_noise_floor(sig, 0.005f, 99u);
        auto notes = make_score();
        kan::score_verify::measure_score(sig, kSr, notes);
        dump(notes);
        // The measured signature of a wrong pitch is the CENTS, not the sim:
        // the fit bends toward the sounding note (here -56 c) but cannot
        // reach it, while sim stays high. So the judge MUST gate on |cents|
        // — sim alone would let this pass. (And a +/-60 c band would half-
        // swallow it: this is why game judging should use a tighter band.)
        const float cents1 = notes[1].cents_center;
        check(std::fabs(cents1) > 50.0f,
              "the written C4 bends visibly off pitch (|cents| > 50)");
        check(notes[1].saturated,
              "the estimate saturates at the search cap and is flagged (ISS-1)");
        check(sim_of(notes[0]) >= 0.80f && sim_of(notes[2]) >= 0.80f,
              "the correctly played notes still verify");
    }

    // 3. Missing note: G3 never played.
    {
        std::printf("mistake: missing note (G3 skipped)\n");
        std::vector<float> sig(size_t(2.2f * kSr), 0.0f);
        add_note(sig, 0.0f, 500.0f, 57, 0.5f, 1u);
        add_note(sig, 500.0f, 500.0f, 60, 0.5f, 2u);
        add_noise_floor(sig, 0.005f, 99u);
        auto notes = make_score();
        kan::score_verify::measure_score(sig, kSr, notes);
        dump(notes);
        // The signature of a skipped note is the SIM (the window is not
        // explained), not the amplitude: the fit can still lease amplitude
        // to the missing pitch from neighbor leakage (amp 0.12 here) — but
        // sim collapses to ~0.06.
        check(sim_of(notes[2]) < 0.10f,
              "the skipped G3 collapses (sim ~ 0) — reported as not played");
    }

    // 4. Extra note: E4 played where the score has silence.
    {
        std::printf("mistake: extra note (E4 not in the score)\n");
        std::vector<float> sig(size_t(2.2f * kSr), 0.0f);
        add_note(sig, 0.0f, 500.0f, 57, 0.5f, 1u);
        add_note(sig, 500.0f, 500.0f, 60, 0.5f, 2u);
        add_note(sig, 750.0f, 250.0f, 64, 0.5f, 7u);   // EXTRA: E4
        add_note(sig, 1000.0f, 1000.0f, 55, 0.5f, 3u);
        add_noise_floor(sig, 0.005f, 99u);
        auto notes = make_score();
        std::vector<float> extras;
        kan::score_verify::measure_score(sig, kSr, notes, &extras);
        dump(notes);
        bool extra_seen = false;
        for (float t : extras) {
            if (std::fabs(t - 750.0f) < 150.0f) extra_seen = true;
        }
        check(extra_seen, "the extra note appears as an unmatched strike");
    }

    // 5. Late note: C4 arrives 200 ms after its written onset.
    {
        std::printf("mistake: late note (C4 at +200 ms)\n");
        std::vector<float> sig(size_t(2.4f * kSr), 0.0f);
        add_note(sig, 0.0f, 500.0f, 57, 0.5f, 1u);
        add_note(sig, 700.0f, 500.0f, 60, 0.5f, 2u);   // LATE
        add_note(sig, 1000.0f, 1000.0f, 55, 0.5f, 3u);
        add_noise_floor(sig, 0.005f, 99u);
        auto notes = make_score();
        kan::score_verify::measure_score(sig, kSr, notes);
        dump(notes);
        const float t_peak = notes[1].peak_ms - notes[1].onset_ms;
        check(t_peak > 100.0f,
              "the written C4's energy peaks late (t_peak onset +>100 ms)");
    }

    // 6. Too long: G3 held past its written end.
    {
        std::printf("mistake: wrong duration (G3 held too long)\n");
        std::vector<float> sig(size_t(2.6f * kSr), 0.0f);
        add_note(sig, 0.0f, 500.0f, 57, 0.5f, 1u);
        add_note(sig, 500.0f, 500.0f, 60, 0.5f, 2u);
        add_note(sig, 1000.0f, 1400.0f, 55, 0.5f, 3u); // TOO LONG (1000 ms written)
        add_noise_floor(sig, 0.005f, 99u);
        auto notes = make_score();
        kan::score_verify::measure_score(sig, kSr, notes);
        dump(notes);
        const float meas = notes[2].measured_end_ms - notes[2].onset_ms;
        // The over-run must exceed the documented release bias (~+200 ms)
        // to be distinguishable; here 1400 ms played vs 1000 written shows
        // +225 ms error beyond the written end.
        check(meas > notes[2].dur_ms + 200.0f,
              "the over-held G3 shows duration error beyond the release bias");
    }

    std::printf("%s\n", g_pass ? "ALL PASS" : "FAILURES");
    return g_pass ? 0 : 1;
}
