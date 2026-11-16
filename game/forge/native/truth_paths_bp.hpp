// truth_paths_bp: learned evidence in the truth bench (issue 41, work
// package 2): what Basic Pitch's posteriors can answer about a written
// note. Two paths run the same measurement over different posteriors:
//
//   bp     one pass over the whole recording (kan::basic_pitch::infer),
//          the ceiling of what the posteriors can answer;
//   bp-la  the same decision on posteriors that could exist with a
//          limited look-ahead (infer_window over short windows), what a
//          live stream could have.
//
// Per score note (written pitch m, score time t, meh window W, in score
// time order): the attack is the highest onset posterior of m over the
// frames within [t - W, t + W] that are not already used by an earlier
// score note of the same pitch (a used peak blocks 3 frames either side).
// Its frame time is the measured attack, its value is the support s. The
// rivals are m +- 1, m +- 2 and m +- 12 (inside 21..108), leaving out any
// pitch the score itself has within the perfect window of the measured
// attack. The rival support r is the highest onset posterior of any rival
// within 3 frames of the measured attack, and rival_midi keeps which
// rival it was. n_after and n_before are the mean note posterior of m
// over the 10 frames after and the 10 frames before the attack (clipped
// to the recording; a note that already rang has both high). The
// decision takes a threshold th and a rule: plain accepts when s >= th,
// rival accepts when s >= th and s >= r, and fair (the default, the
// owner's rule of 2026-11-16) takes its own settings (a, b, c) and
// accepts when n_after >= a, or when s >= b and no rival has
// r >= s + c. Not accepted is Misreported (midi = the rival) when the
// rule's onset bar (th, or b for fair) is reached by r and r > s, else
// Absent.
//
// Contour features (work package 4b), computed for every score note
// over the frames from the measured attack to 150 ms after it (the
// score time when no attack was measured). The written pitch m has
// three bins, 3 * (m - 21) to 3 * (m - 21) + 2 (three per semitone from
// A0), centred on bin 3 * (m - 21) + 1: an exact tone peaks on that
// centre bin (the reference capture of libkan's basic_pitch test, an
// exact C5, peaks on bin 154 = 3 * (72 - 21) + 1). Bin j sits at
// (j - centre) * 100 / 3 cents from the written pitch. The five bins of
// a pitch are its three bins and their two neighbours: centre - 2 to
// centre + 2. c_own is the mean over the window's frames of the largest
// contour value in the written pitch's five bins; c_up1, c_dn1, c_up2
// and c_dn2 are the same one and two semitones up and down (each shifts
// the centre by 3 bins). c_cents is the contour-weighted mean offset in
// cents of the bins within +-70 cents of the written pitch (centre - 2
// to centre + 2) over the same frames, pooled over frames and bins,
// empty when the total contour weight is under 0.05. It is the reading
// a tuning tracker will use.
//
// Frame times. The offline path uses Posteriorgrams::frame_time, the
// reference's own mapping. The look-ahead path places frame i of a window
// at window sample i * 256 and takes that position as the frame's time.
// When the two paths are compared (the 1000 ms check) frames are paired
// by that same sample position rule.
//
// The posterior dump (work package 4c) writes the whole-recording
// posteriors of one run and the score it was given, one pair of files per
// excerpt and case, for the analysis of what the learned evidence can
// answer. Its score.csv carries analysis columns (right, sounding,
// truth_onset_ms) that only the bench can fill; the path never reads them.
//
// The path sees the audio and the case's score only, never the truth.
//
// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
#ifndef KAN_TRUTH_PATHS_BP_HPP
#define KAN_TRUTH_PATHS_BP_HPP

#include "truth_bench.hpp"
#include "truth_paths.hpp"

#include "libkan/basic_pitch.hpp"

#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <limits>
#include <string>
#include <vector>

namespace truth_bench {
namespace bp {

using ::kan::basic_pitch::kNotes;
using ::kan::basic_pitch::kMidiOffset;
using ::kan::basic_pitch::kContourBins;

constexpr int kWindow = 43844;   // the reference model's input, samples
constexpr int kFrames = 172;     // frames of one window
constexpr int kHop = 256;        // samples per frame
constexpr double kSampleRate = 22050.0;

inline float nanf_v() { return std::numeric_limits<float>::quiet_NaN(); }
inline double nand_v() { return std::numeric_limits<double>::quiet_NaN(); }

/// The decision rule of deliverable 1 (plain, rival) and of the fair rule
/// of work package 4b (fair, the default).
enum class Rule { Plain, Rival, Fair };

inline const char* rule_name(Rule r) {
    switch (r) {
    case Rule::Plain: return "plain";
    case Rule::Rival: return "rival";
    default: return "fair";
    }
}

inline bool rule_from_name(const std::string& s, Rule& out) {
    if (s == "plain") {
        out = Rule::Plain;
        return true;
    }
    if (s == "rival") {
        out = Rule::Rival;
        return true;
    }
    if (s == "fair") {
        out = Rule::Fair;
        return true;
    }
    return false;
}

/// The fair rule's three settings (the defaults are the floor 99 %
/// development settings of the fusion, plan section 4 of 2026-11-16:
/// a note posterior after the attack of 0.13, an onset support of 0.25
/// and a rival lead of 0.3).
struct FairParams {
    double a = 0.13; // n_after accepts at once
    double b = 0.25; // the onset support bar
    double c = 0.3;  // a rival must lead s by this to veto
};

/// The posteriors of one excerpt as the paths see them: onset and note
/// (frames x 88, the bin of midi is midi - 21), the contour (frames x
/// 264, bin j at 100/3 cents per bin from midi 21; nullptr when the
/// caller has none) and each frame's time in ms (strictly increasing).
struct PosteriorView {
    int frames = 0;
    const float* onset = nullptr;
    const float* note = nullptr;
    const float* contour = nullptr;
    std::vector<double> time_ms;

    float onset_at(int f, int midi) const {
        return onset[size_t(f) * kNotes + size_t(midi - kMidiOffset)];
    }
    float note_at(int f, int midi) const {
        return note[size_t(f) * kNotes + size_t(midi - kMidiOffset)];
    }
    float contour_at(int f, int bin) const {
        return contour[size_t(f) * kContourBins + size_t(bin)];
    }
};

/// What the posteriors say about one score note. The verdict is separate
/// (decide), so one measurement serves many (rule, th) settings. The
/// contour features (c_*) are NaN when they could not be measured.
struct NoteMeasure {
    double attack_ms = nand_v(); // measured attack (frame time), NaN = none
    int attack_frame = -1;
    float s = 0.0f;        // support: the onset posterior at the attack
    float r = 0.0f;        // rival support, 0 when no rival was measured
    float rival_midi = nanf_v();
    float n_before = nanf_v(), n_after = nanf_v();
    float c_own = nanf_v(), c_up1 = nanf_v(), c_dn1 = nanf_v();
    float c_up2 = nanf_v(), c_dn2 = nanf_v();
    float c_cents = nanf_v(); // empty when the contour weight is under 0.05
};

/// The contour features of one score note (work package 4b), over the
/// frames from t_ms (the measured attack, or the score time when no
/// attack was measured) to 150 ms after it. See the header comment for
/// the bin layout. Bins outside 0..263 are left out of the maxima and
/// the weights.
inline void measure_contour(const PosteriorView& p, int midi, double t_ms, NoteMeasure& m) {
    if (p.contour == nullptr || p.frames <= 0 || midi < kMinMidi || midi > kMaxMidi) return;
    const int f0 = int(std::lower_bound(p.time_ms.begin(), p.time_ms.end(), t_ms) - p.time_ms.begin());
    const int f1 =
        int(std::upper_bound(p.time_ms.begin(), p.time_ms.end(), t_ms + 150.0) - p.time_ms.begin()) - 1;
    if (f0 >= p.frames || f0 > f1) return;
    // The written pitch's centre bin: the middle of its three bins (see
    // the header comment).
    const int centre = 3 * (midi - kMidiOffset) + 1;
    auto five_max = [&](int c) {
        double sum = 0.0;
        for (int f = f0; f <= f1; ++f) {
            float best = 0.0f;
            for (int j = c - 2; j <= c + 2; ++j) {
                if (j < 0 || j >= kContourBins) continue;
                best = std::max(best, p.contour_at(f, j));
            }
            sum += best;
        }
        return float(sum / double(f1 - f0 + 1));
    };
    m.c_own = five_max(centre);
    m.c_up1 = five_max(centre + 3);
    m.c_dn1 = five_max(centre - 3);
    m.c_up2 = five_max(centre + 6);
    m.c_dn2 = five_max(centre - 6);
    // The tuning reading: bins within +-70 cents of the written pitch
    // (2 bins either side, the third would sit at 100 cents), the
    // contour as weight, pooled over the window's frames.
    double wsum = 0.0, csum = 0.0;
    for (int f = f0; f <= f1; ++f) {
        for (int j = centre - 2; j <= centre + 2; ++j) {
            if (j < 0 || j >= kContourBins) continue;
            const double w = p.contour_at(f, j);
            wsum += w;
            csum += w * (double(j - centre) * 100.0 / 3.0);
        }
    }
    if (wsum >= 0.05) m.c_cents = float(csum / wsum);
}

/// Measure every score note against the posteriors. Score notes are
/// processed in score time order; a used peak blocks 3 frames either side
/// for the later notes of the same pitch. Ties of the same posterior
/// value take the earlier frame.
inline std::vector<NoteMeasure> measure(const PosteriorView& p, const std::vector<ScoreNote>& score,
                                        const ScoreWindows& w) {
    std::vector<NoteMeasure> out(score.size());
    if (p.frames <= 0) return out;

    // Used frames per pitch (21..108), one flag per frame.
    std::vector<std::vector<char>> used(size_t(kMaxMidi + 1),
                                        std::vector<char>(size_t(p.frames), 0));
    auto first_frame_at = [&](double ms) {
        return int(std::lower_bound(p.time_ms.begin(), p.time_ms.end(), ms) - p.time_ms.begin());
    };
    auto first_frame_past = [&](double ms) {
        return int(std::upper_bound(p.time_ms.begin(), p.time_ms.end(), ms) - p.time_ms.begin());
    };

    static const int kRivalDeltas[6] = {-12, -2, -1, 1, 2, 12};

    for (size_t i = 0; i < score.size(); ++i) {
        NoteMeasure& m = out[i];
        const int midi = score[i].midi;
        if (midi < kMinMidi || midi > kMaxMidi) continue; // no bin for it
        const double t = score[i].onset_ms;

        // The attack: the highest unblocked onset posterior in the meh
        // window.
        const int f0 = std::max(0, first_frame_at(t - w.meh_ms[i]));
        const int f1 = std::min(p.frames, first_frame_past(t + w.meh_ms[i]));
        int best = -1;
        float best_v = -1.0f;
        for (int f = f0; f < f1; ++f) {
            if (used[size_t(midi)][size_t(f)]) continue;
            const float v = p.onset_at(f, midi);
            if (v > best_v) {
                best_v = v;
                best = f;
            }
        }
        if (best < 0) {
            // Every frame used: no attack measured. The contour features
            // still read from the score time.
            measure_contour(p, midi, t, m);
            continue;
        }
        m.attack_frame = best;
        m.attack_ms = p.time_ms[size_t(best)];
        m.s = best_v;
        for (int f = std::max(0, best - 3); f <= std::min(p.frames - 1, best + 3); ++f)
            used[size_t(midi)][size_t(f)] = 1;

        // The rivals, minus any pitch the score itself has within the
        // perfect window of the measured attack.
        float best_r = -1.0f;
        int best_rival = -1;
        for (int d : kRivalDeltas) {
            const int rival = midi + d;
            if (rival < kMinMidi || rival > kMaxMidi) continue;
            bool written_there = false;
            for (const auto& q : score) {
                if (q.midi == rival && std::fabs(q.onset_ms - m.attack_ms) <= w.perfect_ms[i]) {
                    written_there = true;
                    break;
                }
            }
            if (written_there) continue;
            for (int f = std::max(0, best - 3); f <= std::min(p.frames - 1, best + 3); ++f) {
                const float v = p.onset_at(f, rival);
                if (v > best_r) {
                    best_r = v;
                    best_rival = rival;
                }
            }
        }
        if (best_rival >= 0) {
            m.r = std::max(0.0f, best_r);
            m.rival_midi = float(best_rival);
        }

        // The note posterior before and after the attack.
        auto mean_note = [&](int from, int to) {
            const int a = std::max(0, from), b = std::min(p.frames - 1, to);
            if (a > b) return nanf_v();
            float sum = 0.0f;
            for (int f = a; f <= b; ++f) sum += p.note_at(f, midi);
            return sum / float(b - a + 1);
        };
        m.n_before = mean_note(best - 10, best - 1);
        m.n_after = mean_note(best + 1, best + 10);
        measure_contour(p, midi, m.attack_ms, m);
    }
    return out;
}

/// The decision of deliverable 1 and of the fair rule on one measurement.
/// plain and rival take the threshold th; fair takes its own settings
/// (FairParams) and never penalises a right note for a loud rival: a
/// rival vetoes only when it leads the onset support by c.
inline NoteResult decide(const NoteMeasure& m, Rule rule, double th, int written_midi,
                         const FairParams& fp = FairParams()) {
    NoteResult r;
    bool accepted;
    double onset_bar;
    if (rule == Rule::Fair) {
        accepted = (std::isfinite(m.n_after) && double(m.n_after) >= fp.a) ||
                   (double(m.s) >= fp.b && double(m.r) < double(m.s) + fp.c);
        onset_bar = fp.b;
    } else {
        accepted = m.s >= th && (rule == Rule::Plain || m.s >= m.r);
        onset_bar = th;
    }
    if (accepted) {
        r.outcome = Outcome::Accepted;
        r.onset_ms = m.attack_ms;
        r.midi = float(written_midi);
    } else if (m.r >= onset_bar && m.r > m.s && std::isfinite(m.rival_midi)) {
        r.outcome = Outcome::Misreported;
        r.onset_ms = m.attack_ms;
        r.midi = m.rival_midi;
    } else {
        r.outcome = Outcome::Absent;
    }
    return r;
}

// ---- the posterior dump (work package 4c) -----------------------------------

/// One score row of a dump's score.csv. `note`, `midi`, `score_onset_ms`
/// and `score_end_ms` are the columns of the score the path was given.
/// `right`, `sounding` and `truth_onset_ms` are the analysis columns: the
/// bench fills them with the same values the notes CSV carries, for the
/// analysis only. The path never reads them. `truth_onset_ms` is NaN for
/// an added note.
struct DumpScoreRow {
    int note = 0;
    int midi = 0;
    double score_onset_ms = 0.0, score_end_ms = 0.0;
    int right = 0, sounding = 0;
    double truth_onset_ms = nand_v();
};

/// The score rows of a dump, filled from the score the path was given.
/// The analysis columns stay at their defaults here; the bench fills
/// them.
inline std::vector<DumpScoreRow> dump_score_rows(const std::vector<ScoreNote>& score) {
    std::vector<DumpScoreRow> rows(score.size());
    for (size_t i = 0; i < score.size(); ++i) {
        rows[i].note = int(i);
        rows[i].midi = score[i].midi;
        rows[i].score_onset_ms = score[i].onset_ms;
        rows[i].score_end_ms = score[i].onset_ms + score[i].dur_ms;
    }
    return rows;
}

namespace dump_io {

// The .post file is little endian whatever the host is: every integer and
// every float goes in through its bytes, low first.
inline void put_u32(std::string& out, std::uint32_t v) {
    out.push_back(char(v & 0xffu));
    out.push_back(char((v >> 8) & 0xffu));
    out.push_back(char((v >> 16) & 0xffu));
    out.push_back(char((v >> 24) & 0xffu));
}

inline void put_f32(std::string& out, float v) {
    std::uint32_t bits = 0;
    std::memcpy(&bits, &v, 4);
    put_u32(out, bits);
}

inline void put_f64(std::string& out, double v) {
    std::uint64_t bits = 0;
    std::memcpy(&bits, &v, 8);
    put_u32(out, std::uint32_t(bits & 0xffffffffu));
    put_u32(out, std::uint32_t(bits >> 32));
}

inline void put_f32_array(std::string& out, const float* p, size_t n) {
    out.reserve(out.size() + n * 4);
    for (size_t i = 0; i < n; ++i) put_f32(out, p[i]);
}

inline bool get_u32(const char*& p, const char* end, std::uint32_t& v) {
    if (end - p < 4) return false;
    v = std::uint32_t(std::uint8_t(p[0])) | (std::uint32_t(std::uint8_t(p[1])) << 8) |
        (std::uint32_t(std::uint8_t(p[2])) << 16) | (std::uint32_t(std::uint8_t(p[3])) << 24);
    p += 4;
    return true;
}

inline bool get_f32(const char*& p, const char* end, float& v) {
    std::uint32_t bits = 0;
    if (!get_u32(p, end, bits)) return false;
    std::memcpy(&v, &bits, 4);
    return true;
}

inline bool get_f64(const char*& p, const char* end, double& v) {
    std::uint32_t lo = 0, hi = 0;
    if (!get_u32(p, end, lo) || !get_u32(p, end, hi)) return false;
    const std::uint64_t bits = std::uint64_t(lo) | (std::uint64_t(hi) << 32);
    std::memcpy(&v, &bits, 8);
    return true;
}

} // namespace dump_io

/// Write the whole-recording posteriors of one run and the score it was
/// given (work package 4c) as <dir>/<e>_<case>.post and
/// <dir>/<e>_<case>.score.csv, where <e> is the excerpt's index in the
/// manifest with two digits. The .post is little endian: the 8 bytes
/// KANPOST1, int32 frames, int32 88, int32 264, float64 time_ms[frames],
/// float32 onset[frames*88], float32 note[frames*88], float32
/// contour[frames*264], each row major by frame. The score CSV holds a
/// header and one line per score note in the order of the results. False
/// when a file cannot be written.
inline bool write_posterior_dump(const std::string& dir, int e, const char* case_label,
                                 const PosteriorView& p, const std::vector<DumpScoreRow>& rows) {
    if (p.frames < 0 || size_t(p.frames) != p.time_ms.size()) return false;
    const size_t f = size_t(p.frames);
    if (f > 0 && (p.onset == nullptr || p.note == nullptr || p.contour == nullptr)) return false;

    char stem[256];
    std::snprintf(stem, sizeof(stem), "%02d_%s", e, case_label);
    const std::string base = dir + "/" + stem;

    std::string buf;
    buf.reserve(20 + f * 8 + f * (size_t(kNotes) + size_t(kNotes) + size_t(kContourBins)) * 4);
    buf.append("KANPOST1", 8);
    dump_io::put_u32(buf, std::uint32_t(p.frames));
    dump_io::put_u32(buf, std::uint32_t(kNotes));
    dump_io::put_u32(buf, std::uint32_t(kContourBins));
    for (size_t i = 0; i < f; ++i) dump_io::put_f64(buf, p.time_ms[i]);
    dump_io::put_f32_array(buf, p.onset, f * kNotes);
    dump_io::put_f32_array(buf, p.note, f * kNotes);
    dump_io::put_f32_array(buf, p.contour, f * kContourBins);
    std::FILE* post = std::fopen((base + ".post").c_str(), "wb");
    if (post == nullptr) return false;
    const bool post_ok = std::fwrite(buf.data(), 1, buf.size(), post) == buf.size();
    if (std::fclose(post) != 0) return false;
    if (!post_ok) return false;

    std::FILE* csv = std::fopen((base + ".score.csv").c_str(), "w");
    if (csv == nullptr) return false;
    std::fprintf(csv, "note,midi,score_onset_ms,score_end_ms,right,sounding,truth_onset_ms\n");
    for (const DumpScoreRow& r : rows) {
        char truth_s[32];
        if (std::isfinite(r.truth_onset_ms))
            std::snprintf(truth_s, sizeof(truth_s), "%.3f", r.truth_onset_ms);
        else
            std::snprintf(truth_s, sizeof(truth_s), "na");
        std::fprintf(csv, "%d,%d,%.3f,%.3f,%d,%d,%s\n", r.note, r.midi, r.score_onset_ms,
                     r.score_end_ms, r.right, r.sounding, truth_s);
    }
    return std::fclose(csv) == 0;
}

/// A whole-recording posterior dump read back from its .post file (the
/// selftest's round trip and the analysis tools).
struct PosteriorDump {
    int frames = 0;
    std::vector<double> time_ms;
    std::vector<float> onset, note, contour; // row major by frame
};

/// Read a .post file back. False when the file cannot be read or does not
/// hold a dump of this format.
inline bool read_posterior_dump(const std::string& path, PosteriorDump& out) {
    std::FILE* f = std::fopen(path.c_str(), "rb");
    if (f == nullptr) return false;
    std::string buf;
    char chunk[65536];
    for (size_t got; (got = std::fread(chunk, 1, sizeof(chunk), f)) > 0;) buf.append(chunk, got);
    const bool read_ok = std::feof(f) != 0;
    std::fclose(f);
    if (!read_ok || buf.size() < 20) return false;

    const char* p = buf.data();
    const char* end = p + buf.size();
    if (std::memcmp(p, "KANPOST1", 8) != 0) return false;
    p += 8;
    std::uint32_t frames = 0, notes = 0, bins = 0;
    if (!dump_io::get_u32(p, end, frames) || !dump_io::get_u32(p, end, notes) ||
        !dump_io::get_u32(p, end, bins))
        return false;
    if (notes != std::uint32_t(kNotes) || bins != std::uint32_t(kContourBins)) return false;
    const size_t f_count = size_t(frames);
    if (buf.size() != 20 + f_count * 8 + f_count * (size_t(notes) * 2 + size_t(bins)) * 4)
        return false;

    PosteriorDump d;
    d.frames = int(frames);
    d.time_ms.resize(f_count);
    d.onset.resize(f_count * kNotes);
    d.note.resize(f_count * kNotes);
    d.contour.resize(f_count * kContourBins);
    for (size_t i = 0; i < f_count; ++i)
        if (!dump_io::get_f64(p, end, d.time_ms[i])) return false;
    for (size_t i = 0; i < d.onset.size(); ++i)
        if (!dump_io::get_f32(p, end, d.onset[i])) return false;
    for (size_t i = 0; i < d.note.size(); ++i)
        if (!dump_io::get_f32(p, end, d.note[i])) return false;
    for (size_t i = 0; i < d.contour.size(); ++i)
        if (!dump_io::get_f32(p, end, d.contour[i])) return false;
    out = std::move(d);
    return true;
}

/// bp: Basic Pitch over the whole recording (the ceiling). The
/// posteriors do not depend on the score, so one infer per excerpt is
/// cached by the audio buffer (as the blind path caches its pass). The
/// CPU time of that pass is what the bench reports for this path.
struct BpPath : Path {
    double th = 0.3;
    Rule rule = Rule::Fair;
    FairParams fp;

    const void* cached_audio = nullptr;
    size_t cached_n = 0;
    ::kan::basic_pitch::Posteriorgrams pg;
    PosteriorView view;
    std::vector<NoteMeasure> last;

    const char* name() override { return "bp"; }
    bool uses_score() override { return true; }
    const std::vector<NoteMeasure>& measures() const { return last; }
    /// The posteriors of the whole recording of the last run (the dump of
    /// work package 4c writes them out).
    const PosteriorView& posteriors() const { return view; }

    void run(const std::vector<float>& audio, float sr, const std::vector<ScoreNote>& score,
             std::vector<NoteResult>& results, CpuStats& cpu) override {
        if (cached_audio != audio.data() || cached_n != audio.size()) {
            const auto t0 = std::chrono::steady_clock::now();
            pg = ::kan::basic_pitch::infer(audio.data(), audio.size(), double(sr), 1);
            cpu = CpuStats();
            cpu.total_ms = std::chrono::duration<double, std::milli>(
                               std::chrono::steady_clock::now() - t0)
                               .count();
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
        last = measure(view, score, compute_windows(score));
        results.resize(score.size());
        for (size_t i = 0; i < score.size(); ++i)
            results[i] = decide(last[i], rule, th, score[i].midi, fp);
    }
};

/// bp-la: the same measurement on posteriors that could exist with a
/// limited look-ahead. The recording is resampled once to 22050 Hz and
/// cut into chunks of 5 frames. Each chunk gets one 43844 sample window
/// that ENDS at the chunk's last frame time plus the look-ahead (samples
/// before and after the recording are zero), and only that chunk's 5
/// frames are kept. Every kept frame then saw between L and L + 58 ms of
/// the future and nothing more: the window's end is the chunk's last
/// frame time plus L, the kept frames sit at window sample i * 256 at or
/// just before their nominal sample f * 256 (at most 256 samples early),
/// and 5 frames span 5 * 256 samples. Frame i of a window sits at window
/// sample i * 256 and that position is the frame's time in ms. This
/// costs about 30 times the offline network (one window per 5 frames
/// instead of one per 142), so the bench runs it with --jobs and does
/// not report its CPU time.
struct BpLaPath : Path {
    double th = 0.3;
    Rule rule = Rule::Fair;
    FairParams fp;
    double lookahead_ms = 100.0;

    const void* cached_audio = nullptr;
    size_t cached_n = 0;
    PosteriorView view;
    std::vector<float> onset, note, contour;
    std::vector<NoteMeasure> last;

    const char* name() override { return "bp-la"; }
    bool uses_score() override { return true; }
    bool reports_cpu() override { return false; }
    const std::vector<NoteMeasure>& measures() const { return last; }

    void run(const std::vector<float>& audio, float sr, const std::vector<ScoreNote>& score,
             std::vector<NoteResult>& results, CpuStats& cpu) override {
        if (cached_audio != audio.data() || cached_n != audio.size()) {
            compute(audio, sr);
            cached_audio = audio.data();
            cached_n = audio.size();
        }
        cpu = CpuStats(); // no CPU numbers for this path
        last = measure(view, score, compute_windows(score));
        results.resize(score.size());
        for (size_t i = 0; i < score.size(); ++i)
            results[i] = decide(last[i], rule, th, score[i].midi, fp);
    }

    void compute(const std::vector<float>& audio, float sr) {
        const std::vector<float> sig =
            ::kan::basic_pitch::resample(audio.data(), audio.size(), double(sr), kSampleRate);
        const long n = long(sig.size());
        const long frames = (n + kHop - 1) / kHop;
        onset.assign(size_t(frames) * kNotes, 0.0f);
        note.assign(size_t(frames) * kNotes, 0.0f);
        contour.assign(size_t(frames) * kContourBins, 0.0f);
        view.frames = int(frames);
        view.onset = onset.data();
        view.note = note.data();
        view.contour = contour.data();
        view.time_ms.assign(size_t(frames), 0.0);
        const long la = std::lround(lookahead_ms * kSampleRate / 1000.0);

        std::vector<float> win(kWindow), wn(size_t(kFrames) * kNotes), wo(size_t(kFrames) * kNotes),
            wc(size_t(kFrames) * kContourBins);
        for (long f0 = 0; f0 < frames; f0 += 5) {
            const long f1 = std::min(f0 + 5, frames);
            const long f_last = f1 - 1;
            const long end = f_last * kHop + la; // the window ends here
            const long start = end - kWindow;
            std::fill(win.begin(), win.end(), 0.0f);
            for (long s = 0; s < kWindow; ++s) {
                const long g = start + s;
                if (g >= 0 && g < n) win[size_t(s)] = sig[size_t(g)];
            }
            ::kan::basic_pitch::infer_window(win.data(), wn.data(), wo.data(), wc.data());
            for (long f = f0; f < f1; ++f) {
                // The window frame at or just before the nominal sample
                // f * kHop, so the frame sits in [f * kHop - kHop, f * kHop].
                long i = f * kHop - start >= 0 ? (f * kHop - start) / kHop : 0;
                if (i >= kFrames) i = kFrames - 1;
                view.time_ms[size_t(f)] = double(start + i * kHop) / kSampleRate * 1000.0;
                for (int b = 0; b < kNotes; ++b) {
                    onset[size_t(f) * kNotes + size_t(b)] = wo[size_t(i) * kNotes + size_t(b)];
                    note[size_t(f) * kNotes + size_t(b)] = wn[size_t(i) * kNotes + size_t(b)];
                }
                for (int b = 0; b < kContourBins; ++b)
                    contour[size_t(f) * kContourBins + size_t(b)] =
                        wc[size_t(i) * kContourBins + size_t(b)];
            }
        }
        if (lookahead_ms == 1000.0) check_against_offline(audio, sr);
    }

    /// The check of deliverable 4: with a 1000 ms look-ahead, print the
    /// mean and the worst absolute difference of the onset posterior
    /// against the offline infer on the same excerpt. Frames are paired
    /// by the sample position rule of both paths.
    void check_against_offline(const std::vector<float>& audio, float sr) {
        const ::kan::basic_pitch::Posteriorgrams off =
            ::kan::basic_pitch::infer(audio.data(), audio.size(), double(sr), 1);
        // The offline frames come from windows every (kWindow - 30 * kHop)
        // samples, 142 frames kept from each, the first window 15 frames
        // after a 30 * kHop / 2 sample silence (libkan/src/basic_pitch.cpp,
        // infer): frame j sits at sample j * kHop + (kWindowHop - kKeep *
        // kHop) * (j / kKeep) of the resampled recording, which is 188
        // samples before j * kHop for every window passed.
        constexpr long kWindowHop = kWindow - 30 * kHop; // 36164
        constexpr long kKeep = kFrames - 30;            // 142
        auto off_pos = [&](long j) { return j * kHop + (kWindowHop - kKeep * kHop) * (j / kKeep); };

        double sum = 0.0, worst = 0.0, sum_hot = 0.0;
        long cells = 0, hot = 0, paired = 0;
        long j = 0;
        for (long f = 0; f < view.frames; ++f) {
            const long pos = std::lround(view.time_ms[size_t(f)] / 1000.0 * kSampleRate);
            while (j + 1 < off.frames && std::labs(off_pos(j + 1) - pos) <= std::labs(off_pos(j) - pos))
                ++j;
            if (j >= off.frames) break;
            if (std::labs(off_pos(j) - pos) > kHop / 2) continue; // no frame that close
            ++paired;
            for (int b = 0; b < kNotes; ++b) {
                const double a = double(view.onset_at(int(f), b + kMidiOffset));
                const double o = double(off.onset[size_t(j) * kNotes + size_t(b)]);
                const double d = std::fabs(a - o);
                sum += d;
                worst = std::max(worst, d);
                ++cells;
                if (std::max(a, o) >= 0.5) {
                    sum_hot += d;
                    ++hot;
                }
            }
        }
        std::printf("bp-la look-ahead check (look-ahead 1000 ms): %ld of %d frames paired with the "
                    "offline infer\n",
                    paired, view.frames);
        std::printf("  onset posterior |difference|: mean %.4f, worst %.4f\n",
                    cells > 0 ? sum / double(cells) : 0.0, worst);
        std::printf("  where either posterior >= 0.5: mean %.4f (%ld cells)\n",
                    hot > 0 ? sum_hot / double(hot) : 0.0, hot);
        if (worst > 0.02) {
            std::printf(
                "  differences above 0.02 come from the 2 s window: the CQT is normalised by that\n"
                "  window's own minimum and maximum, and its lowest octaves read about 3 s of\n"
                "  audio, so the same frame scales and reads differently in two windows that hold\n"
                "  different audio. The offline windows see a varying 177 ms to 1.8 s of future\n"
                "  across their 142 kept frames; the 1000 ms chunks see 1000 to 1058 ms.\n");
        }
    }
};

} // namespace bp
} // namespace truth_bench

#endif // KAN_TRUTH_PATHS_BP_HPP
