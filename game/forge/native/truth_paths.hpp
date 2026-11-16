// truth_paths: the paths under test for the truth bench, and the live
// audio pass and event dump they share (docs/architecture/
// polyphonic-live-measurement.md section 2). Every path goes through the
// C ABI the game calls (kan_forge_live_*), so what is measured is the
// game's own path. The truth reaches no path except the oracle.
//
// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
#ifndef KAN_TRUTH_PATHS_HPP
#define KAN_TRUTH_PATHS_HPP

#include "truth_bench.hpp"

#include "kan_forge_api.h"
#include "libkan/pitch_shift.hpp"
#include "libkan/score_verify.hpp"

#include <chrono>
#include <cmath>
#include <cstdio>
#include <map>
#include <string>
#include <vector>

namespace truth_bench {

/// The detuned control's audio (work package 4b): the recording shifted
/// up by `cents` through libkan's pitch shifter at equal in and out
/// counts, which keeps the duration, so the score and the truth times
/// stay valid. The shifter's group delay (input latency plus output
/// latency) is cut from the front and the end is padded with silence,
/// so the attacks stay at their times and the length is unchanged.
inline std::vector<float> detuned_audio(const std::vector<float>& in, float sr, float cents) {
    ::kan::PitchShifter shifter(sr);
    shifter.set_detune_cents(float(cents));
    const size_t delay = size_t(shifter.input_latency() + shifter.output_latency());
    std::vector<float> work(in.size() + delay, 0.0f);
    std::copy(in.begin(), in.end(), work.begin());
    std::vector<float> shifted(work.size());
    constexpr size_t kBlock = 512;
    for (size_t at = 0; at < work.size(); at += kBlock) {
        const size_t n = std::min(kBlock, work.size() - at);
        shifter.process(work.data() + at, n, shifted.data() + at, n);
    }
    std::vector<float> out(in.size());
    std::copy(shifted.begin() + std::ptrdiff_t(delay),
              shifted.begin() + std::ptrdiff_t(delay + in.size()), out.begin());
    return out;
}

/// One played note: a HIT plus its latest PITCH. midi <= 0 means no pitch
/// (unresolved, or a pitch of no note number).
struct PlayedNote {
    double onset_ms = 0.0;
    float midi = 0.0f;
};

/// The live audio pass shared by blind and expect: the audio in 10 ms
/// blocks through kan_forge_live_process, exactly as the game feeds it.
/// `expects` (expect only) holds one entry per chord: its score onset and
/// the written pitches, armed 40 ms ahead as polybench does. The sounding
/// counts of the CPU blocks are not set here; the bench fills them from
/// the truth afterwards, so no path reads the truth.
inline void live_audio_pass(const std::vector<float>& audio, float sr,
                            const std::vector<std::pair<double, std::vector<float>>>* expects,
                            std::vector<PlayedNote>& played, CpuStats& cpu, long& refused, long& late) {
    played.clear();
    cpu = CpuStats();
    refused = 0;
    late = 0;
    kan_forge_live* live = kan_forge_live_open("keyboard", sr);
    if (live == nullptr) return;

    const size_t block = size_t(sr / 100.0f); // 480 samples = 10 ms at 48 kHz
    std::vector<kan_forge_live_event> ev(4096);

    // A played note = a HIT plus its latest PITCH (final replaces
    // provisional; unresolved or midi <= 0 = no pitch).
    struct Rec {
        double onset_ms = -1.0;
        float midi = 0.0f;
        int state = 0; // 0 provisional, 1 final, 2 unresolved
    };
    std::map<uint32_t, Rec> recs;

    size_t next_expect = 0;
    for (size_t at = 0; at < audio.size(); at += block) {
        const double now = 1000.0 * double(at) / sr;
        if (expects != nullptr) {
            while (next_expect < expects->size() && (*expects)[next_expect].first < now + 40.0) {
                const auto& e = (*expects)[next_expect];
                if (e.first >= now) {
                    const std::vector<float>& g = e.second;
                    if (kan_forge_live_expect(live, g.data(), int32_t(g.size())) == 0) ++refused;
                } else {
                    ++late; // the chord arrived before it could be armed
                }
                ++next_expect;
            }
        }

        const int32_t n = int32_t(std::min(block, audio.size() - at));
        const auto t0 = std::chrono::steady_clock::now();
        const int32_t got =
            kan_forge_live_process(live, audio.data() + at, n, ev.data(), int32_t(ev.size()));
        const double wall =
            std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - t0).count();
        cpu.blocks.push_back({now, wall, -1});

        for (int32_t k = 0; k < got; ++k) {
            const kan_forge_live_event& e = ev[size_t(k)];
            Rec& r = recs[e.note_id];
            if (e.kind == KAN_FORGE_LIVE_HIT) {
                if (r.onset_ms < 0.0) r.onset_ms = e.onset_ms;
            } else if (e.kind == KAN_FORGE_LIVE_PITCH) {
                if (e.stage == 1) {
                    r.midi = e.midi;
                    r.state = 1;
                } else if (e.stage == 2) {
                    r.state = 2;
                } else if (r.state == 0) {
                    r.midi = e.midi;
                }
            }
        }
    }
    kan_forge_live_close(live);

    for (const auto& kv : recs) {
        const Rec& r = kv.second;
        if (r.onset_ms < 0.0) continue;
        PlayedNote p;
        p.onset_ms = r.onset_ms;
        p.midi = (r.state != 2 && r.midi > 0.0f) ? r.midi : 0.0f;
        played.push_back(p);
    }
    std::sort(played.begin(), played.end(),
              [](const PlayedNote& a, const PlayedNote& b) { return a.onset_ms < b.onset_ms; });
}

/// Pair played notes to score notes and decide the outcome of each. Score
/// notes are processed in score time order and each played note answers
/// one score note at most.
inline void pair_played(const std::vector<ScoreNote>& score, const ScoreWindows& w,
                        const std::vector<PlayedNote>& played, std::vector<NoteResult>& results) {
    results.assign(score.size(), NoteResult{});
    std::vector<char> claimed(played.size(), 0);
    for (size_t i = 0; i < score.size(); ++i) {
        const double t = score[i].onset_ms;
        const double half_cents = 0.5; // 50 cents of written pitch
        int best = -1;
        double bestd = 0.0;

        // The nearest unclaimed played note with the written pitch and an
        // onset inside the meh window: accepted (claimed).
        for (size_t j = 0; j < played.size(); ++j) {
            if (claimed[j] || played[j].midi <= 0.0f) continue;
            if (std::fabs(double(played[j].midi) - double(score[i].midi)) > half_cents) continue;
            const double d = std::fabs(played[j].onset_ms - t);
            if (d > w.meh_ms[i]) continue;
            if (best < 0 || d < bestd) { best = int(j); bestd = d; }
        }
        if (best >= 0) {
            claimed[size_t(best)] = 1;
            results[i].outcome = Outcome::Accepted;
            results[i].onset_ms = played[size_t(best)].onset_ms;
            results[i].midi = played[size_t(best)].midi;
            continue;
        }

        // Else an unclaimed played note with no pitch inside the meh
        // window: not measured (it was struck, but has no pitch).
        for (size_t j = 0; j < played.size(); ++j) {
            if (claimed[j] || played[j].midi > 0.0f) continue;
            const double d = std::fabs(played[j].onset_ms - t);
            if (d > w.meh_ms[i]) continue;
            if (best < 0 || d < bestd) { best = int(j); bestd = d; }
        }
        if (best >= 0) {
            claimed[size_t(best)] = 1;
            results[i].outcome = Outcome::NotMeasured;
            results[i].onset_ms = played[size_t(best)].onset_ms;
            continue;
        }

        // Else an unclaimed played note with another pitch inside the
        // perfect window: misreported.
        for (size_t j = 0; j < played.size(); ++j) {
            if (claimed[j] || played[j].midi <= 0.0f) continue;
            if (std::fabs(double(played[j].midi) - double(score[i].midi)) <= half_cents) continue;
            const double d = std::fabs(played[j].onset_ms - t);
            if (d > w.perfect_ms[i]) continue;
            if (best < 0 || d < bestd) { best = int(j); bestd = d; }
        }
        if (best >= 0) {
            claimed[size_t(best)] = 1;
            results[i].outcome = Outcome::Misreported;
            results[i].onset_ms = played[size_t(best)].onset_ms;
            results[i].midi = played[size_t(best)].midi;
            continue;
        }

        results[i].outcome = Outcome::Absent;
    }
}

/// blind: today's game path. The audio pass does not depend on the score,
/// so it runs once per excerpt and is paired against every case (cached
/// by the audio buffer it was run on).
struct BlindPath : Path {
    const void* cached_audio = nullptr;
    size_t cached_n = 0;
    std::vector<PlayedNote> played;

    const char* name() override { return "blind"; }
    bool uses_score() override { return false; }

    void run(const std::vector<float>& audio, float sr, const std::vector<ScoreNote>& score,
             std::vector<NoteResult>& results, CpuStats& cpu) override {
        if (cached_audio != audio.data() || cached_n != audio.size()) {
            long refused = 0, late = 0;
            live_audio_pass(audio, sr, nullptr, played, cpu, refused, late);
            cached_audio = audio.data();
            cached_n = audio.size();
        } else {
            cpu = CpuStats(); // the pass was measured on the first call
        }
        pair_played(score, compute_windows(score), played, results);
    }
};

/// expect: the same, with the score's chords told to the describer 40 ms
/// ahead (kan_forge_live_expect). The pass depends on the score, so it
/// runs per case. Refused sets are counted.
struct ExpectPath : Path {
    std::string stats;
    long last_sets = 0, last_refused = 0, last_late = 0;

    const char* name() override { return "expect"; }
    bool uses_score() override { return true; }
    const char* extra() override { return stats.c_str(); }

    void run(const std::vector<float>& audio, float sr, const std::vector<ScoreNote>& score,
             std::vector<NoteResult>& results, CpuStats& cpu) override {
        // Chords: the score notes struck within 30 ms of each other, as
        // polybench groups them.
        std::vector<std::pair<double, std::vector<float>>> chords;
        for (const auto& n : score) {
            if (chords.empty() || n.onset_ms - chords.back().first > kChordMs) {
                chords.push_back({n.onset_ms, {}});
            }
            chords.back().second.push_back(float(n.midi));
        }
        long refused = 0, late = 0;
        std::vector<PlayedNote> played;
        live_audio_pass(audio, sr, &chords, played, cpu, refused, late);
        last_sets = long(chords.size());
        last_refused = refused;
        last_late = late;
        char buf[128];
        std::snprintf(buf, sizeof(buf), "expect sets: %zu, refused %ld, too late to arm %ld",
                      chords.size(), refused, late);
        stats = buf;
        pair_played(score, compute_windows(score), played, results);
    }
};

/// core: today's offline core (kan::score_verify::measure_score) on the
/// case's score, the baseline of the old claim. It measures no time (the
/// onset is NaN): accepted when the fitted similarity holds and the pitch
/// is in tune, not measured when the estimate saturated, else absent.
struct CorePath : Path {
    const char* name() override { return "core"; }
    bool uses_score() override { return true; }

    void run(const std::vector<float>& audio, float sr, const std::vector<ScoreNote>& score,
             std::vector<NoteResult>& results, CpuStats& cpu) override {
        std::vector<::kan::score_verify::ScoreNote> sn(score.size());
        for (size_t i = 0; i < score.size(); ++i) {
            sn[i].onset_ms = float(score[i].onset_ms);
            sn[i].dur_ms = float(score[i].dur_ms);
            sn[i].midi = score[i].midi;
        }
        const auto t0 = std::chrono::steady_clock::now();
        ::kan::score_verify::measure_score(audio, sr, sn);
        cpu = CpuStats();
        cpu.total_ms =
            std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - t0).count();

        results.assign(score.size(), NoteResult{});
        for (size_t i = 0; i < score.size(); ++i) {
            const float ratio = sn[i].sim_w > 0.0f ? sn[i].sim_wsum / sn[i].sim_w : 0.0f;
            if (sn[i].saturated) {
                results[i].outcome = Outcome::NotMeasured;
            } else if (ratio >= 0.60f && std::fabs(sn[i].cents_center) <= 50.0f) {
                results[i].outcome = Outcome::Accepted;
            } else {
                results[i].outcome = Outcome::Absent;
            }
            if (sn[i].sim_w > 0.0f && !sn[i].saturated) {
                results[i].midi = float(score[i].midi) + sn[i].cents_center / 100.0f;
            }
        }
    }
};

/// oracle: the control that knows the truth (the only path that ever gets
/// it). Accepted at the truth onset when the truth has an attack of the
/// written pitch inside the meh window (nearest, one truth note serves one
/// score note), else misreported when the truth has another pitch inside
/// the perfect window, else absent.
struct OraclePath : Path {
    const std::vector<TruthNote>& truth;

    explicit OraclePath(const std::vector<TruthNote>& t) : truth(t) {}
    const char* name() override { return "oracle"; }
    bool uses_score() override { return true; }

    void run(const std::vector<float>& /*audio*/, float /*sr*/, const std::vector<ScoreNote>& score,
             std::vector<NoteResult>& results, CpuStats& /*cpu*/) override {
        results.assign(score.size(), NoteResult{});
        const ScoreWindows w = compute_windows(score);
        std::vector<char> served(truth.size(), 0);
        for (size_t i = 0; i < score.size(); ++i) {
            const double t = score[i].onset_ms;
            int best = -1;
            double bestd = 0.0;
            for (size_t j = 0; j < truth.size(); ++j) {
                if (served[j] || truth[j].midi != score[i].midi) continue;
                const double d = std::fabs(truth[j].onset_ms - t);
                if (d > w.meh_ms[i]) continue;
                if (best < 0 || d < bestd) { best = int(j); bestd = d; }
            }
            if (best >= 0) {
                served[size_t(best)] = 1;
                results[i].outcome = Outcome::Accepted;
                results[i].onset_ms = truth[size_t(best)].onset_ms;
                results[i].midi = float(score[i].midi);
                continue;
            }
            int other = -1;
            double otherd = 0.0;
            for (size_t j = 0; j < truth.size(); ++j) {
                if (truth[j].midi == score[i].midi) continue;
                const double d = std::fabs(truth[j].onset_ms - t);
                if (d > w.perfect_ms[i]) continue;
                if (other < 0 || d < otherd) { other = int(j); otherd = d; }
            }
            if (other >= 0) {
                results[i].outcome = Outcome::Misreported;
                results[i].onset_ms = truth[size_t(other)].onset_ms;
                results[i].midi = float(truth[size_t(other)].midi);
                continue;
            }
            results[i].outcome = Outcome::Absent;
        }
    }
};

/// yes: accepts every score note at its score time. The control that
/// proves the bench can show a time error (the shifted cases must read
/// exactly the shift).
struct YesPath : Path {
    const char* name() override { return "yes"; }
    bool uses_score() override { return true; }

    void run(const std::vector<float>& /*audio*/, float /*sr*/, const std::vector<ScoreNote>& score,
             std::vector<NoteResult>& results, CpuStats& /*cpu*/) override {
        results.assign(score.size(), NoteResult{});
        for (size_t i = 0; i < score.size(); ++i) {
            results[i].outcome = Outcome::Accepted;
            results[i].onset_ms = score[i].onset_ms;
            results[i].midi = float(score[i].midi);
        }
    }
};

// A `chart` path (section 3's chart-anchored measurement) goes here when
// it exists: same Path interface, added to the tool's path list.

/// The event dump of a recording (section 3, the one-voice guard): every
/// event of kan_forge_live_process over the audio as text, one line per
/// event, all fields %.9g, nothing else. Byte identical for identical
/// input.
inline std::string dump_live_events(const std::vector<float>& audio, float sr, const char* profile) {
    std::string out;
    kan_forge_live* live = kan_forge_live_open(profile, sr);
    if (live == nullptr) return out;
    const size_t block = size_t(sr / 100.0f);
    std::vector<kan_forge_live_event> ev(4096);
    char line[256];
    for (size_t at = 0; at < audio.size(); at += block) {
        const int32_t n = int32_t(std::min(block, audio.size() - at));
        const int32_t got =
            kan_forge_live_process(live, audio.data() + at, n, ev.data(), int32_t(ev.size()));
        for (int32_t k = 0; k < got; ++k) {
            const kan_forge_live_event& e = ev[size_t(k)];
            std::snprintf(line, sizeof(line),
                          "%.9g %.9g %.9g %.9g %.9g %.9g %.9g %.9g %.9g %.9g %.9g %.9g %.9g\n",
                          double(e.note_id), double(e.kind), double(e.stage), double(e.t_ms),
                          double(e.onset_ms), double(e.midi), double(e.cents), double(e.end_ms),
                          double(e.level), double(e.presence), double(e.similarity), double(e.flags),
                          double(e.group));
            out += line;
        }
    }
    kan_forge_live_close(live);
    return out;
}

} // namespace truth_bench

#endif // KAN_TRUTH_PATHS_HPP
