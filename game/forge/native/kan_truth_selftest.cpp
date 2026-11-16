// kan_truth_selftest: the truth bench on a synthetic piano-like piece
// (ctest, no dataset, repo rule 5). It checks that the case builders hold
// their contract (the expected number of right and wrong notes), that the
// oracle and the yes path score exactly what a perfect and an accept
// everything path must score (the controls that prove the bench measures
// what it claims), that the blind path runs through with counts that add
// up, and that the event dump of the same audio twice is identical.
//
// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
#include "truth_bench.hpp"
#include "truth_paths.hpp"

#include <cmath>
#include <cstdio>
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
            c == Case::Jitter60) {
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

    // 5. The event dump of the same audio twice is identical.
    const std::string d1 = dump_live_events(audio, kSr, "keyboard");
    const std::string d2 = dump_live_events(audio, kSr, "keyboard");
    check(!d1.empty(), "the event dump of the synthetic piece must not be empty");
    check(d1 == d2, "the event dump of the same audio twice must be identical");

    if (failures == 0) std::printf("kan_truth_selftest OK\n");
    return failures == 0 ? 0 : 1;
}
