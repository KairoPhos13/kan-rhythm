// truth_bench: the engine of the truth bench (docs/architecture/
// polyphonic-live-measurement.md section 2, issue 41). Header only, no
// I/O, so the ctest selftest can drive it with no dataset. It builds the
// cases from a truth (the MIDI a recording was played from), holds the
// judge's windows, and scores the outcome of every score note by how many
// notes sound at its attack.
//
// The judge's windows are the game's own, ported as they are from
// game/core/Play/LiveJudge.cs (PaceOf, WindowMs and the constants).
//
// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
#ifndef KAN_TRUTH_BENCH_HPP
#define KAN_TRUTH_BENCH_HPP

#include <algorithm>
#include <cmath>
#include <cstddef>
#include <limits>
#include <random>
#include <utility>
#include <vector>

namespace truth_bench {

// ---- judge windows (game/core/Play/LiveJudge.cs lines 55 to 130) ------------

// The windows are a share of the note's pace (the time to its nearest
// neighbour at another moment), either side of it, held within bounds.
// Ported one to one from LiveJudge.cs (PERFECT_SHARE, GOOD_SHARE,
// MEH_SHARE, PERFECT_MIN_MS, PERFECT_MAX_MS, GOOD_MIN_MS, GOOD_MAX_MS,
// MEH_MIN_MS, MEH_MAX_MS, UNKNOWN_PACE_MS, CHORD_MS). Keep in step with
// the C# judge.
constexpr double kPerfectShare = 0.20;
constexpr double kGoodShare = 0.45;
constexpr double kMehShare = 0.80;
constexpr double kPerfectMinMs = 50.0, kPerfectMaxMs = 120.0;
constexpr double kGoodMinMs = 135.0, kGoodMaxMs = 450.0;
constexpr double kMehMinMs = 240.0, kMehMaxMs = 800.0;
constexpr double kUnknownPaceMs = 500.0;
constexpr double kChordMs = 30.0;

/// The piano range the cases keep their pitches inside.
constexpr int kMinMidi = 21, kMaxMidi = 108;

enum class Window { Perfect, Good, Meh };

inline double clamp(double v, double lo, double hi) {
    return v < lo ? lo : (v > hi ? hi : v);
}

/// A note's pace: the time to the nearest note before or after it that
/// starts at another moment (kChordMs), or 0 when it has none. Ported
/// from LiveJudge.PaceOf. `starts` in order.
inline double pace_of(const std::vector<double>& starts, int index) {
    const double at = starts[size_t(index)];
    double pace = std::numeric_limits<double>::infinity();
    for (int i = index - 1; i >= 0; --i) {
        if (at - starts[size_t(i)] < kChordMs) continue;
        pace = at - starts[size_t(i)];
        break;
    }
    for (size_t i = size_t(index) + 1; i < starts.size(); ++i) {
        if (starts[i] - at < kChordMs) continue;
        pace = std::min(pace, starts[i] - at);
        break;
    }
    return std::isinf(pace) ? 0.0 : pace;
}

/// A grade's window (half width, ms) at a note's pace (0 = unknown).
/// Ported from LiveJudge.WindowMs.
inline double window_ms(Window w, double pace_ms) {
    const double pace = pace_ms > 0 ? pace_ms : kUnknownPaceMs;
    switch (w) {
    case Window::Perfect: return clamp(pace * kPerfectShare, kPerfectMinMs, kPerfectMaxMs);
    case Window::Good: return clamp(pace * kGoodShare, kGoodMinMs, kGoodMaxMs);
    default: return clamp(pace * kMehShare, kMehMinMs, kMehMaxMs);
    }
}

// ---- truth, score and cases -------------------------------------------------

/// One note of the truth (the MIDI of the recording).
struct TruthNote {
    double onset_ms, dur_ms;
    int midi;
};

/// One note of the score a path is judged on. `right` is false for a wrong
/// note (changed or added). `truth_index` is the truth note it came from,
/// or -1 for an added note.
struct ScoreNote {
    double onset_ms, dur_ms;
    int midi;
    bool right;
    int truth_index;
};

/// The cases of section 2: the truth itself and the negative controls.
enum class Case {
    Right,    // the truth
    Semitone, // 30% of the notes moved by +-1 semitone
    Tone,     // 30% moved by +-2
    Octave,   // 30% moved by +-12
    Added,    // 30% more notes the pianist never played
    LeftOut,  // 30% of the notes removed
    Late120,  // the whole score 120 ms late
    Late250,  // 250 ms late
    Early120, // 120 ms early
    Jitter60  // each note moved by a uniform +-60 ms
};

constexpr Case kAllCases[] = {Case::Right,   Case::Semitone, Case::Tone,
                              Case::Octave,  Case::Added,    Case::LeftOut,
                              Case::Late120, Case::Late250,  Case::Early120,
                              Case::Jitter60};
constexpr size_t kCaseCount = sizeof(kAllCases) / sizeof(kAllCases[0]);

inline const char* case_name(Case c) {
    switch (c) {
    case Case::Right: return "right";
    case Case::Semitone: return "semitone";
    case Case::Tone: return "tone";
    case Case::Octave: return "octave";
    case Case::Added: return "added";
    case Case::LeftOut: return "left_out";
    case Case::Late120: return "late120";
    case Case::Late250: return "late250";
    case Case::Early120: return "early120";
    case Case::Jitter60: return "jitter60";
    }
    return "?";
}

inline bool case_from_name(const char* s, Case& out) {
    for (size_t i = 0; i < kCaseCount; ++i) {
        const char* n = case_name(kAllCases[i]);
        const char* p = s;
        while (*p != '\0' && *p == *n) { ++p; ++n; }
        if (*p == '\0' && *n == '\0') { out = kAllCases[i]; return true; }
    }
    return false;
}

/// The result of a case builder. `kept_unchanged` counts the notes the
/// builder could not make wrong: after 8 redraws the truth still had an
/// attack of the drawn pitch inside the note's meh window, so a changed
/// note keeps its truth pitch (it stays right) and an added note is not
/// added at all.
struct BuiltCase {
    std::vector<ScoreNote> notes;
    int kept_unchanged = 0;
};

/// The judge's windows of a score, per note. `starts` is the sorted list
/// of the score's onsets as the judge sees them.
struct ScoreWindows {
    std::vector<double> starts;
    std::vector<double> perfect_ms; // half width per note
    std::vector<double> meh_ms;
};

inline ScoreWindows compute_windows(const std::vector<ScoreNote>& notes) {
    ScoreWindows w;
    w.starts.reserve(notes.size());
    for (const auto& n : notes) w.starts.push_back(n.onset_ms);
    for (size_t i = 0; i < notes.size(); ++i) {
        const double pace = pace_of(w.starts, int(i));
        w.perfect_ms.push_back(window_ms(Window::Perfect, pace));
        w.meh_ms.push_back(window_ms(Window::Meh, pace));
    }
    return w;
}

/// The truth has an attack of `midi` within half_ms of t.
inline bool truth_has_attack(const std::vector<TruthNote>& truth, int midi, double t, double half_ms) {
    for (const auto& g : truth) {
        if (g.midi != midi) continue;
        if (std::fabs(g.onset_ms - t) <= half_ms) return true;
    }
    return false;
}

/// Build one case from the truth. `seed` feeds std::mt19937 (7 by
/// default), so a case is reproducible. A changed or added note counts as
/// wrong only when the truth has no attack of its new pitch inside its meh
/// window around its score time; a draw that fails is redrawn up to 8
/// times (see BuiltCase::kept_unchanged).
inline BuiltCase build_case(Case c, const std::vector<TruthNote>& truth, unsigned seed = 7) {
    BuiltCase out;
    const size_t n = truth.size();
    if (n == 0) return out;
    std::mt19937 rng(seed);
    const size_t k30 = size_t(std::llround(0.30 * double(n)));

    // The notes of the case's score: the truth notes, then the added ones.
    // Onsets and durations are placed before any pitch draw, so the
    // windows below are the windows of the finished score.
    out.notes.reserve(n + (c == Case::Added ? k30 : 0));
    for (size_t i = 0; i < n; ++i)
        out.notes.push_back({truth[i].onset_ms, truth[i].dur_ms, truth[i].midi, true, int(i)});

    // Which truth notes the case moves, removes or copies.
    std::vector<size_t> order(n);
    for (size_t i = 0; i < n; ++i) order[i] = i;
    std::shuffle(order.begin(), order.end(), rng);

    if (c == Case::LeftOut) {
        for (size_t k = 0; k < k30; ++k) out.notes[order[k]].truth_index = -2; // dropped
        std::vector<ScoreNote> kept;
        for (auto& note : out.notes)
            if (note.truth_index != -2) kept.push_back(note);
        out.notes.swap(kept);
    } else if (c == Case::Late120 || c == Case::Late250 || c == Case::Early120) {
        const double shift = c == Case::Late120 ? 120.0 : (c == Case::Late250 ? 250.0 : -120.0);
        for (auto& note : out.notes) note.onset_ms += shift;
    } else if (c == Case::Jitter60) {
        std::uniform_real_distribution<double> u(-60.0, 60.0);
        for (auto& note : out.notes) note.onset_ms += u(rng);
    }

    // Added notes copy a random truth note's onset and duration and start
    // at its pitch; the draw below moves the pitch away from it.
    if (c == Case::Added) {
        std::uniform_int_distribution<size_t> src(0, n - 1);
        for (size_t a = 0; a < k30; ++a) {
            const size_t s = src(rng);
            out.notes.push_back({truth[s].onset_ms, truth[s].dur_ms, truth[s].midi, false, -1});
        }
    }

    const ScoreWindows w = compute_windows(out.notes);

    auto is_genuinely_wrong = [&](int m, const ScoreNote& note, size_t index) {
        return m >= kMinMidi && m <= kMaxMidi &&
               !truth_has_attack(truth, m, note.onset_ms, w.meh_ms[index]);
    };

    if (c == Case::Semitone || c == Case::Tone || c == Case::Octave) {
        // Moved notes: a new pitch a semitone, a tone or an octave away,
        // random sign. A draw the truth accounts for is redrawn up to 8
        // times; then the note keeps its truth pitch (kept_unchanged).
        const int magnitude = c == Case::Semitone ? 1 : (c == Case::Tone ? 2 : 12);
        std::uniform_int_distribution<int> sign(0, 1);
        for (size_t k = 0; k < k30; ++k) {
            ScoreNote& note = out.notes[order[k]];
            bool ok = false;
            for (int attempt = 0; attempt < 8 && !ok; ++attempt) {
                const int m = note.midi + (sign(rng) != 0 ? magnitude : -magnitude);
                if (!is_genuinely_wrong(m, note, order[k])) continue;
                note.midi = m;
                note.right = false;
                ok = true;
            }
            if (!ok) ++out.kept_unchanged;
        }
    } else if (c == Case::Added) {
        // The pitch 1 to 12 semitones away from the copied note, random
        // sign, redrawn up to 8 times. A note with no genuinely wrong
        // pitch in 8 draws is not added (kept_unchanged).
        std::uniform_int_distribution<int> amount(1, 12);
        std::uniform_int_distribution<int> sign(0, 1);
        for (size_t a = 0; a < k30; ++a) {
            ScoreNote& note = out.notes[n + a];
            bool ok = false;
            for (int attempt = 0; attempt < 8 && !ok; ++attempt) {
                const int mag = amount(rng);
                const int m = note.midi + (sign(rng) != 0 ? mag : -mag);
                if (!is_genuinely_wrong(m, note, n + a)) continue;
                note.midi = m;
                ok = true;
            }
            if (!ok) {
                note.truth_index = -2;
                ++out.kept_unchanged;
            }
        }
        std::vector<ScoreNote> kept;
        for (auto& note : out.notes)
            if (note.truth_index != -2) kept.push_back(note);
        out.notes.swap(kept);
    }

    std::stable_sort(out.notes.begin(), out.notes.end(),
                     [](const ScoreNote& a, const ScoreNote& b) { return a.onset_ms < b.onset_ms; });
    return out;
}

// ---- outcomes and scoring ---------------------------------------------------

/// The outcome of one score note. NotMeasured is the path saying it could
/// not measure the note (no pitch, or a saturated estimate), never a
/// verdict on the pitch.
enum class Outcome { Accepted, Misreported, Absent, NotMeasured };

inline const char* outcome_name(Outcome o) {
    switch (o) {
    case Outcome::Accepted: return "accepted";
    case Outcome::Misreported: return "misreported";
    case Outcome::Absent: return "absent";
    case Outcome::NotMeasured: return "not_measured";
    }
    return "?";
}

/// What the path reports for one score note. onset_ms is its measured
/// attack in stream time (NaN when it measured none). midi is the pitch it
/// reports (NaN when none).
struct NoteResult {
    Outcome outcome = Outcome::Absent;
    double onset_ms = std::numeric_limits<double>::quiet_NaN();
    float midi = std::numeric_limits<float>::quiet_NaN();
};

/// Wall time of a path: the time of every 10 ms kan_forge_live_process
/// call with the sounding count at that time (filled in by the bench from
/// the truth; a path never sees it), and the total time of paths that
/// measure the whole audio at once (core).
struct CpuStats {
    struct Block {
        double t_ms = 0.0;
        double wall_ms = 0.0;
        int sounding = -1;
    };
    std::vector<Block> blocks;
    double total_ms = 0.0;
};

/// A path under test: the audio and one case's score in, one NoteResult
/// per score note out. It never sees the truth (only the oracle does, and
/// it gets the truth at construction). The bench runs a pass whose result
/// does not depend on the score (blind) once per excerpt and pairs it
/// against every case; the path caches that pass internally.
///
/// A `chart` path (the new measurement of section 3) belongs here when it
/// exists: same interface, same scoring, no change to the bench.
struct Path {
    virtual ~Path() = default;
    virtual const char* name() = 0;
    virtual bool uses_score() = 0;
    virtual void run(const std::vector<float>& audio, float sr, const std::vector<ScoreNote>& score,
                     std::vector<NoteResult>& results, CpuStats& cpu) = 0;
    /// A stats line the bench prints with the path's tables (for example
    /// the refused expectation sets of expect). Optional.
    virtual const char* extra() { return nullptr; }
};

// ---- scoring ----------------------------------------------------------------

/// The sounding buckets of the report: 1, 2 to 4, 5 to 10, 11+, and the
/// pooled "up to 10" line. Sounding is the number of truth notes sounding
/// at the attack (onset <= t < onset + dur).
constexpr const char* kBucketNames[] = {"1", "2-4", "5-10", "11+"};
constexpr size_t kBucketCount = 4;

inline size_t sounding_bucket(int sounding) {
    return sounding <= 1 ? 0 : (sounding <= 4 ? 1 : (sounding <= 10 ? 2 : 3));
}

/// The counts of one bucket (or of the pooled "up to 10").
struct BucketStats {
    size_t right_n = 0;
    size_t accepted = 0, not_measured = 0, misreported = 0, absent = 0;
    size_t wrong_n = 0, rejected = 0;
    // (measured onset minus TRUTH onset, the note's perfect half width)
    std::vector<std::pair<double, double>> errs;
};

/// Attack time error of accepted right notes that carry a measured time.
struct TimeErr {
    bool has = false;
    double median_ms = 0.0;
    double p95_abs_ms = 0.0;
    double in_perfect_pct = 0.0;
    double in_50ms_pct = 0.0;
};

inline TimeErr time_error(const BucketStats& b) {
    TimeErr r;
    if (b.errs.empty()) return r;
    r.has = true;
    std::vector<double> signed_err, abs_err;
    double in_perf = 0.0, in_50 = 0.0;
    for (const auto& e : b.errs) {
        signed_err.push_back(e.first);
        abs_err.push_back(std::fabs(e.first));
        if (std::fabs(e.first) <= e.second) in_perf += 1.0;
        if (std::fabs(e.first) <= 50.0) in_50 += 1.0;
    }
    std::sort(signed_err.begin(), signed_err.end());
    std::sort(abs_err.begin(), abs_err.end());
    r.median_ms = signed_err[signed_err.size() / 2];
    // Nearest rank: the smallest value covering 95% of the notes.
    const size_t idx = size_t(std::ceil(0.95 * double(abs_err.size())));
    r.p95_abs_ms = abs_err[idx - 1];
    r.in_perfect_pct = 100.0 * in_perf / double(b.errs.size());
    r.in_50ms_pct = 100.0 * in_50 / double(b.errs.size());
    return r;
}

/// The report of one path on one case, pooled over the excerpts.
struct CaseStats {
    BucketStats buckets[kBucketCount];
    BucketStats up_to_10;
};

/// Score one run: bucket every score note by the notes sounding at its
/// attack and count the outcomes. The sounding count is taken at the
/// note's truth onset (at its score onset for an added note).
inline CaseStats score_case(const std::vector<TruthNote>& truth, const std::vector<ScoreNote>& score,
                            const std::vector<NoteResult>& results) {
    CaseStats st;
    const ScoreWindows w = compute_windows(score);
    for (size_t i = 0; i < score.size(); ++i) {
        const ScoreNote& sn = score[i];
        const NoteResult& r = results[i];
        const double t = sn.truth_index >= 0 ? truth[size_t(sn.truth_index)].onset_ms : sn.onset_ms;
        int sounding = 0;
        for (const auto& g : truth)
            if (g.onset_ms <= t && t < g.onset_ms + g.dur_ms) ++sounding;
        BucketStats* rows[2] = {&st.buckets[sounding_bucket(sounding)], nullptr};
        if (sounding <= 10) rows[1] = &st.up_to_10;
        for (BucketStats* row : rows) {
            if (row == nullptr) continue;
            if (sn.right) {
                ++row->right_n;
                switch (r.outcome) {
                case Outcome::Accepted: ++row->accepted; break;
                case Outcome::NotMeasured: ++row->not_measured; break;
                case Outcome::Misreported: ++row->misreported; break;
                default: ++row->absent; break;
                }
            } else {
                ++row->wrong_n;
                if (r.outcome != Outcome::Accepted) ++row->rejected;
            }
        }
        if (sn.right && r.outcome == Outcome::Accepted && std::isfinite(r.onset_ms)) {
            const double err = r.onset_ms - truth[size_t(sn.truth_index)].onset_ms;
            for (BucketStats* row : rows)
                if (row != nullptr) row->errs.push_back({err, w.perfect_ms[i]});
        }
    }
    return st;
}

/// Merge one case's report into an accumulator (the excerpts pool).
inline void merge(CaseStats& into, const CaseStats& from) {
    BucketStats* dst[5] = {&into.buckets[0], &into.buckets[1], &into.buckets[2], &into.buckets[3], &into.up_to_10};
    const BucketStats* src[5] = {&from.buckets[0], &from.buckets[1], &from.buckets[2], &from.buckets[3], &from.up_to_10};
    for (size_t k = 0; k < 5; ++k) {
        dst[k]->right_n += src[k]->right_n;
        dst[k]->accepted += src[k]->accepted;
        dst[k]->not_measured += src[k]->not_measured;
        dst[k]->misreported += src[k]->misreported;
        dst[k]->absent += src[k]->absent;
        dst[k]->wrong_n += src[k]->wrong_n;
        dst[k]->rejected += src[k]->rejected;
        dst[k]->errs.insert(dst[k]->errs.end(), src[k]->errs.begin(), src[k]->errs.end());
    }
}

inline double pct(double part, double whole) {
    return whole <= 0.0 ? 0.0 : 100.0 * part / whole;
}

} // namespace truth_bench

#endif // KAN_TRUTH_BENCH_HPP
