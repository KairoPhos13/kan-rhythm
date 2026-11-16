// truth_path_pair: the paired decision of the truth bench (issue 41,
// work package 4d). Pairing first (plan findings of 2026-11-16): every
// attack peak of the model is claimed by the written note of its pitch
// whose meh window holds it, and a rival of a written note is then only
// an unclaimed peak no written note explains. A wrong note is judged by
// positive evidence of another pitch, not by weak support of its own,
// and the two refusals (wrong pitch, nothing played) stay two facts.
//
// The measurement, on the offline Basic Pitch posteriors exactly as the
// bp path takes them:
//
//   peaks   for every pitch, each frame where the onset posterior is a
//           local maximum (at least the frame before, more than the
//           frame after) and at least peak_min (0.15). Interior frames
//           only: the first and the last frame of a recording are never
//           peaks.
//   claim   score notes in score order. A note claims the strongest
//           unclaimed peak of its pitch inside its meh window. s is that
//           peak's value and the attack time is its frame time. With no
//           peak, s is the highest onset posterior of the pitch in the
//           meh window and the attack time is the score time.
//   na      the mean note posterior of the pitch over the frames attack
//           +1 to attack +12 (0 when the attack sits at the last frame).
//   nmax    the highest note posterior of the pitch from the attack
//           frame to the written end, at most 1000 ms after the score
//           onset.
//   rival   among the unclaimed peaks at -12, -2, -1, +1, +2 and +12
//           semitones whose time is inside the note's perfect window
//           around the score onset, leaving out a peak within 6 frames
//           of a claimed peak of its own pitch (a double peak of an
//           attack a written note already explains): the strongest. u is
//           its value, un the mean note posterior of the rival pitch
//           over its frames +1 to +12, u_midi its pitch and rival_ms its
//           frame time. With no rival, u and un are 0.
//
// The decision (the settings of PairParams, the defaults are the hand
// rule of the plan's findings):
//
//   real = u >= pair-u and un >= pair-un and u >= s + pair-c.
//   Accepted when (nmax >= pair-a and not (real and un >= nmax + 0.15))
//   or (s >= pair-b and not real).
//   Not accepted: Misreported with the rival's pitch when real, else
//   Absent. These two are always separate outcomes.
//
// Frame windows above are inclusive of their bounds. The study script
// (wp4c-posteriors/pair.py) let its slices take one frame just past the
// written end; the brief's windows win here. The script also read na and
// un over the same frames and kept a note posterior before the attack
// (nb) and rivals over the good and meh windows; the brief's feature
// list has none of them.
//
// The path sees the audio and the case's score only, never the truth.
//
// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
#ifndef KAN_TRUTH_PATH_PAIR_HPP
#define KAN_TRUTH_PATH_PAIR_HPP

#include "truth_paths_bp.hpp"

#include "libkan/basic_pitch.hpp"

#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <limits>
#include <string>
#include <vector>

namespace truth_bench {
namespace pair {

using ::kan::basic_pitch::kMidiOffset;
using ::kan::basic_pitch::kNotes;

inline float nanf_v() { return std::numeric_limits<float>::quiet_NaN(); }
inline double nand_v() { return std::numeric_limits<double>::quiet_NaN(); }

/// A real rival must out-sustain nmax by this to block the sustain bar
/// (the 0.15 of the hand rule; not a setting).
constexpr double kSustainLead = 0.15;

/// The decision's settings. The defaults are the hand rule (floor 99 %)
/// of the plan's findings of 2026-11-16, measured on the development
/// list: a rival peak of 0.3 with a note posterior after its attack of
/// 0.15 leads the written note's support by 0.3 to be real, the note's
/// own note posterior carries it at 0.13 and its onset support at 0.25.
struct PairParams {
    double peak_min = 0.15; // a frame below this is not an attack peak
    double u = 0.3;         // pair-u: a rival's peak must reach this
    double un = 0.15;       // pair-un: its note posterior after the attack
    double c = 0.3;         // pair-c: its lead over s
    double a = 0.13;        // pair-a: nmax accepts at once
    double b = 0.25;        // pair-b: the onset support bar
};

/// What the posteriors say about one score note. `found` is true when a
/// peak was claimed (else the attack time is the score time and s the
/// highest onset posterior in the window). `real` is filled by measure()
/// with the settings it ran with; decide() recomputes it.
struct PairMeasure {
    double attack_ms = nand_v();
    int attack_frame = -1;
    bool found = false;
    float s = 0.0f, na = 0.0f, nmax = 0.0f;
    float u = 0.0f, un = 0.0f;
    int u_midi = 0;             // 0 when there is no rival
    double rival_ms = nand_v(); // the rival peak's frame time
    bool real = false;
};

/// A rival is real only on positive evidence of all three: its peak, its
/// own sustain and its lead over the written note's support.
inline bool is_real(const PairMeasure& m, const PairParams& pp) {
    return double(m.u) >= pp.u && double(m.un) >= pp.un && double(m.u) >= double(m.s) + pp.c;
}

/// One attack peak of one pitch. `claimed` marks the peaks a written
/// note took.
struct Peak {
    int frame = 0;
    float val = 0.0f;
    bool claimed = false;
};

/// Measure every score note against the posteriors: peaks, claims, own
/// sustain and the rival. Score notes are processed in score order and a
/// claimed peak serves one note only. Ties of the same posterior value
/// take the earlier frame.
inline std::vector<PairMeasure> measure(const bp::PosteriorView& p, const std::vector<ScoreNote>& score,
                                        const ScoreWindows& w, const PairParams& pp) {
    std::vector<PairMeasure> out(score.size());
    const int frames = p.frames;
    if (frames <= 0) {
        // No posteriors: the attack time is the score time and nothing
        // else can be measured (every value stays 0).
        for (size_t i = 0; i < score.size(); ++i) out[i].attack_ms = score[i].onset_ms;
        return out;
    }
    auto first_frame_at = [&](double ms) {
        return int(std::lower_bound(p.time_ms.begin(), p.time_ms.end(), ms) - p.time_ms.begin());
    };
    auto last_frame_at = [&](double ms) {
        return int(std::upper_bound(p.time_ms.begin(), p.time_ms.end(), ms) - p.time_ms.begin()) - 1;
    };
    auto mean_note = [&](int midi, int from, int to) {
        float sum = 0.0f;
        for (int f = from; f <= to; ++f) sum += p.note_at(f, midi);
        return sum / float(to - from + 1);
    };

    // The attack peaks, per pitch, in frame order.
    std::vector<std::vector<Peak>> peaks(size_t(kMaxMidi + 1));
    for (int midi = kMinMidi; midi <= kMaxMidi; ++midi) {
        std::vector<Peak>& list = peaks[size_t(midi)];
        for (int f = 1; f + 1 < frames; ++f) {
            const float v = p.onset_at(f, midi);
            if (v < float(pp.peak_min)) continue;
            if (v >= p.onset_at(f - 1, midi) && v > p.onset_at(f + 1, midi)) list.push_back({f, v, false});
        }
    }

    // Claims and own sustain, in score order.
    for (size_t i = 0; i < score.size(); ++i) {
        PairMeasure& m = out[i];
        const int midi = score[i].midi;
        m.attack_ms = score[i].onset_ms;
        if (midi < kMinMidi || midi > kMaxMidi) continue; // no bin for it
        const double t = score[i].onset_ms;
        const double lo = t - w.meh_ms[i], hi = t + w.meh_ms[i];

        Peak* best = nullptr;
        for (Peak& pk : peaks[size_t(midi)]) {
            if (pk.claimed) continue;
            const double tq = p.time_ms[size_t(pk.frame)];
            if (tq < lo || tq > hi) continue;
            if (best == nullptr || pk.val > best->val) best = &pk;
        }
        if (best != nullptr) {
            best->claimed = true;
            m.found = true;
            m.attack_frame = best->frame;
            m.attack_ms = p.time_ms[size_t(best->frame)];
            m.s = best->val;
        } else {
            const int f0 = std::max(0, first_frame_at(lo));
            const int f1 = std::min(frames - 1, last_frame_at(hi));
            float s = 0.0f;
            for (int f = f0; f <= f1; ++f) s = std::max(s, p.onset_at(f, midi));
            m.s = s;
            m.attack_frame = std::min(frames - 1, first_frame_at(t));
        }

        const int a0 = m.attack_frame + 1, a1 = std::min(m.attack_frame + 12, frames - 1);
        m.na = a0 > a1 ? 0.0f : mean_note(midi, a0, a1);

        const double end_ms = std::min(t + score[i].dur_ms, t + 1000.0);
        const int e1 = std::max(m.attack_frame, std::min(frames - 1, last_frame_at(end_ms)));
        float nmax = 0.0f;
        for (int f = m.attack_frame; f <= e1; ++f) nmax = std::max(nmax, p.note_at(f, midi));
        m.nmax = nmax;
    }

    // The rivals, now that every claim is known.
    static const int kRivalDeltas[6] = {-12, -2, -1, 1, 2, 12};
    for (size_t i = 0; i < score.size(); ++i) {
        PairMeasure& m = out[i];
        const int midi = score[i].midi;
        if (midi < kMinMidi || midi > kMaxMidi) continue;
        const double t = score[i].onset_ms;
        const double lo = t - w.perfect_ms[i], hi = t + w.perfect_ms[i];
        const Peak* best = nullptr;
        int best_midi = 0;
        for (int d : kRivalDeltas) {
            const int rival = midi + d;
            if (rival < kMinMidi || rival > kMaxMidi) continue;
            const std::vector<Peak>& list = peaks[size_t(rival)];
            for (const Peak& pk : list) {
                if (pk.claimed) continue;
                const double tq = p.time_ms[size_t(pk.frame)];
                if (tq < lo || tq > hi) continue;
                // A double peak of a claimed attack of its own pitch is
                // not a second attack.
                bool explained = false;
                for (const Peak& c : list) {
                    if (c.claimed && (c.frame > pk.frame ? c.frame - pk.frame : pk.frame - c.frame) <= 6) {
                        explained = true;
                        break;
                    }
                }
                if (explained) continue;
                if (best == nullptr || pk.val > best->val) {
                    best = &pk;
                    best_midi = rival;
                }
            }
        }
        if (best != nullptr) {
            m.u = best->val;
            m.u_midi = best_midi;
            m.rival_ms = p.time_ms[size_t(best->frame)];
            const int b0 = best->frame + 1, b1 = std::min(best->frame + 12, frames - 1);
            m.un = b0 > b1 ? 0.0f : mean_note(best_midi, b0, b1);
        }
        m.real = is_real(m, pp);
    }
    return out;
}

/// The decision on one measurement. The sustain bar (nmax) carries the
/// note while no real rival out-sustains it; the onset bar (s) carries it
/// while no rival is real at all. A refusal names the rival's pitch when
/// the rival is real (wrong pitch), else it says absent (nothing played).
inline NoteResult decide(const PairMeasure& m, const PairParams& pp, int written_midi) {
    NoteResult r;
    const bool real = is_real(m, pp);
    const bool accepted =
        (double(m.nmax) >= pp.a && !(real && double(m.un) >= double(m.nmax) + kSustainLead)) ||
        (double(m.s) >= pp.b && !real);
    if (accepted) {
        r.outcome = Outcome::Accepted;
        r.onset_ms = m.attack_ms;
        r.midi = float(written_midi);
    } else if (real) {
        r.outcome = Outcome::Misreported;
        r.onset_ms = m.rival_ms;
        r.midi = float(m.u_midi);
    }
    return r;
}

/// The pair path's features file columns (deliverable 6). The row is one
/// score note; found is 1 when a peak was claimed and real is 1 when the
/// run's settings make the rival real.
inline constexpr const char* kPairFeatureHeader =
    "excerpt,case,note,right,sounding,midi,score_onset_ms,truth_onset_ms,measured_attack_ms,"
    "s,na,nmax,u,un,u_midi,found,real\n";

/// One features row of the pair path. `truth_onset_ms` is na for an
/// added note (the bench fills it; the path never reads it).
inline void write_pair_feature_row(std::string& out, const char* excerpt, const char* case_label,
                                   size_t index, const ScoreNote& sn, double truth_onset_ms,
                                   int sounding, const PairMeasure& m) {
    char truth_s[32] = "na";
    if (std::isfinite(truth_onset_ms)) std::snprintf(truth_s, sizeof(truth_s), "%.3f", truth_onset_ms);
    char line[2048];
    const int len = std::snprintf(
        line, sizeof(line),
        "%s,%s,%zu,%d,%d,%d,%.3f,%s,%.3f,%.6f,%.6f,%.6f,%.6f,%.6f,%d,%d,%d\n", excerpt, case_label,
        index, sn.right ? 1 : 0, sounding, sn.midi, sn.onset_ms, truth_s, m.attack_ms, double(m.s),
        double(m.na), double(m.nmax), double(m.u), double(m.un), m.u_midi, m.found ? 1 : 0,
        m.real ? 1 : 0);
    out.append(line, size_t(std::max(0, std::min(len, int(sizeof(line)) - 1))));
}

/// pair: Basic Pitch over the whole recording (as bp takes them), then
/// the paired measurement and decision above. The posteriors do not
/// depend on the score, so one infer per excerpt is cached by the audio
/// buffer and its time is the CPU time the bench reports.
struct PairPath : Path {
    PairParams pp;

    const void* cached_audio = nullptr;
    size_t cached_n = 0;
    ::kan::basic_pitch::Posteriorgrams pg;
    bp::PosteriorView view;
    std::vector<PairMeasure> last;

    const char* name() override { return "pair"; }
    bool uses_score() override { return true; }
    const std::vector<PairMeasure>& measures() const { return last; }

    void run(const std::vector<float>& audio, float sr, const std::vector<ScoreNote>& score,
             std::vector<NoteResult>& results, CpuStats& cpu) override {
        if (cached_audio != audio.data() || cached_n != audio.size()) {
            const auto t0 = std::chrono::steady_clock::now();
            pg = ::kan::basic_pitch::infer(audio.data(), audio.size(), double(sr), 1);
            cpu = CpuStats();
            cpu.total_ms =
                std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - t0).count();
            view.frames = pg.frames;
            view.onset = pg.onset.data();
            view.note = pg.note.data();
            view.contour = pg.contour.data();
            view.time_ms.resize(size_t(pg.frames));
            for (int j = 0; j < pg.frames; ++j) view.time_ms[size_t(j)] = pg.frame_time(j) * 1000.0;
            cached_audio = audio.data();
            cached_n = audio.size();
        } else {
            cpu = CpuStats(); // the pass was measured on the first call
        }
        last = measure(view, score, compute_windows(score), pp);
        results.resize(score.size());
        for (size_t i = 0; i < score.size(); ++i) results[i] = decide(last[i], pp, score[i].midi);
    }
};

} // namespace pair
} // namespace truth_bench

#endif // KAN_TRUTH_PATH_PAIR_HPP
