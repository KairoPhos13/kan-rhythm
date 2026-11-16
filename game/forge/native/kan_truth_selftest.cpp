// kan_truth_selftest: the truth bench on a synthetic piano-like piece
// (ctest, no dataset, repo rule 5). It checks that the case builders hold
// their contract (the expected number of right and wrong notes), that the
// oracle and the yes path score exactly what a perfect and an accept
// everything path must score (the controls that prove the bench measures
// what it claims), that the blind path runs through with counts that add
// up, that the bp path of work package 2 runs with counts that add up and
// that its sweep line with th 0 and rule plain accepts every right note,
// that the fair rule of work package 4b holds its floor (98 % of the
// right notes accepted, 60 % of the wrong semitone notes rejected) and
// obeys its clauses on hand-written measurements, that the detuned
// control's audio keeps the duration and the attack times, and that the
// event dump of the same audio twice is identical. It also round trips
// the posterior dump of work package 4c: one dump of the synthetic piece
// is written to a temporary directory, read back, and its frames, its
// first and last frame times and one posterior are compared.
//
// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
#include "truth_bench.hpp"
#include "truth_paths.hpp"
#include "truth_paths_bp.hpp"

#include <cmath>
#include <cstdio>
#include <filesystem>
#include <limits>
#include <random>
#include <string>
#include <vector>

using namespace truth_bench;

namespace {

constexpr float kSr = 48000.0f;
constexpr double kPi = 3.14159265358979323846;

// A synthetic piano-like piece of about 20 s: moments of 1 to 6 notes at
// least 350 ms apart (so the widest case shift stays inside every meh
// window), pitches 36 to 96 with no pitch reused within 800 ms, notes of
// 8 inharmonic partials at k*f0*sqrt(1+B*k*k) (B = 0.0004), a 3 ms attack
// and an exponential decay. Fixed seed.
void synth_piece(unsigned seed, std::vector<TruthNote>& truth, std::vector<float>& audio) {
    constexpr double B = 0.0004;
    std::mt19937 rng(seed);
    std::uniform_int_distribution<int> voices(1, 6);
    std::uniform_int_distribution<int> gap(350, 900);
    std::uniform_int_distribution<int> hold(300, 1500);
    std::uniform_int_distribution<int> pitch(36, 96);

    double last_used[128];
    for (double& u : last_used) u = -1e9;

    double t = 500.0;
    while (t < 19500.0) {
        const int n = voices(rng);
        std::vector<int> chord;
        for (int v = 0; v < n; ++v) {
            for (int tries = 0; tries < 200; ++tries) {
                const int m = pitch(rng);
                if (last_used[m] > t - 800.0) continue;
                bool used = false;
                for (int o : chord) used = used || o == m;
                if (used) continue;
                chord.push_back(m);
                last_used[m] = t;
                break;
            }
        }
        for (int m : chord) {
            const double dur = double(hold(rng));
            truth.push_back({t, dur, m});
        }
        t += double(gap(rng));
    }

    const size_t len = size_t(20.0 * kSr);
    audio.assign(len, 0.0f);
    for (const auto& n : truth) {
        const double f0 = 440.0 * std::pow(2.0, (double(n.midi) - 69.0) / 12.0);
        const size_t at = size_t(n.onset_ms * kSr / 1000.0);
        const size_t count = size_t((n.dur_ms + 200.0) * kSr / 1000.0);
        for (size_t i = 0; i < count && at + i < len; ++i) {
            const double u = double(i) / kSr;
            const double env = (u < 0.003 ? u / 0.003 : 1.0) * std::exp(-u / 0.5);
            double v = 0.0;
            for (int k = 1; k <= 8; ++k) {
                const double fk = double(k) * f0 * std::sqrt(1.0 + B * double(k) * double(k));
                v += std::sin(2.0 * kPi * fk * u) / double(k);
            }
            audio[at + i] += float(0.1 * env * v);
        }
    }
    float peak = 0.0f;
    for (float s : audio) peak = std::max(peak, std::fabs(s));
    if (peak > 0.0f)
        for (float& s : audio) s *= 0.5f / peak;
}

int failures = 0;

void check(bool ok, const char* what) {
    if (!ok) {
        std::fprintf(stderr, "SELFTEST FAIL: %s\n", what);
        ++failures;
    }
}

void check_eq(size_t got, size_t want, const char* what) {
    if (got != want) {
        std::fprintf(stderr, "SELFTEST FAIL: %s: got %zu, want %zu\n", what, got, want);
        ++failures;
    }
}

} // namespace

int main() {
    std::vector<TruthNote> truth;
    std::vector<float> audio;
    synth_piece(7, truth, audio);
    const size_t n = truth.size();
    const size_t k30 = size_t(std::llround(0.30 * double(n)));
    std::printf("selftest: synthetic piece %.1f s, %zu truth notes (%zu = 30%%)\n",
                double(audio.size()) / kSr, n, k30);

    // 1. The case builders: the expected number of right and wrong notes,
    //    and every wrong note genuinely wrong (the truth has no attack of
    //    its pitch inside its meh window around its score time).
    for (size_t ci = 0; ci < kCaseCount; ++ci) {
        const Case c = kAllCases[ci];
        const BuiltCase b = build_case(c, truth, 7);
        size_t right = 0, wrong = 0;
        const ScoreWindows w = compute_windows(b.notes);
        for (size_t i = 0; i < b.notes.size(); ++i) {
            (b.notes[i].right ? right : wrong)++;
            if (!b.notes[i].right) {
                check(!truth_has_attack(truth, b.notes[i].midi, b.notes[i].onset_ms, w.meh_ms[i]),
                      "a wrong note must have no truth attack of its pitch in its meh window");
            }
        }
        const char* name = case_name(c);
        char what[128];
        std::snprintf(what, sizeof(what), "%s: kept_unchanged must be 0", name);
        check(b.kept_unchanged == 0, what);
        if (c == Case::Right || c == Case::Late120 || c == Case::Late250 || c == Case::Early120 ||
            c == Case::Jitter60 || case_detune_cents(c) != 0.0) {
            std::snprintf(what, sizeof(what), "%s: right notes", name);
            check_eq(right, n, what);
            std::snprintf(what, sizeof(what), "%s: wrong notes", name);
            check_eq(wrong, 0, what);
        } else if (c == Case::Added) {
            std::snprintf(what, sizeof(what), "%s: right notes", name);
            check_eq(right, n, what);
            std::snprintf(what, sizeof(what), "%s: wrong notes", name);
            check_eq(wrong, k30, what);
            std::snprintf(what, sizeof(what), "%s: total notes", name);
            check_eq(b.notes.size(), n + k30, what);
        } else if (c == Case::LeftOut) {
            std::snprintf(what, sizeof(what), "%s: right notes", name);
            check_eq(right, n - k30, what);
            std::snprintf(what, sizeof(what), "%s: wrong notes", name);
            check_eq(wrong, 0, what);
        } else {
            std::snprintf(what, sizeof(what), "%s: right notes", name);
            check_eq(right, n - k30, what);
            std::snprintf(what, sizeof(what), "%s: wrong notes", name);
            check_eq(wrong, k30, what);
        }
    }

    // The controls, every case: the oracle accepts every right note at its
    // truth onset (time error 0) and rejects every wrong note; the yes
    // path accepts everything and shows the shift exactly on the shifted
    // cases.
    OraclePath oracle(truth);
    YesPath yes;
    BlindPath blind;
    for (size_t ci = 0; ci < kCaseCount; ++ci) {
        const Case c = kAllCases[ci];
        const BuiltCase b = build_case(c, truth, 7);
        const char* name = case_name(c);
        char what[128];

        std::vector<NoteResult> res;
        CpuStats cpu;
        oracle.run(audio, kSr, b.notes, res, cpu);
        for (size_t i = 0; i < b.notes.size(); ++i) {
            const bool want_accepted = b.notes[i].right;
            if (want_accepted) {
                std::snprintf(what, sizeof(what), "%s: oracle must accept every right note", name);
                check(res[i].outcome == Outcome::Accepted, what);
                const double err = res[i].onset_ms - truth[size_t(b.notes[i].truth_index)].onset_ms;
                std::snprintf(what, sizeof(what), "%s: oracle time error must be 0", name);
                check(std::fabs(err) < 1e-9, what);
            } else {
                std::snprintf(what, sizeof(what), "%s: oracle must reject every wrong note", name);
                check(res[i].outcome != Outcome::Accepted, what);
            }
        }

        yes.run(audio, kSr, b.notes, res, cpu);
        const double shift = c == Case::Late120 ? 120.0 : (c == Case::Late250 ? 250.0
                                                    : c == Case::Early120 ? -120.0 : 0.0);
        for (size_t i = 0; i < b.notes.size(); ++i) {
            std::snprintf(what, sizeof(what), "%s: yes must accept every note", name);
            check(res[i].outcome == Outcome::Accepted, what);
            if (shift != 0.0) {
                const double truth_onset = b.notes[i].truth_index >= 0
                                               ? truth[size_t(b.notes[i].truth_index)].onset_ms
                                               : b.notes[i].onset_ms - shift;
                const double err = res[i].onset_ms - truth_onset;
                std::snprintf(what, sizeof(what), "%s: yes time error must be exactly the shift",
                              name);
                check(std::fabs(err - shift) < 1e-9, what);
            }
        }

        // blind runs through and its counts add up (no quality threshold).
        blind.run(audio, kSr, b.notes, res, cpu);
        const CaseStats st = score_case(truth, b.notes, res);
        size_t right_sum = 0, wrong_sum = 0, outcomes = 0, rejected = 0;
        for (size_t bk = 0; bk < kBucketCount; ++bk) {
            right_sum += st.buckets[bk].right_n;
            wrong_sum += st.buckets[bk].wrong_n;
            outcomes += st.buckets[bk].accepted + st.buckets[bk].not_measured +
                        st.buckets[bk].misreported + st.buckets[bk].absent;
            rejected += st.buckets[bk].rejected;
        }
        size_t want_right = 0, want_wrong = 0, accepted_wrong = 0;
        for (size_t i = 0; i < b.notes.size(); ++i) {
            (b.notes[i].right ? want_right : want_wrong)++;
            if (!b.notes[i].right && res[i].outcome == Outcome::Accepted) ++accepted_wrong;
        }
        std::snprintf(what, sizeof(what), "%s: blind right counts must add up", name);
        check_eq(right_sum, want_right, what);
        std::snprintf(what, sizeof(what), "%s: blind wrong counts must add up", name);
        check_eq(wrong_sum, want_wrong, what);
        std::snprintf(what, sizeof(what), "%s: blind outcome counts must add up", name);
        check_eq(outcomes, want_right, what);
        std::snprintf(what, sizeof(what), "%s: blind rejection counts must add up", name);
        check_eq(rejected + accepted_wrong, want_wrong, what);
    }

    // 5. The learned evidence path of work package 2: bp runs on the
    //    synthetic piece (one offline pass, cached over the cases), its
    //    counts add up like the blind path's, and the sweep line with
    //    th 0 and rule plain accepts 100 % of the right notes.
    {
        bp::BpPath bpath; // rule fair, th 0.3: only the counts matter here
        for (size_t ci = 0; ci < kCaseCount; ++ci) {
            const Case c = kAllCases[ci];
            const BuiltCase b = build_case(c, truth, 7);
            const char* name = case_name(c);
            char what[128];

            std::vector<NoteResult> res;
            CpuStats cpu;
            bpath.run(audio, kSr, b.notes, res, cpu);
            const CaseStats st = score_case(truth, b.notes, res);
            size_t right_sum = 0, wrong_sum = 0, outcomes = 0, rejected = 0;
            for (size_t bk = 0; bk < kBucketCount; ++bk) {
                right_sum += st.buckets[bk].right_n;
                wrong_sum += st.buckets[bk].wrong_n;
                outcomes += st.buckets[bk].accepted + st.buckets[bk].not_measured +
                            st.buckets[bk].misreported + st.buckets[bk].absent;
                rejected += st.buckets[bk].rejected;
            }
            size_t want_right = 0, want_wrong = 0, accepted_wrong = 0;
            for (size_t i = 0; i < b.notes.size(); ++i) {
                (b.notes[i].right ? want_right : want_wrong)++;
                if (!b.notes[i].right && res[i].outcome == Outcome::Accepted) ++accepted_wrong;
            }
            std::snprintf(what, sizeof(what), "%s: bp right counts must add up", name);
            check_eq(right_sum, want_right, what);
            std::snprintf(what, sizeof(what), "%s: bp wrong counts must add up", name);
            check_eq(wrong_sum, want_wrong, what);
            std::snprintf(what, sizeof(what), "%s: bp outcome counts must add up", name);
            check_eq(outcomes, want_right, what);
            std::snprintf(what, sizeof(what), "%s: bp rejection counts must add up", name);
            check_eq(rejected + accepted_wrong, want_wrong, what);
        }

        // The sweep line with th 0 and rule plain accepts every right note
        // of the right case (100 %).
        const BuiltCase b = build_case(Case::Right, truth, 7);
        std::vector<NoteResult> res;
        CpuStats cpu;
        bpath.run(audio, kSr, b.notes, res, cpu);
        const std::vector<bp::NoteMeasure>& measures = bpath.measures();
        size_t right = 0, accepted = 0;
        for (size_t i = 0; i < b.notes.size(); ++i) {
            const NoteResult r = bp::decide(measures[i], bp::Rule::Plain, 0.0, b.notes[i].midi);
            if (!b.notes[i].right) continue;
            ++right;
            if (r.outcome == Outcome::Accepted) ++accepted;
        }
        check(right > 0, "the synthetic piece must have right notes");
        check_eq(accepted, right, "bp sweep th 0 plain must accept every right note");
    }

    // 6. The fair rule of work package 4b. First the rule function on
    //    hand-written measurements (its clauses and their boundaries),
    //    then its floor on the synthetic piece through the bp path: at
    //    least 98 % of the right notes accepted and at least 60 % of the
    //    wrong semitone notes rejected.
    {
        const bp::FairParams fp; // the defaults
        auto outcome_of = [&](const bp::NoteMeasure& m) {
            return bp::decide(m, bp::Rule::Fair, 0.3, 60, fp).outcome;
        };
        bp::NoteMeasure m;
        m.attack_ms = 1000.0;
        // n_after above the bar accepts at once, whatever the rivals say.
        m.n_after = 0.14f;
        m.s = 0.0f;
        m.r = 1.0f;
        m.rival_midi = 61.0f;
        check(outcome_of(m) == Outcome::Accepted, "fair: n_after above a accepts");
        // Under the note bar the onset support carries it while no rival
        // leads by c. (0.13f is just under 0.13, so the note bar alone
        // does not carry these.)
        m.n_after = 0.12f;
        m.s = 0.25f;
        m.r = 0.54f;
        check(outcome_of(m) == Outcome::Accepted, "fair: s at b with no rival lead accepts");
        // A rival leading by c vetoes.
        m.r = 0.55f;
        check(outcome_of(m) != Outcome::Accepted, "fair: a rival lead of c vetoes");
        // Both bars missed: not accepted.
        m.s = 0.24f;
        m.r = 0.0f;
        check(outcome_of(m) != Outcome::Accepted, "fair: both bars missed rejects");
        // No attack measured: no n_after and no support.
        const bp::NoteMeasure none;
        check(outcome_of(none) != Outcome::Accepted, "fair: no attack is not accepted");
        // plain and rival keep their behaviour: rival vetoes when the
        // rival is stronger, plain ignores the rival.
        m.s = 0.5f;
        m.r = 0.6f;
        check(bp::decide(m, bp::Rule::Rival, 0.3, 60).outcome != Outcome::Accepted,
              "rival: a rival stronger than s still vetoes");
        check(bp::decide(m, bp::Rule::Plain, 0.3, 60).outcome == Outcome::Accepted,
              "plain: s at th accepts");

        bp::BpPath fair_path; // rule fair, the default settings
        {
            const BuiltCase b = build_case(Case::Right, truth, 7);
            std::vector<NoteResult> res;
            CpuStats cpu;
            fair_path.run(audio, kSr, b.notes, res, cpu);
            size_t right = 0, accepted = 0;
            for (size_t i = 0; i < b.notes.size(); ++i) {
                if (!b.notes[i].right) continue;
                ++right;
                if (res[i].outcome == Outcome::Accepted) ++accepted;
            }
            check(right > 0, "the synthetic piece must have right notes");
            const double rate = right > 0 ? 100.0 * double(accepted) / double(right) : 0.0;
            std::printf("selftest: fair accepts %.1f %% of the right notes (%zu of %zu)\n", rate,
                        accepted, right);
            check(rate >= 98.0, "fair must accept at least 98 % of the right notes");
        }
        {
            const BuiltCase b = build_case(Case::Semitone, truth, 7);
            std::vector<NoteResult> res;
            CpuStats cpu;
            fair_path.run(audio, kSr, b.notes, res, cpu);
            size_t wrong = 0, rejected = 0;
            for (size_t i = 0; i < b.notes.size(); ++i) {
                if (b.notes[i].right) continue;
                ++wrong;
                if (res[i].outcome != Outcome::Accepted) ++rejected;
            }
            check(wrong > 0, "the semitone case must have wrong notes");
            const double rate = wrong > 0 ? 100.0 * double(rejected) / double(wrong) : 0.0;
            std::printf("selftest: fair rejects %.1f %% of the wrong semitone notes (%zu of %zu)\n",
                        rate, rejected, wrong);
            check(rate >= 60.0, "fair must reject at least 60 % of the semitone notes");
        }
    }

    // 7. The detuned control's audio (work package 4b): the shifter
    //    keeps the duration and the attacks stay at their times (its
    //    group delay is cut from the front and padded at the end).
    {
        std::vector<float> click(size_t(kSr), 0.0f);
        click[size_t(kSr / 2)] = 1.0f;
        const std::vector<float> shifted = detuned_audio(click, kSr, 20.0);
        check(shifted.size() == click.size(), "detuned audio must keep the duration");
        size_t peak = 0;
        for (size_t i = 0; i < shifted.size(); ++i)
            if (std::fabs(shifted[i]) > std::fabs(shifted[peak])) peak = i;
        check(std::fabs(double(peak) - double(kSr / 2)) < 0.030 * kSr,
              "detuned audio must keep the attack time (within 30 ms)");
    }

    // 8. The event dump of the same audio twice is identical.
    const std::string d1 = dump_live_events(audio, kSr, "keyboard");
    const std::string d2 = dump_live_events(audio, kSr, "keyboard");
    check(!d1.empty(), "the event dump of the synthetic piece must not be empty");
    check(d1 == d2, "the event dump of the same audio twice must be identical");

    // 9. The posterior dump of work package 4c: one dump of the synthetic
    //    piece is written to a temporary directory and read back; the
    //    frames, the first and the last frame time and one posterior must
    //    survive the round trip. The analysis columns are filled here the
    //    way the bench fills them (the path never reads them).
    {
        const BuiltCase b = build_case(Case::Right, truth, 7);
        bp::BpPath dpath;
        std::vector<NoteResult> res;
        CpuStats cpu;
        dpath.run(audio, kSr, b.notes, res, cpu);
        std::vector<bp::DumpScoreRow> rows = bp::dump_score_rows(b.notes);
        for (size_t i = 0; i < b.notes.size(); ++i) {
            const ScoreNote& sn = b.notes[i];
            const double truth_onset = sn.truth_index >= 0
                                           ? truth[size_t(sn.truth_index)].onset_ms
                                           : std::numeric_limits<double>::quiet_NaN();
            const double at = sn.truth_index >= 0 ? truth_onset : sn.onset_ms;
            int sounding = 0;
            for (const auto& g : truth)
                if (g.onset_ms <= at && at < g.onset_ms + g.dur_ms) ++sounding;
            rows[i].right = sn.right ? 1 : 0;
            rows[i].sounding = sounding;
            rows[i].truth_onset_ms = truth_onset;
        }
        const std::string dir =
            (std::filesystem::temp_directory_path() / "kan_truth_selftest_dump").string();
        std::filesystem::create_directories(dir);
        check(bp::write_posterior_dump(dir, 3, "right", dpath.posteriors(), rows),
              "the posterior dump must write");
        bp::PosteriorDump got;
        check(bp::read_posterior_dump(dir + "/03_right.post", got),
              "the posterior dump must read back");
        const bp::PosteriorView& want = dpath.posteriors();
        check_eq(size_t(got.frames), size_t(want.frames), "the dump's frame count");
        if (want.frames > 0) {
            check(got.time_ms.size() == size_t(want.frames) &&
                      got.time_ms.front() == want.time_ms.front(),
                  "the dump's first frame time");
            check(got.time_ms.size() == size_t(want.frames) && got.time_ms.back() == want.time_ms.back(),
                  "the dump's last frame time");
            // One posterior: the onset posterior of the middle frame at
            // midi 60 (the bin of a midi note is midi - 21).
            const size_t cell = size_t(want.frames / 2) * bp::kNotes + size_t(60 - bp::kMidiOffset);
            check(got.onset.size() == size_t(want.frames) * bp::kNotes &&
                      got.onset[cell] == want.onset_at(want.frames / 2, 60),
                  "the dump's onset posterior");
        }
        std::filesystem::remove_all(dir);
    }

    if (failures == 0) std::printf("kan_truth_selftest OK\n");
    return failures == 0 ? 0 : 1;
}
