// Counterexample suite: when the player makes mistakes, the measurement
// must FAIL LOUDLY (never smooth the error away). Score-anchored
// verification (score_verify.hpp) is tested against players that:
//   1. play correctly          (control: everything must verify)
//   2. play a wrong pitch      (B3 where the score says C4)
//   3. skip a note             (G3 never played)
//   4. add an extra note       (E4 where the score says silence)
//   5. play late               (C4 arrives 200 ms after its written onset)
//   6. hold too long           (G3 held past its written end)
// Every mistake must leave a measurable signature; that is what makes the
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

    // Band interpolation (ISS-1 follow-up): the search/trust band is a
    // smooth curve with register (user decision), not a hard breakpoint.
    {
        std::printf("band: pitch_search_cents_at() interpolates smoothly\n");
        const kan::InstrumentProfile kp = kan::InstrumentProfile::keyboard();
        check(kp.pitch_search_cents_at(20) >= 119.0f,
              "deep bass gets the wide band (120 c)");
        check(kp.pitch_search_cents_at(80) <= 61.0f,
              "upper registers get the base band (60 c)");
        const float mid = kp.pitch_search_cents_at(45);
        check(mid > 60.0f && mid < 120.0f,
              "transition register interpolates strictly between the anchors");
    }

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
        // reach it, while sim stays high. So the judge MUST gate on |cents|;
        // sim alone would let this pass. (And a +/-60 c band would half-
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
        // to the missing pitch from neighbor leakage (amp 0.12 here), but
        // sim collapses to ~0.06.
        check(sim_of(notes[2]) < 0.10f,
              "the skipped G3 collapses (sim ~ 0), reported as not played");
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

    // 7. Dense passage (ISS-3 regression): a 28-note glissando mirrors the
    //    real density peak measured on Chopin Op. 25 (27 alive at once).
    //    The heavy tier must engage: every note measured and verified by a
    //    joint fit -- no fixed-cap truncation.
    {
        std::printf("density: 28-note glissando (real Chopin peak); heavy cavalry\n");
        std::vector<float> sig(size_t(2.6f * kSr), 0.0f);
        std::vector<kan::score_verify::ScoreNote> notes(28);
        for (int i = 0; i < 28; ++i) {
            const float t = 35.0f * float(i);
            const int midi = 40 + 2 * i;
            add_note(sig, t, 1200.0f, float(midi), 0.25f, uint32_t(100 + i));
            notes[size_t(i)].onset_ms = t;
            notes[size_t(i)].dur_ms = 1200.0f;
            notes[size_t(i)].midi = midi;
        }
        add_noise_floor(sig, 0.005f, 99u);
        kan::score_verify::measure_score(sig, kSr, notes);
        int measured = 0, found = 0;
        for (const auto& n : notes) {
            if (n.sim_w > 0.0f) ++measured;
            if (sim_of(n) >= 0.60f) ++found;
        }
        dump(notes);
        check(measured == 28, "all 28 dense-passage notes are measured (no cap truncation)");
        // Fit quality holds through midi ~84 (sim 0.93-1.00); the top of an
        // extreme 28-note cluster degrades (conditioning: ~336 atoms vs 2048
        // samples) and is honestly reported as not-verified, never as fact.
        check(found >= 24, "at least 24/28 verify (top-octave degradation documented)");
    }

    // 8. Beyond design (documented limit): 40 simultaneous notes exceed any
    //    real instrument density. Every note must still be MEASURED (facts
    //    over guesses), even if some cannot fully verify.
    {
        std::printf("density: 40-note extreme (>32 alive); documented limit\n");
        std::vector<float> sig(size_t(3.2f * kSr), 0.0f);
        std::vector<kan::score_verify::ScoreNote> notes(40);
        for (int i = 0; i < 40; ++i) {
            const float t = 25.0f * float(i);
            const int midi = 36 + 2 * i;
            add_note(sig, t, 1200.0f, float(midi), 0.25f, uint32_t(100 + i));
            notes[size_t(i)].onset_ms = t;
            notes[size_t(i)].dur_ms = 1200.0f;
            notes[size_t(i)].midi = midi;
        }
        add_noise_floor(sig, 0.005f, 99u);
        kan::score_verify::measure_score(sig, kSr, notes);
        int measured = 0;
        for (const auto& n : notes) {
            if (n.sim_w > 0.0f) ++measured;
        }
        check(measured == 40,
              "all 40 extreme-density notes are measured (facts, never truncation)");
    }

    // 9. Mash / cheat-superset (the "press random keys that include the
    //    right ones" attack): the written chord verifies, but the extra
    //    sounding keys must leave a measurable purity signature that a
    //    judge can penalize. Measured here to keep the game-side rule
    //    honest (game-judging-guide G6).
    {
        std::printf("cheat: correct chord + 3 random extra keys; mash attack\n");
        // The written chord: A3 + C3 + F2 (F major, 800 ms at t=0).
        auto make_chord = []() {
            std::vector<kan::score_verify::ScoreNote> notes(3);
            notes[0].onset_ms = 0.0f;  notes[0].dur_ms = 800.0f; notes[0].midi = 57;
            notes[1].onset_ms = 0.0f;  notes[1].dur_ms = 800.0f; notes[1].midi = 48;
            notes[2].onset_ms = 0.0f;  notes[2].dur_ms = 800.0f; notes[2].midi = 41;
            return notes;
        };
        // Control: the chord played correctly.
        std::vector<float> clean(size_t(1.4f * kSr), 0.0f);
        add_note(clean, 0.0f, 800.0f, 57, 0.5f, 1u);
        add_note(clean, 0.0f, 800.0f, 48, 0.5f, 2u);
        add_note(clean, 0.0f, 800.0f, 41, 0.5f, 3u);
        add_noise_floor(clean, 0.005f, 99u);
        auto cn = make_chord();
        kan::score_verify::measure_score(clean, kSr, cn);

        // Mash: same chord + D#4, G#2, B3 struck at the same time.
        std::vector<float> mash(size_t(1.4f * kSr), 0.0f);
        add_note(mash, 0.0f, 800.0f, 57, 0.5f, 1u);
        add_note(mash, 0.0f, 800.0f, 48, 0.5f, 2u);
        add_note(mash, 0.0f, 800.0f, 41, 0.5f, 3u);
        add_note(mash, 0.0f, 800.0f, 63, 0.5f, 4u);   // EXTRA: D#4
        add_note(mash, 0.0f, 800.0f, 44, 0.5f, 5u);   // EXTRA: G#2
        add_note(mash, 0.0f, 800.0f, 59, 0.5f, 6u);   // EXTRA: B3
        add_noise_floor(mash, 0.005f, 99u);
        auto mn = make_chord();
        std::vector<float> extras;
        kan::score_verify::measure_score(mash, kSr, mn, &extras);

        float sim_clean = 0.0f, sim_mash = 0.0f, amp_clean = 0.0f, amp_mash = 0.0f;
        for (const auto& n : cn) { sim_clean += sim_of(n); amp_clean += n.amp_peak; }
        for (const auto& n : mn) { sim_mash += sim_of(n); amp_mash += n.amp_peak; }
        std::printf("    sim: clean %.3f vs mash %.3f | written-set amp: clean %.2f vs mash %.2f\n",
                    double(sim_clean / 3.0), double(sim_mash / 3.0),
                    double(amp_clean), double(amp_mash));
        dump(mn);
        bool notes_verify = true;
        for (const auto& n : mn) {
            if (sim_of(n) < 0.60f) notes_verify = false;
        }
        check(notes_verify,
              "the written chord still verifies (note gate alone passes the cheat)");
        // sim alone does NOT drop (energy gets absorbed: amps inflate ~2-4x,
        // cents bend) -- the honest finding that motivates G6.
        check(amp_mash > amp_clean * 1.5f,
              "absorption signature: the written set's level inflates >1.5x");

        // The detection that DOES work: set-extension search -- the extra
        // sounding pitches are found as non-expected pitches at the strike.
        std::vector<kan::score_verify::ExtraPitch> xp_clean, xp_mash;
        std::vector<kan::score_verify::StrikePurity> pu_clean, pu_mash;
        kan::score_verify::search_extra_pitches(clean, kSr, cn, &xp_clean, &pu_clean);
        kan::score_verify::search_extra_pitches(mash, kSr, mn, &xp_mash, &pu_mash);
        std::printf("    extra-pitch search: clean %zu, mash %zu\n",
                    xp_clean.size(), xp_mash.size());
        const float un_clean = pu_clean.empty() ? 0.0f : pu_clean[0].unexplained;
        const float un_mash = pu_mash.empty() ? 0.0f : pu_mash[0].unexplained;
        std::printf("    strike purity (unexplained energy): clean %.3f vs mash %.3f\n",
                    double(un_clean), double(un_mash));
        // The robust primary fact of the purity mod (G6): unexplained energy.
        check(un_mash >= 0.03f && un_mash > 3.0f * un_clean,
              "unexplained-energy purity flags the mash (>=3x clean, >=3%)");
        for (const auto& x : xp_mash) {
            std::printf("    [extra] midi %d gain %.3f amp %.3f\n",
                        x.midi, double(x.sim_gain), double(x.amp));
        }
        // Candidate list: total recall but noisy neighbors (honestly noted);
        // the list is informational for replay UI, the purity number judges.
        int caught = 0;
        for (int want : {63, 44, 59}) {
            for (const auto& x : xp_mash) {
                if (std::abs(x.midi - want) <= 1) { ++caught; break; }
            }
        }
        check(xp_clean.empty(),
              "clean chord: no phantom extra pitches (no false accusation)");
        check(caught == 3,
              "all 3 mashed keys appear among the extra-pitch candidates");
    }

    // 10. Envelope-type matrix (ISS-5): the end_faded flag must classify
    //     the END TYPE correctly on every envelope shape. Contract:
    //     release (cliff) -> observed end; decay crossing / pedal ring ->
    //     inferred end (faded). Only a flag validated here is a fact.
    {
        std::printf("envelope matrix (ISS-5): end_faded must classify end types\n");
        auto add_shape_note = [](std::vector<float>& sig, float t0_ms, float total_ms,
                                 int midi, float amp, float plateau_ms,
                                 float release_ms, float tau_s) {
            const float f = 440.0f * std::pow(2.0f, (float(midi) - 69.0f) / 12.0f);
            const size_t at = size_t(t0_ms / 1000.0f * kSr);
            const size_t n = size_t(total_ms / 1000.0f * kSr);
            for (size_t i = 0; at + i < sig.size() && i < n; ++i) {
                const float t = float(i) / kSr;
                const float t_ms = t * 1000.0f;
                float env = std::exp(-t / std::max(tau_s, 1e-3f));
                if (release_ms > 0.0f && t_ms > plateau_ms) {
                    const float g = std::min(1.0f, (t_ms - plateau_ms) / release_ms);
                    env *= (1.0f - g);
                }
                float s = 0.0f;
                for (int h = 1; h <= 5; ++h) {
                    s += (0.5f / float(h))
                       * std::sin(2.0f * float(M_PI) * f * float(h) * t + 0.3f * float(h));
                }
                sig[at + i] += amp * env * s;
            }
        };
        // Notes start at 1000 ms: the measurement grid begins 200 ms after
        // the first onset, so a fixture note must live in covered territory
        // (what the flag's criterion is validated on).
        // A quiet lead-in note at 500 ms establishes the measurement grid
        // (it starts 200 ms after the first onset); the shaped note at
        // 2500 ms is then fully covered -- the flag's criterion is
        // validated where it actually operates. The lead is part of the
        // score, so the fit models it honestly.
        auto measure_one = [&](float total_ms, int midi, float plateau_ms,
                               float release_ms, float tau_s, float written_ms) {
            std::vector<float> sig(size_t((3000.0f + total_ms + 400.0f) / 1000.0f * kSr), 0.0f);
            add_shape_note(sig, 500.0f, 3000.0f, 33, 0.25f, 2500.0f, 50.0f, 50.0f);
            add_shape_note(sig, 2500.0f, total_ms, midi, 0.5f, plateau_ms, release_ms, tau_s);
            add_noise_floor(sig, 0.005f, 99u);
            std::vector<kan::score_verify::ScoreNote> notes(2);
            notes[0].onset_ms = 500.0f;
            notes[0].dur_ms = 2500.0f;
            notes[0].midi = 33;
            notes[1].onset_ms = 2500.0f;
            notes[1].dur_ms = written_ms;
            notes[1].midi = midi;
            kan::score_verify::measure_score(sig, kSr, notes, nullptr, 30.0f);
            return notes[1];
        };
        // A: clean release (plateau then damper cliff) -> NOT faded.
        {
            const auto n = measure_one(600.0f, 57, 500.0f, 30.0f, 50.0f, 500.0f);
            check(!n.end_faded, "A: clean release = observed end (not faded)");
        }
        // B: monotone slow decay crossing the threshold while held -> faded.
        {
            const auto n = measure_one(4500.0f, 45, 4500.0f, 0.0f, 1.2f, 4000.0f);
            check(n.end_faded,
                  "B: decay crossing = inferred end (faded) -- the ISS-2 shape");
        }
        // C: pedal ring (key release, slow tail) -> faded (no cliff exists).
        {
            const auto n = measure_one(2900.0f, 57, 400.0f, 0.0f, 0.8f, 400.0f);
            check(n.end_faded,
                  "C: pedal ring = inferred end (faded) -- release not audible");
        }
        // D: short note with fast release -> NOT faded (the 43% bug shape).
        {
            const auto n = measure_one(90.0f, 72, 60.0f, 20.0f, 50.0f, 60.0f);
            check(!n.end_faded,
                  "D: short note with release = observed end (not faded)");
        }
    }

    std::printf("%s\n", g_pass ? "ALL PASS" : "FAILURES");
    return g_pass ? 0 : 1;
}
