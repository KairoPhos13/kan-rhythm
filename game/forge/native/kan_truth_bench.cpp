// kan_truth_bench: how well does a live path judge real piano, with
// negative controls?
//
// The yardstick of docs/architecture/polyphonic-live-measurement.md
// section 2 (issue 41). It drives the C ABI the game calls
// (kan_forge_live_*) on a recording and the MIDI it was played from (the
// truth), builds every case from that truth (the right score, pitches a
// semitone / a tone / an octave off, notes added, notes left out, the
// score shifted and jittered), runs the paths on each case and reports,
// by how many notes sound at the attack: right notes accepted, wrong
// notes rejected, the attack time's error against the truth, and CPU per
// 10 ms block. Only the oracle ever sees the truth.
//
// Usage:
//   kan_truth_bench --maestro <dir> --manifest <file>   (excerpts)
//   kan_truth_bench --audio a.wav --truth a.midi --from 0 --to 60
//   [--paths blind,expect,core,oracle,yes,bp,bp-la,pair]
//                           (default blind,core)
//   [--cases all | right,semitone,...] [--seed N] [--repeat N]
//                           (all is every case but the detuned controls
//                           detune20/40/60, which run only when named)
//   [--out <dir>]           writes summary.txt and notes.csv
//   [--bp-th 0.3] [--bp-rule fair | plain | rival]  the bp paths' decision
//                           (default fair: accept when n_after >= A, or
//                           when s >= B and no rival has r >= s + c)
//   [--bp-fair-a 0.13 --bp-fair-b 0.25 --bp-fair-c 0.3]
//                           the fair rule's settings
//   [--bp-lookahead <ms>]   the look-ahead the bp-la path must respect
//   [--pair-u 0.3 --pair-un 0.15 --pair-c 0.3]
//   [--pair-a 0.13 --pair-b 0.25 --pair-peak-min 0.15]
//                           the pair path's decision settings (a rival is
//                           real when u >= pair-u, un >= pair-un and
//                           u >= s + pair-c; accept when nmax >= pair-a
//                           and no real rival out-sustains it by 0.15, or
//                           when s >= pair-b and no rival is real)
//   [--pair-claim closeness|strongest]
//                           how a note claims its peak (default
//                           closeness: the highest value times 1 minus
//                           its distance from the score time plus the
//                           running offset, over the meh window;
//                           strongest is the claim of package 4d)
//   [--pair-off-k 15 --pair-off-min 0.3]
//                           the running time offset: the median of the
//                           last pair-off-k unambiguous notes (exactly
//                           one peak of at least pair-off-min of the
//                           pitch in the meh window) written at least
//                           300 ms earlier; 0 when there is none
//   [--features <file.csv>] the bp or the pair path writes one row per
//                           excerpt, case and score note (the fusion
//                           input), its own columns; when both paths run,
//                           the pair rows take the file and the bp rows
//                           go to a sibling with .bp before .csv
//   [--dump-posteriors <dir>]
//                           the bp path writes its whole-recording
//                           posteriors and the score it was given, one
//                           .post and one .score.csv per excerpt and
//                           case, plus index.csv (work package 4c)
//   --bp-sweep              instead of the paths: one table over (rule,
//                           th) built from one bp measurement per score
//                           note, with --features the same rows
//   [--jobs N]              excerpts in parallel (at most 6), for runs
//                           where CPU time is not reported (bp-la and the
//                           sweep); with --jobs no CPU is measured
//   --dump-events <audio> --profile <name>
//                           every live event as one text line (compare a
//                           path before and after a change)
//   --make-manifests <maestro-v3.0.0.csv>
//                           writes truth_manifest_dev.txt and
//                           truth_manifest_heldout.txt into the working
//                           directory (file names only, no data)
//
// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
#include "bench_io.hpp"
#include "truth_bench.hpp"
#include "truth_paths.hpp"
#include "truth_paths_bp.hpp"
#include "truth_path_pair.hpp"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <filesystem>
#include <map>
#include <memory>
#include <string>
#include <thread>
#include <vector>

using namespace truth_bench;

namespace {

// ---- small formatting helpers ----------------------------------------------

const char* pct_str(double part, double whole, char* buf, size_t cap) {
    if (whole <= 0.0) {
        std::snprintf(buf, cap, "n/a");
        return buf;
    }
    std::snprintf(buf, cap, "%.1f", 100.0 * part / whole);
    return buf;
}

// One report row: counts by outcome, rejection of wrong notes, and the
// attack time error of accepted right notes (measured onset minus TRUTH
// onset). A path that measures no time prints n/a for the time columns.
void print_row(FILE* f, const char* label, const BucketStats& b) {
    char a[16], n[16], m[16], s[16], r[16];
    std::fprintf(f, "%11s %7zu %7s %7s %7s %7s %8zu %7s", label, b.right_n,
                 pct_str(double(b.accepted), double(b.right_n), a, sizeof(a)),
                 pct_str(double(b.not_measured), double(b.right_n), n, sizeof(n)),
                 pct_str(double(b.misreported), double(b.right_n), m, sizeof(m)),
                 pct_str(double(b.absent), double(b.right_n), s, sizeof(s)), b.wrong_n,
                 pct_str(double(b.rejected), double(b.wrong_n), r, sizeof(r)));
    const TimeErr te = time_error(b);
    if (te.has) {
        std::fprintf(f, " %9.1f %8.1f %6.1f %6.1f\n", te.median_ms, te.p95_abs_ms, te.in_perfect_pct,
                     te.in_50ms_pct);
    } else {
        std::fprintf(f, " %9s %8s %6s %6s\n", "n/a", "n/a", "n/a", "n/a");
    }
}

void print_header(FILE* f) {
    std::fprintf(f, "%11s %7s %7s %7s %7s %7s %8s %7s %9s %8s %6s %6s\n", "sounding", "right",
                 "acc%", "nm%", "mis%", "abs%", "wrong", "rej%", "med ms", "p95|e|", "perf%",
                 "50ms%");
}

std::string trim(const std::string& s) {
    size_t a = 0, b = s.size();
    while (a < b && (s[a] == ' ' || s[a] == '\t' || s[a] == '\r' || s[a] == '\n')) ++a;
    while (b > a && (s[b - 1] == ' ' || s[b - 1] == '\t' || s[b - 1] == '\r' || s[b - 1] == '\n')) --b;
    return s.substr(a, b - a);
}

// ---- manifest -------------------------------------------------------------

struct Excerpt {
    std::string audio, midi; // relative to the maestro dir (or absolute)
    double from_s = 0.0, to_s = 0.0;
};

bool read_manifest(const std::string& path, std::vector<Excerpt>& out) {
    std::FILE* f = std::fopen(path.c_str(), "r");
    if (f == nullptr) return false;
    char line[1024];
    while (std::fgets(line, sizeof(line), f) != nullptr) {
        const std::string t = trim(line);
        if (t.empty() || t[0] == '#') continue;
        Excerpt e;
        char a[512] = {0}, m[512] = {0};
        if (std::sscanf(t.c_str(), "%511s %511s %lf %lf", a, m, &e.from_s, &e.to_s) != 4) continue;
        e.audio = a;
        e.midi = m;
        out.push_back(e);
    }
    std::fclose(f);
    return !out.empty();
}

// ---- MAESTRO csv (fields are quoted and hold commas) -----------------------

std::vector<std::string> csv_fields(const std::string& line) {
    std::vector<std::string> fields;
    std::string cur;
    bool quoted = false;
    for (size_t i = 0; i < line.size(); ++i) {
        const char c = line[i];
        if (quoted) {
            if (c == '"') {
                if (i + 1 < line.size() && line[i + 1] == '"') {
                    cur += '"';
                    ++i;
                } else {
                    quoted = false;
                }
            } else {
                cur += c;
            }
        } else if (c == '"') {
            quoted = true;
        } else if (c == ',') {
            fields.push_back(cur);
            cur.clear();
        } else {
            cur += c;
        }
    }
    fields.push_back(cur);
    return fields;
}

struct MaestroRow {
    std::string split, midi, audio;
    double duration = 0.0;
};

bool read_maestro_csv(const std::string& path, std::vector<MaestroRow>& out) {
    std::FILE* f = std::fopen(path.c_str(), "r");
    if (f == nullptr) return false;
    char line[2048];
    bool first = true;
    while (std::fgets(line, sizeof(line), f) != nullptr) {
        if (first) {
            first = false; // header
            continue;
        }
        const std::string t = trim(line);
        if (t.empty()) continue;
        const std::vector<std::string> fields = csv_fields(t);
        if (fields.size() < 7) continue;
        MaestroRow r;
        r.split = fields[2];
        r.midi = fields[4];
        r.audio = fields[5];
        r.duration = std::atof(fields[6].c_str());
        out.push_back(r);
    }
    std::fclose(f);
    return !out.empty();
}

int make_manifests(const std::string& csv_path) {
    std::vector<MaestroRow> rows;
    if (!read_maestro_csv(csv_path, rows)) {
        std::fprintf(stderr, "cannot read MAESTRO csv %s\n", csv_path.c_str());
        return 1;
    }
    const char* kNocturne = "2011/MIDI-Unprocessed_06_R3_2011_MID--AUDIO_R3-D3_05_Track05_wav";

    std::vector<size_t> train, validation, test, nocturne;
    for (size_t i = 0; i < rows.size(); ++i) {
        if (rows[i].duration < 150.0) continue;
        if (rows[i].audio.rfind(kNocturne, 0) == 0) {
            nocturne.push_back(i);
        } else if (rows[i].split == "train") {
            train.push_back(i);
        } else if (rows[i].split == "validation") {
            validation.push_back(i);
        } else if (rows[i].split == "test") {
            test.push_back(i);
        }
    }
    if (nocturne.size() != 1) {
        std::fprintf(stderr, "MAESTRO csv must hold exactly one %s.wav\n", kNocturne);
        return 1;
    }

    // Dev: 12 recordings from train. Held out: 6 validation and 6 test.
    // The held-out list starts with the Chopin Nocturne Op. 9 No. 2 (2011)
    // from 0 to 60 s, one of its validation recordings, then the other
    // picks. Each other excerpt starts at 0 and runs for a drawn 30 to 90 s
    // (seed 7 for the picks and the lengths).
    std::mt19937 rng(7);
    std::shuffle(train.begin(), train.end(), rng);
    std::vector<size_t> dev(train.begin(), train.begin() + std::min<size_t>(12, train.size()));
    std::shuffle(validation.begin(), validation.end(), rng);
    std::shuffle(test.begin(), test.end(), rng);
    std::vector<size_t> held;
    held.push_back(nocturne[0]);
    for (size_t i = 0; i < validation.size() && held.size() < 6; ++i) {
        if (validation[i] != nocturne[0]) held.push_back(validation[i]);
    }
    for (size_t i = 0; i < test.size() && held.size() < 12; ++i) held.push_back(test[i]);
    if (dev.size() < 12 || held.size() < 12) {
        std::fprintf(stderr, "MAESTRO csv has too few recordings of at least 150 s\n");
        return 1;
    }

    std::uniform_int_distribution<int> len(30, 90);
    std::mt19937 len_rng(7);
    std::FILE* f = std::fopen("truth_manifest_dev.txt", "w");
    if (f == nullptr) return 1;
    for (size_t i : dev) {
        std::fprintf(f, "%s %s 0 %d\n", rows[i].audio.c_str(), rows[i].midi.c_str(), len(len_rng));
    }
    std::fclose(f);

    std::mt19937 len_rng2(7);
    std::FILE* g = std::fopen("truth_manifest_heldout.txt", "w");
    if (g == nullptr) return 1;
    std::fprintf(g, "%s %s 0 60\n", rows[nocturne[0]].audio.c_str(), rows[nocturne[0]].midi.c_str());
    for (size_t k = 1; k < held.size(); ++k) {
        std::fprintf(g, "%s %s 0 %d\n", rows[held[k]].audio.c_str(), rows[held[k]].midi.c_str(),
                     len(len_rng2));
    }
    std::fclose(g);

    std::printf("wrote truth_manifest_dev.txt (%zu excerpts) and truth_manifest_heldout.txt (%zu excerpts)\n",
                dev.size(), held.size());
    return 0;
}

// ---- cpu ------------------------------------------------------------------

double median_of(std::vector<double> v) {
    if (v.empty()) return 0.0;
    std::sort(v.begin(), v.end());
    return v[v.size() / 2];
}

double percentile_of(std::vector<double> v, double q) {
    if (v.empty()) return 0.0;
    std::sort(v.begin(), v.end());
    const size_t idx = size_t(std::ceil(q * double(v.size())));
    return v[idx - 1];
}

struct CpuPoint {
    double share = 0.0; // percent of real time
    double block_med = 0.0, block_p99 = 0.0, block_worst = 0.0;
    bool has_blocks = false;
};

CpuPoint cpu_point(const CpuStats& cpu, double audio_ms) {
    CpuPoint p;
    if (!cpu.blocks.empty()) {
        std::vector<double> wall;
        double total = 0.0;
        for (const auto& b : cpu.blocks) {
            wall.push_back(b.wall_ms);
            total += b.wall_ms;
        }
        p.share = 100.0 * total / std::max(1.0, audio_ms);
        p.block_med = median_of(wall);
        p.block_p99 = percentile_of(wall, 0.99);
        p.block_worst = *std::max_element(wall.begin(), wall.end());
        p.has_blocks = true;
    } else {
        p.share = 100.0 * cpu.total_ms / std::max(1.0, audio_ms);
    }
    return p;
}

// The sounding count at every measured block, from the truth. The bench
// fills it in: a path never sees the truth.
void annotate_cpu(CpuStats& cpu, const std::vector<TruthNote>& truth) {
    for (auto& b : cpu.blocks) {
        int sounding = 0;
        for (const auto& g : truth)
            if (g.onset_ms <= b.t_ms && b.t_ms < g.onset_ms + g.dur_ms) ++sounding;
        b.sounding = sounding;
    }
}

std::unique_ptr<Path> make_path(const std::string& name, const std::vector<TruthNote>& truth,
                                double bp_th, bp::Rule bp_rule, double bp_lookahead_ms,
                                const bp::FairParams& bp_fair, const pair::PairParams& pair_pp) {
    if (name == "blind") return std::make_unique<BlindPath>();
    if (name == "expect") return std::make_unique<ExpectPath>();
    if (name == "core") return std::make_unique<CorePath>();
    if (name == "oracle") return std::make_unique<OraclePath>(truth);
    if (name == "pair") {
        auto p = std::make_unique<pair::PairPath>();
        p->pp = pair_pp;
        return p;
    }
    if (name == "bp") {
        auto p = std::make_unique<bp::BpPath>();
        p->th = bp_th;
        p->rule = bp_rule;
        p->fp = bp_fair;
        return p;
    }
    if (name == "bp-la") {
        auto p = std::make_unique<bp::BpLaPath>();
        p->th = bp_th;
        p->rule = bp_rule;
        p->fp = bp_fair;
        p->lookahead_ms = bp_lookahead_ms;
        return p;
    }
    return std::make_unique<YesPath>();
}

/// The audio of one excerpt for one case: the recording itself, or the
/// detuned control's shifted copy (work package 4b), cached per shift.
struct CaseAudio {
    const std::vector<float>& base;
    float sr;
    std::map<int, std::vector<float>> shifted;

    CaseAudio(const std::vector<float>& a, float sample_rate) : base(a), sr(sample_rate) {}
    const std::vector<float>& of(Case c) {
        const double cents = case_detune_cents(c);
        if (cents == 0.0) return base;
        const int key = int(cents);
        auto it = shifted.find(key);
        if (it == shifted.end())
            it = shifted.emplace(key, detuned_audio(base, sr, cents)).first;
        return it->second;
    }
};

// ---- truth columns of one score note (the path never sees these) -----------

double truth_onset_of(const std::vector<TruthNote>& truth, const ScoreNote& sn) {
    return sn.truth_index >= 0 ? truth[size_t(sn.truth_index)].onset_ms
                               : std::numeric_limits<double>::quiet_NaN();
}

int sounding_at(const std::vector<TruthNote>& truth, double at) {
    int sounding = 0;
    for (const auto& g : truth)
        if (g.onset_ms <= at && at < g.onset_ms + g.dur_ms) ++sounding;
    return sounding;
}

// The dump's index.csv (work package 4c): one row per written dump, in
// the order the excerpts and cases ran.
bool write_dump_index(const std::string& dir, const std::vector<std::string>& rows) {
    const std::string path = dir + "/index.csv";
    std::FILE* f = std::fopen(path.c_str(), "w");
    if (f == nullptr) return false;
    std::fprintf(f, "e,excerpt,case,frames,notes\n");
    for (const auto& r : rows) std::fwrite(r.data(), 1, r.size(), f);
    return std::fclose(f) == 0;
}

// One features row (deliverable 2): the measurement of the bp path plus
// the truth columns only the bench can fill. The contour columns are
// empty when they could not be measured (c_cents when its weight is
// under 0.05).
void write_feature_row(std::string& out, const char* excerpt, const char* case_name, size_t index,
                       const ScoreNote& sn, double truth_onset_ms, int sounding,
                       const bp::NoteMeasure& m) {
    char truth_s[32] = "na", atk_s[32] = "na", rival_s[32] = "na", before_s[32] = "na",
         after_s[32] = "na";
    if (std::isfinite(truth_onset_ms)) std::snprintf(truth_s, sizeof(truth_s), "%.3f", truth_onset_ms);
    if (std::isfinite(m.attack_ms)) std::snprintf(atk_s, sizeof(atk_s), "%.3f", m.attack_ms);
    if (std::isfinite(m.rival_midi)) std::snprintf(rival_s, sizeof(rival_s), "%d", int(m.rival_midi));
    if (std::isfinite(m.n_before)) std::snprintf(before_s, sizeof(before_s), "%.6f", double(m.n_before));
    if (std::isfinite(m.n_after)) std::snprintf(after_s, sizeof(after_s), "%.6f", double(m.n_after));
    auto fmt6 = [](float v, char* buf, size_t cap) {
        if (std::isfinite(v)) std::snprintf(buf, cap, "%.6f", double(v));
        else buf[0] = '\0';
    };
    auto fmt4 = [](float v, char* buf, size_t cap) {
        if (std::isfinite(v)) std::snprintf(buf, cap, "%.4f", double(v));
        else buf[0] = '\0';
    };
    char c_own[32], c_up1[32], c_dn1[32], c_up2[32], c_dn2[32], c_cents[32];
    fmt6(m.c_own, c_own, sizeof(c_own));
    fmt6(m.c_up1, c_up1, sizeof(c_up1));
    fmt6(m.c_dn1, c_dn1, sizeof(c_dn1));
    fmt6(m.c_up2, c_up2, sizeof(c_up2));
    fmt6(m.c_dn2, c_dn2, sizeof(c_dn2));
    fmt4(m.c_cents, c_cents, sizeof(c_cents));
    char line[2048];
    const int len = std::snprintf(line, sizeof(line),
                                  "%s,%s,%zu,%d,%d,%d,%.3f,%s,%s,%.6f,%.6f,%s,%s,%s,%s,%s,%s,%s,%s,%s\n",
                                  excerpt, case_name, index, sn.right ? 1 : 0, sounding, sn.midi,
                                  sn.onset_ms, truth_s, atk_s, double(m.s), double(m.r), rival_s,
                                  before_s, after_s, c_own, c_up1, c_dn1, c_up2, c_dn2, c_cents);
    out.append(line, size_t(std::max(0, std::min(len, int(sizeof(line)) - 1))));
}

// ---- the bp sweep (deliverable 3) -----------------------------------------

constexpr size_t kSweepThCount = 7;
const double kSweepTh[kSweepThCount] = {0.05, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6};
constexpr size_t kSweepLines = 2 * kSweepThCount; // plain lines, then rival lines

bp::Rule sweep_rule(size_t line) {
    return line < kSweepThCount ? bp::Rule::Plain : bp::Rule::Rival;
}

double sweep_th(size_t line) { return kSweepTh[line % kSweepThCount]; }

size_t case_index(Case c) {
    for (size_t i = 0; i < kCaseCount; ++i)
        if (kAllCases[i] == c) return i;
    return 0;
}

// One sweep line, all on up to 10 sounding: right accepted % (case
// right), wrong rejected % of semitone, tone, octave and added, right
// accepted % of left_out, late250 and jitter60, and the attack error
// (median, p95, % in the perfect window, % within 50 ms) of right and
// late250.
void print_sweep_line(FILE* f, size_t line, const CaseStats* st) {
    auto acc = [&](Case c) {
        const BucketStats& b = st[case_index(c)].up_to_10;
        char buf[16];
        pct_str(double(b.accepted), double(b.right_n), buf, sizeof(buf));
        return std::string(buf);
    };
    auto rej = [&](Case c) {
        const BucketStats& b = st[case_index(c)].up_to_10;
        char buf[16];
        pct_str(double(b.rejected), double(b.wrong_n), buf, sizeof(buf));
        return std::string(buf);
    };
    const TimeErr rt = time_error(st[case_index(Case::Right)].up_to_10);
    const TimeErr lt = time_error(st[case_index(Case::Late250)].up_to_10);
    auto tcol = [](const TimeErr& e, char* buf, size_t cap) {
        if (!e.has) {
            std::snprintf(buf, cap, "%8s %7s %7s %7s", "n/a", "n/a", "n/a", "n/a");
        } else {
            std::snprintf(buf, cap, "%8.1f %7.1f %7.1f %7.1f", e.median_ms, e.p95_abs_ms,
                          e.in_perfect_pct, e.in_50ms_pct);
        }
    };
    char rt_s[64], lt_s[64];
    tcol(rt, rt_s, sizeof(rt_s));
    tcol(lt, lt_s, sizeof(lt_s));
    std::fprintf(f, "%6s %5.2f %7s %7s %7s %7s %7s %7s %8s %7s | %s | %s\n", rule_name(sweep_rule(line)),
                 sweep_th(line), acc(Case::Right).c_str(), rej(Case::Semitone).c_str(),
                 rej(Case::Tone).c_str(), rej(Case::Octave).c_str(), rej(Case::Added).c_str(),
                 acc(Case::LeftOut).c_str(), acc(Case::Late250).c_str(), acc(Case::Jitter60).c_str(),
                 rt_s, lt_s);
}

void print_sweep_header(FILE* f) {
    std::fprintf(f, "%6s %5s %7s %7s %7s %7s %7s %7s %8s %7s | %8s %7s %7s %7s | %8s %7s %7s %7s\n",
                 "rule", "th", "right%", "semi%", "tone%", "octv%", "added%", "left%", "late250%",
                 "jitter%", "r med", "r p95", "r perf%", "r 50ms%", "l med", "l p95", "l perf%",
                 "l 50ms%");
}

} // namespace

int main(int argc, char** argv) {
    std::string maestro, manifest, audio_path, truth_path, paths_arg = "blind,core",
                cases_arg = "all", out_dir, dump_audio, dump_profile, make_manifests_csv,
                features_path, dump_posteriors_dir, bp_rule_arg = "fair",
                pair_claim_arg = "closeness";
    double from_s = 0.0, to_s = -1.0, bp_th = 0.3, bp_lookahead_ms = -1.0;
    bp::FairParams bp_fair;
    pair::PairParams pair_pp;
    unsigned seed = 7;
    int repeat = 1, jobs = 1;
    bool bp_sweep = false;

    int i = 0;
    bool bad = false;
    auto value = [&](const char* k) -> std::string {
        if (i + 1 >= argc) {
            std::fprintf(stderr, "missing value for %s\n", k);
            bad = true;
            return std::string();
        }
        return argv[++i];
    };
    for (i = 1; i < argc && !bad; ++i) {
        const std::string k = argv[i];
        if (k == "--maestro") maestro = value("--maestro");
        else if (k == "--manifest") manifest = value("--manifest");
        else if (k == "--audio") audio_path = value("--audio");
        else if (k == "--truth") truth_path = value("--truth");
        else if (k == "--from") from_s = std::atof(value("--from").c_str());
        else if (k == "--to") to_s = std::atof(value("--to").c_str());
        else if (k == "--paths") paths_arg = value("--paths");
        else if (k == "--cases") cases_arg = value("--cases");
        else if (k == "--seed") seed = unsigned(std::atoi(value("--seed").c_str()));
        else if (k == "--repeat") repeat = std::max(1, std::atoi(value("--repeat").c_str()));
        else if (k == "--out") out_dir = value("--out");
        else if (k == "--features") features_path = value("--features");
        else if (k == "--dump-posteriors") dump_posteriors_dir = value("--dump-posteriors");
        else if (k == "--bp-th") bp_th = std::atof(value("--bp-th").c_str());
        else if (k == "--bp-rule") bp_rule_arg = value("--bp-rule");
        else if (k == "--bp-fair-a") bp_fair.a = std::atof(value("--bp-fair-a").c_str());
        else if (k == "--bp-fair-b") bp_fair.b = std::atof(value("--bp-fair-b").c_str());
        else if (k == "--bp-fair-c") bp_fair.c = std::atof(value("--bp-fair-c").c_str());
        else if (k == "--bp-lookahead") bp_lookahead_ms = std::atof(value("--bp-lookahead").c_str());
        else if (k == "--pair-u") pair_pp.u = std::atof(value("--pair-u").c_str());
        else if (k == "--pair-un") pair_pp.un = std::atof(value("--pair-un").c_str());
        else if (k == "--pair-c") pair_pp.c = std::atof(value("--pair-c").c_str());
        else if (k == "--pair-a") pair_pp.a = std::atof(value("--pair-a").c_str());
        else if (k == "--pair-b") pair_pp.b = std::atof(value("--pair-b").c_str());
        else if (k == "--pair-peak-min") pair_pp.peak_min = std::atof(value("--pair-peak-min").c_str());
        else if (k == "--pair-claim") pair_claim_arg = value("--pair-claim");
        else if (k == "--pair-off-k") pair_pp.off_k = std::max(0, std::atoi(value("--pair-off-k").c_str()));
        else if (k == "--pair-off-min") pair_pp.off_min = std::atof(value("--pair-off-min").c_str());
        else if (k == "--bp-sweep") bp_sweep = true;
        else if (k == "--jobs") jobs = std::max(1, std::min(6, std::atoi(value("--jobs").c_str())));
        else if (k == "--dump-events") dump_audio = value("--dump-events");
        else if (k == "--profile") dump_profile = value("--profile");
        else if (k == "--make-manifests") make_manifests_csv = value("--make-manifests");
        else {
            std::fprintf(stderr, "unknown option %s\n", k.c_str());
            bad = true;
        }
    }
    if (bad) return 2;

    bp::Rule bp_rule;
    if (!bp::rule_from_name(bp_rule_arg, bp_rule)) {
        std::fprintf(stderr, "unknown rule %s (fair, plain or rival)\n", bp_rule_arg.c_str());
        return 2;
    }

    pair::Claim pair_claim;
    if (!pair::claim_from_name(pair_claim_arg, pair_claim)) {
        std::fprintf(stderr, "unknown pair claim %s (closeness or strongest)\n", pair_claim_arg.c_str());
        return 2;
    }
    pair_pp.claim = pair_claim;

    if (!make_manifests_csv.empty()) return make_manifests(make_manifests_csv);

    if (!dump_audio.empty()) {
        // Nothing else is printed: the dump is compared byte for byte.
        std::vector<float> sig;
        if (!bench_io::read_audio(dump_audio, sig)) {
            std::fprintf(stderr, "cannot decode audio %s\n", dump_audio.c_str());
            return 1;
        }
        const std::string dump = dump_live_events(sig, 48000.0f, dump_profile.c_str());
        std::fwrite(dump.data(), 1, dump.size(), stdout);
        return 0;
    }

    // ---- the paths under test ---------------------------------------------
    std::vector<std::string> path_names;
    if (!bp_sweep) {
        for (size_t a = 0;;) {
            const size_t b = paths_arg.find(',', a);
            const std::string name = paths_arg.substr(a, b == std::string::npos ? b : b - a);
            if (name != "blind" && name != "expect" && name != "core" && name != "oracle" &&
                name != "yes" && name != "bp" && name != "bp-la" && name != "pair") {
                std::fprintf(stderr,
                             "unknown path %s (blind, expect, core, oracle, yes, bp, bp-la, pair)\n",
                             name.c_str());
                return 2;
            }
            if (name == "bp-la" && bp_lookahead_ms <= 0.0) {
                std::fprintf(stderr, "the bp-la path needs --bp-lookahead <ms>\n");
                return 2;
            }
            path_names.push_back(name);
            if (b == std::string::npos) break;
            a = b + 1;
        }
    }

    std::vector<Case> cases;
    if (cases_arg == "all") {
        // The default list: every case but the detuned controls, which
        // run only when named.
        for (size_t ci = 0; ci < kDefaultCaseCount; ++ci) cases.push_back(kDefaultCases[ci]);
    } else {
        for (size_t a = 0;;) {
            const size_t b = cases_arg.find(',', a);
            Case c;
            const std::string name = cases_arg.substr(a, b == std::string::npos ? b : b - a);
            if (!case_from_name(name.c_str(), c)) {
                std::fprintf(stderr, "unknown case %s\n", name.c_str());
                return 2;
            }
            cases.push_back(c);
            if (b == std::string::npos) break;
            a = b + 1;
        }
    }
    auto selected = [&](Case c) {
        for (Case s : cases)
            if (s == c) return true;
        return false;
    };

    // ---- the excerpts ------------------------------------------------------
    std::vector<Excerpt> excerpts;
    if (!manifest.empty()) {
        const std::string base = maestro.empty() ? "." : maestro;
        if (!read_manifest(manifest, excerpts)) {
            std::fprintf(stderr, "cannot read manifest %s\n", manifest.c_str());
            return 1;
        }
        for (auto& e : excerpts) {
            e.audio = base + "/" + e.audio;
            e.midi = base + "/" + e.midi;
        }
    } else if (!audio_path.empty() && !truth_path.empty()) {
        Excerpt e;
        e.audio = audio_path;
        e.midi = truth_path;
        e.from_s = from_s;
        e.to_s = to_s > 0 ? to_s : from_s + 1e6;
        excerpts.push_back(e);
    } else {
        std::fprintf(stderr,
                     "usage: kan_truth_bench --maestro <dir> --manifest <file> | "
                     "--audio a.wav --truth a.midi --from 0 --to 60 "
                     "[--paths blind,core] [--cases all] [--seed N] [--repeat N] [--out <dir>] "
                     "[--bp-th 0.3] [--bp-rule fair] [--bp-lookahead <ms>] [--features <file.csv>] "
                     "[--pair-u 0.3 --pair-un 0.15 --pair-c 0.3 --pair-a 0.13 --pair-b 0.25] "
                     "[--pair-claim closeness|strongest] [--pair-off-k 15 --pair-off-min 0.3] "
                     "[--dump-posteriors <dir>] [--bp-sweep] [--jobs N]\n");
        return 2;
    }

    // One excerpt: the audio and the truth in its time. A note needs 500
    // ms past its attack to be judged fairly (polybench's cut of the
    // excerpt tail), so the truth stops there.
    struct Loaded {
        std::vector<float> audio;
        std::vector<TruthNote> truth;
        double audio_ms = 0.0;
        std::string msg;
        int status = 0; // 0 ok, 1 skip (no truth notes), 2 fatal
    };
    auto load_excerpt = [](const Excerpt& ex) {
        Loaded ld;
        std::fprintf(stderr, "excerpt %s %.3g-%.3g s\n", ex.audio.c_str(), ex.from_s, ex.to_s);
        std::vector<float> full;
        if (!bench_io::read_audio(ex.audio, full)) {
            ld.status = 2;
            ld.msg = "cannot decode audio " + ex.audio;
            return ld;
        }
        std::vector<bench_io::Note> midi;
        if (!bench_io::read_midi(ex.midi, midi)) {
            ld.status = 2;
            ld.msg = "cannot read MIDI " + ex.midi;
            return ld;
        }
        const double from_ms = ex.from_s * 1000.0, to_ms = ex.to_s * 1000.0;
        const size_t s0 = size_t(from_ms * 48.0), s1 = std::min(full.size(), size_t(to_ms * 48.0));
        if (s0 >= s1) {
            ld.status = 2;
            ld.msg = "empty excerpt range " + ex.audio;
            return ld;
        }
        ld.audio.assign(full.begin() + std::ptrdiff_t(s0), full.begin() + std::ptrdiff_t(s1));
        ld.audio_ms = 1000.0 * double(ld.audio.size()) / 48000.0;
        for (const auto& n : midi) {
            if (n.onset_ms < from_ms || n.onset_ms >= to_ms - 500.0) continue;
            ld.truth.push_back({n.onset_ms - from_ms, n.dur_ms, n.midi});
        }
        if (ld.truth.empty()) {
            ld.status = 1;
            ld.msg = "no truth notes in range " + ex.audio;
        }
        return ld;
    };

    if (!out_dir.empty()) std::filesystem::create_directories(out_dir);
    if (!dump_posteriors_dir.empty()) std::filesystem::create_directories(dump_posteriors_dir);
    const std::string summary_path = out_dir.empty() ? "summary.txt" : out_dir + "/summary.txt";

    // ---- the bp sweep ------------------------------------------------------
    if (bp_sweep) {
        struct SweepOut {
            int status = 0;
            std::string msg, features;
            std::vector<CaseStats> stats; // kSweepLines x kCaseCount
            double infer_ms = 0.0, audio_ms = 0.0;
            std::vector<std::string> dump_rows; // index.csv lines (work package 4c)
            std::string dump_err;
        };
        std::vector<SweepOut> outs(excerpts.size());
        std::atomic<size_t> next{0};
        std::vector<std::thread> pool;
        for (int j = 0; j < jobs; ++j) {
            pool.emplace_back([&]() {
                for (size_t idx; (idx = next.fetch_add(1)) < excerpts.size();) {
                    SweepOut& out = outs[idx];
                    const Loaded ld = load_excerpt(excerpts[idx]);
                    out.status = ld.status;
                    out.msg = ld.msg;
                    if (ld.status == 2) continue;
                    out.audio_ms = ld.audio_ms;
                    out.stats.assign(kSweepLines * kCaseCount, CaseStats());
                    bp::BpPath path; // one measurement per score note, no verdict here
                    CaseAudio case_audio(ld.audio, 48000.0f);
                    char label[768];
                    std::snprintf(label, sizeof(label), "%s@%g-%g", excerpts[idx].audio.c_str(),
                                  excerpts[idx].from_s, excerpts[idx].to_s);
                    for (size_t ci = 0; ci < kCaseCount; ++ci) {
                        const Case c = kAllCases[ci];
                        if (!selected(c)) continue;
                        const BuiltCase built = build_case(c, ld.truth, seed);
                        std::vector<NoteResult> res;
                        CpuStats cpu;
                        path.run(case_audio.of(c), 48000.0f, built.notes, res, cpu);
                        if (out.infer_ms <= 0.0) out.infer_ms = cpu.total_ms;
                        const std::vector<bp::NoteMeasure>& measures = path.measures();
                        if (!dump_posteriors_dir.empty()) {
                            // The whole-recording posteriors of this run
                            // and the score it was given (work package
                            // 4c); the analysis columns come from the
                            // truth the bench holds.
                            std::vector<bp::DumpScoreRow> rows = bp::dump_score_rows(built.notes);
                            for (size_t n = 0; n < built.notes.size(); ++n) {
                                const ScoreNote& sn = built.notes[n];
                                const double truth_onset = truth_onset_of(ld.truth, sn);
                                rows[n].right = sn.right ? 1 : 0;
                                rows[n].sounding = sounding_at(
                                    ld.truth, sn.truth_index >= 0 ? truth_onset : sn.onset_ms);
                                rows[n].truth_onset_ms = truth_onset;
                            }
                            const bp::PosteriorView& pv = path.posteriors();
                            if (!bp::write_posterior_dump(dump_posteriors_dir, int(idx), case_name(c),
                                                         pv, rows)) {
                                out.dump_err = "cannot write the posterior dump of " +
                                               std::string(label) + " " + case_name(c);
                            } else {
                                char line[2048];
                                std::snprintf(line, sizeof(line), "%02d,%s,%s,%d,%zu\n", int(idx),
                                              label, case_name(c), pv.frames, rows.size());
                                out.dump_rows.push_back(line);
                            }
                        }
                        for (size_t n = 0; n < built.notes.size(); ++n) {
                            const ScoreNote& sn = built.notes[n];
                            const double truth_onset = truth_onset_of(ld.truth, sn);
                            const double at = sn.truth_index >= 0 ? truth_onset : sn.onset_ms;
                            write_feature_row(out.features, label, case_name(c), n, sn, truth_onset,
                                              sounding_at(ld.truth, at), measures[n]);
                        }
                        for (size_t line = 0; line < kSweepLines; ++line) {
                            std::vector<NoteResult> dec(built.notes.size());
                            for (size_t n = 0; n < built.notes.size(); ++n)
                                dec[n] = bp::decide(measures[n], sweep_rule(line), sweep_th(line),
                                                    built.notes[n].midi);
                            merge(out.stats[line * kCaseCount + ci],
                                  score_case(ld.truth, built.notes, dec));
                        }
                    }
                }
            });
        }
        for (auto& t : pool) t.join();

        std::vector<CaseStats> totals(kSweepLines * kCaseCount);
        double infer_ms = 0.0, audio_ms = 0.0;
        bool fatal = false;
        std::string features;
        std::vector<std::string> dump_index;
        for (size_t idx = 0; idx < excerpts.size(); ++idx) {
            SweepOut& out = outs[idx];
            if (out.status == 2) {
                std::fprintf(stderr, "%s\n", out.msg.c_str());
                fatal = true;
                continue;
            }
            if (out.status == 1) {
                std::fprintf(stderr, "%s\n", out.msg.c_str());
                continue;
            }
            features += out.features;
            if (!out.dump_err.empty()) {
                std::fprintf(stderr, "%s\n", out.dump_err.c_str());
                fatal = true;
            }
            dump_index.insert(dump_index.end(), out.dump_rows.begin(), out.dump_rows.end());
            infer_ms += out.infer_ms;
            audio_ms += out.audio_ms;
            for (size_t line = 0; line < kSweepLines; ++line)
                for (size_t ci = 0; ci < kCaseCount; ++ci)
                    merge(totals[line * kCaseCount + ci], out.stats[line * kCaseCount + ci]);
        }
        if (!dump_posteriors_dir.empty() && !write_dump_index(dump_posteriors_dir, dump_index)) {
            std::fprintf(stderr, "cannot write %s/index.csv\n", dump_posteriors_dir.c_str());
            return 1;
        }
        if (fatal) return 1;
        if (!features_path.empty()) {
            std::FILE* feat = std::fopen(features_path.c_str(), "w");
            if (feat == nullptr) {
                std::fprintf(stderr, "cannot write %s\n", features_path.c_str());
                return 1;
            }
            std::fprintf(feat, "excerpt,case,note,right,sounding,midi,score_onset_ms,truth_onset_ms,"
                               "measured_attack_ms,s,r,rival_midi,n_before,n_after,"
                               "c_own,c_up1,c_dn1,c_up2,c_dn2,c_cents\n");
            std::fwrite(features.data(), 1, features.size(), feat);
            std::fclose(feat);
            std::fprintf(stderr, "wrote %s\n", features_path.c_str());
        }

        auto print_sweep = [&](FILE* f) {
            std::fprintf(f, "kan_truth_bench --bp-sweep\n");
            std::fprintf(f, "excerpts: %zu   cases: %s   seed %u   rule plain then rival\n",
                         excerpts.size(), cases_arg.c_str(), seed);
            std::fprintf(f, "every column is up to 10 sounding: right%% is right notes accepted "
                            "(case right),\nsemi%%/tone%%/octv%%/added%% wrong notes rejected, left%%/"
                            "late250%%/jitter%% right notes\naccepted; r and l are the attack error "
                            "of right and late250 (med and p95 in ms,\nperf%% and 50ms%% the shares "
                            "inside the perfect window and within 50 ms)\n");
            print_sweep_header(f);
            for (size_t line = 0; line < kSweepLines; ++line)
                print_sweep_line(f, line, totals.data() + line * kCaseCount);
            // The setting the report runs with: the mean of right accepted
            // % (case right) and the mean wrong rejected % (semitone,
            // tone, octave, added), highest wins, ties go to the higher
            // threshold and then to the rival rule.
            double best_score = -1.0;
            size_t best_line = 0;
            for (size_t line = 0; line < kSweepLines; ++line) {
                const std::vector<CaseStats> st(totals.begin() + std::ptrdiff_t(line * kCaseCount),
                                                totals.begin() + std::ptrdiff_t((line + 1) * kCaseCount));
                const BucketStats& right = st[case_index(Case::Right)].up_to_10;
                if (right.right_n == 0) continue;
                double wrong_sum = 0.0;
                int wrong_cases = 0;
                for (Case c : {Case::Semitone, Case::Tone, Case::Octave, Case::Added}) {
                    const BucketStats& b = st[case_index(c)].up_to_10;
                    if (b.wrong_n == 0) continue;
                    wrong_sum += pct(double(b.rejected), double(b.wrong_n));
                    ++wrong_cases;
                }
                const double score = 0.5 * (pct(double(right.accepted), double(right.right_n)) +
                                            (wrong_cases > 0 ? wrong_sum / wrong_cases : 0.0));
                if (score > best_score) {
                    best_score = score;
                    best_line = line;
                }
            }
            std::fprintf(f, "best (rule, th) by the mean of right accepted %% and wrong rejected %%: "
                            "%s %.2f\n",
                         rule_name(sweep_rule(best_line)), sweep_th(best_line));
            std::fprintf(f, "CPU of the offline network (infer, one thread): %.1f %% of real time "
                            "(%.1f s over %.1f s of audio)\n",
                         100.0 * infer_ms / std::max(1.0, audio_ms), infer_ms / 1000.0, audio_ms / 1000.0);
            if (jobs > 1)
                std::fprintf(f, "note: the excerpts ran in parallel (--jobs %d), the infer times "
                                "include that load\n", jobs);
        };
        print_sweep(stdout);
        std::FILE* sum = std::fopen(summary_path.c_str(), "w");
        if (sum == nullptr) {
            std::fprintf(stderr, "cannot write %s\n", summary_path.c_str());
            return 1;
        }
        print_sweep(sum);
        std::fclose(sum);
        std::fprintf(stderr, "wrote %s\n", summary_path.c_str());
        return 0;
    }

    const std::string csv_path = out_dir.empty() ? "notes.csv" : out_dir + "/notes.csv";
    std::FILE* csv = std::fopen(csv_path.c_str(), "w");
    if (csv == nullptr) {
        std::fprintf(stderr, "cannot write %s\n", csv_path.c_str());
        return 1;
    }
    std::fprintf(csv, "excerpt,path,case,outcome,right,sounding,score_onset_ms,truth_onset_ms,"
                      "measured_onset_ms,midi,reported_midi\n");

    // The features files (one row per excerpt, case and score note): the
    // pair path's rows take --features and its own columns; when the bp
    // path also runs, its rows go to a sibling file with .bp before .csv.
    bool has_bp = false, has_pair = false;
    for (const auto& name : path_names) {
        has_bp = has_bp || name == "bp";
        has_pair = has_pair || name == "pair";
    }
    std::string bp_features_path, pair_features_path;
    if (!features_path.empty()) {
        if (has_pair) {
            pair_features_path = features_path;
            if (has_bp) {
                const bool is_csv = features_path.size() > 4 &&
                                    features_path.compare(features_path.size() - 4, 4, ".csv") == 0;
                bp_features_path = is_csv ? features_path.substr(0, features_path.size() - 4) +
                                                ".bp.csv"
                                          : features_path + ".bp.csv";
                std::fprintf(stderr, "note: the pair rows go to %s, the bp rows to %s\n",
                             features_path.c_str(), bp_features_path.c_str());
            }
        } else if (has_bp) {
            bp_features_path = features_path;
        } else {
            std::fprintf(stderr, "note: --features writes rows only for the bp and pair paths, which "
                                 "are not among the paths\n");
        }
    }
    std::FILE* feat = nullptr;
    if (!bp_features_path.empty()) {
        feat = std::fopen(bp_features_path.c_str(), "w");
        if (feat == nullptr) {
            std::fprintf(stderr, "cannot write %s\n", bp_features_path.c_str());
            return 1;
        }
        std::fprintf(feat, "excerpt,case,note,right,sounding,midi,score_onset_ms,truth_onset_ms,"
                           "measured_attack_ms,s,r,rival_midi,n_before,n_after,"
                           "c_own,c_up1,c_dn1,c_up2,c_dn2,c_cents\n");
    }
    std::FILE* pfeat = nullptr;
    if (!pair_features_path.empty()) {
        pfeat = std::fopen(pair_features_path.c_str(), "w");
        if (pfeat == nullptr) {
            std::fprintf(stderr, "cannot write %s\n", pair_features_path.c_str());
            return 1;
        }
        std::fprintf(pfeat, "%s", pair::kPairFeatureHeader);
    }
    if (!dump_posteriors_dir.empty()) {
        if (!has_bp)
            std::fprintf(stderr, "note: --dump-posteriors writes dumps only for the bp path, which "
                                 "is not among the paths\n");
    }

    // Reports pooled over the excerpts, one per path and case.
    struct Pool {
        CaseStats stats;
        long sets = 0, refused = 0, late = 0; // expect, summed over excerpts
    };
    std::vector<std::vector<Pool>> pools(path_names.size(), std::vector<Pool>(kCaseCount));
    // CPU of the right case, one point per repeat and excerpt.
    std::vector<std::vector<CpuPoint>> cpu_points(path_names.size());

    // ---- the excerpts, in parallel ----------------------------------------
    struct ExcerptOut {
        int status = 0;
        std::string msg, csv, features, pair_features;
        std::vector<std::vector<Pool>> pools; // path x case
        std::vector<std::vector<CpuPoint>> cpu;
        std::vector<std::string> dump_rows; // index.csv lines (work package 4c)
        std::string dump_err;
    };
    std::vector<ExcerptOut> outs(excerpts.size());
    {
        std::atomic<size_t> next{0};
        std::vector<std::thread> pool;
        for (int j = 0; j < jobs; ++j) {
            pool.emplace_back([&]() {
                for (size_t idx; (idx = next.fetch_add(1)) < excerpts.size();) {
                    ExcerptOut& out = outs[idx];
                    const Excerpt& ex = excerpts[idx];
                    const Loaded ld = load_excerpt(ex);
                    out.status = ld.status;
                    out.msg = ld.msg;
                    if (ld.status == 2) continue;
                    out.pools.assign(path_names.size(), std::vector<Pool>(kCaseCount));
                    out.cpu.resize(path_names.size());

                    // Fresh path instances per excerpt (the blind pass is
                    // cached per audio buffer; the oracle gets the truth
                    // here and only here).
                    std::vector<std::unique_ptr<Path>> run_paths;
                    for (const auto& name : path_names)
                        run_paths.push_back(make_path(name, ld.truth, bp_th, bp_rule, bp_lookahead_ms,
                                                      bp_fair, pair_pp));
                    CaseAudio case_audio(ld.audio, 48000.0f);

                    const char* label_base = ex.audio.c_str();
                    for (size_t ci = 0; ci < kCaseCount; ++ci) {
                        const Case c = kAllCases[ci];
                        if (!selected(c)) continue;
                        const BuiltCase built = build_case(c, ld.truth, seed);

                        for (size_t pi = 0; pi < run_paths.size(); ++pi) {
                            CpuStats cpu;
                            std::vector<NoteResult> results;
                            run_paths[pi]->run(case_audio.of(c), 48000.0f, built.notes, results, cpu);
                            annotate_cpu(cpu, ld.truth);
                            merge(out.pools[pi][ci].stats, score_case(ld.truth, built.notes, results));
                            if (path_names[pi] == "expect") {
                                const ExpectPath* ep = dynamic_cast<const ExpectPath*>(run_paths[pi].get());
                                out.pools[pi][ci].sets += ep->last_sets;
                                out.pools[pi][ci].refused += ep->last_refused;
                                out.pools[pi][ci].late += ep->last_late;
                            }
                            const bp::BpPath* bpp =
                                features_path.empty() || path_names[pi] != "bp"
                                    ? nullptr
                                    : dynamic_cast<const bp::BpPath*>(run_paths[pi].get());
                            const pair::PairPath* prp =
                                features_path.empty() || path_names[pi] != "pair"
                                    ? nullptr
                                    : dynamic_cast<const pair::PairPath*>(run_paths[pi].get());

                            for (size_t n = 0; n < built.notes.size(); ++n) {
                                const ScoreNote& sn = built.notes[n];
                                const NoteResult& r = results[n];
                                const double truth_onset = truth_onset_of(ld.truth, sn);
                                const int sounding =
                                    sounding_at(ld.truth, sn.truth_index >= 0 ? truth_onset : sn.onset_ms);
                                char label[768];
                                std::snprintf(label, sizeof(label), "%s@%g-%g", label_base, ex.from_s,
                                              ex.to_s);
                                char truth_s[32] = "na", meas_s[32] = "na", rep_s[32] = "na";
                                if (std::isfinite(truth_onset))
                                    std::snprintf(truth_s, sizeof(truth_s), "%.3f", truth_onset);
                                if (std::isfinite(r.onset_ms))
                                    std::snprintf(meas_s, sizeof(meas_s), "%.3f", r.onset_ms);
                                if (std::isfinite(r.midi))
                                    std::snprintf(rep_s, sizeof(rep_s), "%.2f", double(r.midi));
                                char line[2048];
                                const int len = std::snprintf(
                                    line, sizeof(line), "%s,%s,%s,%s,%d,%d,%.3f,%s,%s,%d,%s\n", label,
                                    path_names[pi].c_str(), case_name(c), outcome_name(r.outcome),
                                    sn.right ? 1 : 0, sounding, sn.onset_ms, truth_s, meas_s, sn.midi,
                                    rep_s);
                                out.csv.append(line, size_t(std::max(0, std::min(len, int(sizeof(line)) - 1))));
                                if (bpp != nullptr)
                                    write_feature_row(out.features, label, case_name(c), n, sn,
                                                      truth_onset, sounding, bpp->measures()[n]);
                                if (prp != nullptr)
                                    pair::write_pair_feature_row(out.pair_features, label,
                                                                 case_name(c), n, sn, truth_onset,
                                                                 sounding, prp->measures()[n]);
                            }

                            // The dump of work package 4c (the bp path
                            // only): the whole-recording posteriors of
                            // this run and the score it was given. The
                            // analysis columns come from the truth the
                            // bench holds; the path never reads them.
                            if (!dump_posteriors_dir.empty() && path_names[pi] == "bp") {
                                const bp::BpPath* bpd =
                                    dynamic_cast<const bp::BpPath*>(run_paths[pi].get());
                                std::vector<bp::DumpScoreRow> rows = bp::dump_score_rows(built.notes);
                                for (size_t n = 0; n < built.notes.size(); ++n) {
                                    const ScoreNote& sn = built.notes[n];
                                    const double truth_onset = truth_onset_of(ld.truth, sn);
                                    rows[n].right = sn.right ? 1 : 0;
                                    rows[n].sounding = sounding_at(
                                        ld.truth, sn.truth_index >= 0 ? truth_onset : sn.onset_ms);
                                    rows[n].truth_onset_ms = truth_onset;
                                }
                                char label[768];
                                std::snprintf(label, sizeof(label), "%s@%g-%g", label_base, ex.from_s,
                                              ex.to_s);
                                const bp::PosteriorView& pv = bpd->posteriors();
                                if (!bp::write_posterior_dump(dump_posteriors_dir, int(idx),
                                                             case_name(c), pv, rows)) {
                                    out.dump_err = "cannot write the posterior dump of " +
                                                   std::string(label) + " " + case_name(c);
                                } else {
                                    char line[2048];
                                    std::snprintf(line, sizeof(line), "%02d,%s,%s,%d,%zu\n", int(idx),
                                                  label, case_name(c), pv.frames, rows.size());
                                    out.dump_rows.push_back(line);
                                }
                            }
                        }
                    }

                    // CPU measuring pass of the right case, repeated. Only
                    // with one thread and only for paths that report CPU.
                    if (jobs <= 1) {
                        const BuiltCase right = build_case(Case::Right, ld.truth, seed);
                        for (size_t pi = 0; pi < path_names.size(); ++pi) {
                            if (!make_path(path_names[pi], ld.truth, bp_th, bp_rule, bp_lookahead_ms,
                                           bp_fair, pair_pp)
                                     ->reports_cpu())
                                continue;
                            for (int rep = 0; rep < repeat; ++rep) {
                                std::unique_ptr<Path> p =
                                    make_path(path_names[pi], ld.truth, bp_th, bp_rule,
                                              bp_lookahead_ms, bp_fair, pair_pp);
                                CpuStats cpu;
                                std::vector<NoteResult> results;
                                p->run(ld.audio, 48000.0f, right.notes, results, cpu);
                                annotate_cpu(cpu, ld.truth);
                                out.cpu[pi].push_back(cpu_point(cpu, ld.audio_ms));
                            }
                        }
                    }
                }
            });
        }
        for (auto& t : pool) t.join();
    }

    bool fatal = false;
    std::vector<std::string> dump_index;
    for (size_t idx = 0; idx < excerpts.size(); ++idx) {
        ExcerptOut& out = outs[idx];
        if (out.status == 2) {
            std::fprintf(stderr, "%s\n", out.msg.c_str());
            fatal = true;
            continue;
        }
        if (out.status == 1) {
            std::fprintf(stderr, "%s\n", out.msg.c_str());
            continue;
        }
        std::fwrite(out.csv.data(), 1, out.csv.size(), csv);
        if (feat != nullptr) std::fwrite(out.features.data(), 1, out.features.size(), feat);
        if (pfeat != nullptr) std::fwrite(out.pair_features.data(), 1, out.pair_features.size(), pfeat);
        if (!out.dump_err.empty()) {
            std::fprintf(stderr, "%s\n", out.dump_err.c_str());
            fatal = true;
        }
        dump_index.insert(dump_index.end(), out.dump_rows.begin(), out.dump_rows.end());
        for (size_t pi = 0; pi < path_names.size(); ++pi) {
            for (size_t ci = 0; ci < kCaseCount; ++ci) {
                merge(pools[pi][ci].stats, out.pools[pi][ci].stats);
                pools[pi][ci].sets += out.pools[pi][ci].sets;
                pools[pi][ci].refused += out.pools[pi][ci].refused;
                pools[pi][ci].late += out.pools[pi][ci].late;
            }
            for (const auto& p : out.cpu[pi]) cpu_points[pi].push_back(p);
        }
    }
    std::fclose(csv);
    if (feat != nullptr) std::fclose(feat);
    if (pfeat != nullptr) std::fclose(pfeat);
    if (!dump_posteriors_dir.empty() && !write_dump_index(dump_posteriors_dir, dump_index)) {
        std::fprintf(stderr, "cannot write %s/index.csv\n", dump_posteriors_dir.c_str());
        return 1;
    }
    if (fatal) return 1;

    // ---- summary ----------------------------------------------------------
    auto print_summary = [&](FILE* f) {
        std::fprintf(f, "kan_truth_bench\n");
        std::fprintf(f, "excerpts: %zu   paths: %s   cases: %s   seed %u   repeat %d\n",
                     excerpts.size(), paths_arg.c_str(), cases_arg.c_str(), seed, repeat);
        std::fprintf(f,
                     "right = the truth note kept; wrong = changed or added; rejected = any outcome "
                     "but accepted\n");
        std::fprintf(f,
                     "time error = measured onset minus TRUTH onset of accepted right notes; n/a = "
                     "the path measures no time\n\n");

        bool has_detune = false;
        for (Case s : cases) has_detune = has_detune || case_detune_cents(s) != 0.0;
        if (has_detune)
            std::fprintf(f,
                         "detuned controls: the audio shifted up with libkan's pitch shifter at "
                         "equal in and out\ncounts, which keeps the duration (the shifter's group "
                         "delay is cut from the front and\npadded at the end); the score and the "
                         "truth times are unchanged\n\n");

        for (size_t pi = 0; pi < path_names.size(); ++pi) {
            for (size_t ci = 0; ci < kCaseCount; ++ci) {
                if (!selected(kAllCases[ci])) continue;
                const Pool& p = pools[pi][ci];
                std::fprintf(f, "== %s / %s ==\n", path_names[pi].c_str(), case_name(kAllCases[ci]));
                print_header(f);
                for (size_t b = 0; b < kBucketCount; ++b)
                    print_row(f, kBucketNames[b], p.stats.buckets[b]);
                print_row(f, "up to 10", p.stats.up_to_10);
                if (path_names[pi] == "expect") {
                    std::fprintf(f, "expect sets: %ld, refused %ld, too late to arm %ld\n", p.sets,
                                 p.refused, p.late);
                }
                std::fprintf(f, "\n");
            }
            std::fprintf(f, "== %s: every case at up to 10 ==\n", path_names[pi].c_str());
            print_header(f);
            for (size_t ci = 0; ci < kCaseCount; ++ci) {
                if (!selected(kAllCases[ci])) continue;
                print_row(f, case_name(kAllCases[ci]), pools[pi][ci].stats.up_to_10);
            }
            std::fprintf(f, "\n");
        }

        std::fprintf(f, "CPU of the right case (%d repeats per excerpt): share of real time %%, "
                        "per block median / p99 / worst ms\n", repeat);
        std::fprintf(f, "%6s %26s %26s %26s %26s\n", "path", "share %", "block med", "block p99",
                     "block worst");
        for (size_t pi = 0; pi < path_names.size(); ++pi) {
            std::vector<double> share, med, p99, worst;
            bool has_blocks = false;
            for (const auto& p : cpu_points[pi]) {
                share.push_back(p.share);
                if (p.has_blocks) {
                    has_blocks = true;
                    med.push_back(p.block_med);
                    p99.push_back(p.block_p99);
                    worst.push_back(p.block_worst);
                }
            }
            auto spread = [](const std::vector<double>& v, char* buf, size_t cap) {
                if (v.empty()) {
                    std::snprintf(buf, cap, "%26s", "n/a");
                    return;
                }
                std::snprintf(buf, cap, "%7.1f /%7.1f /%7.1f", *std::min_element(v.begin(), v.end()),
                              median_of(v), *std::max_element(v.begin(), v.end()));
            };
            char s[32], m[32], p[32], w[32];
            spread(share, s, sizeof(s));
            spread(med, m, sizeof(m));
            spread(p99, p, sizeof(p));
            spread(worst, w, sizeof(w));
            std::fprintf(f, "%6s %26s %26s %26s %26s\n", path_names[pi].c_str(), s, m, p, w);
            (void)has_blocks;
        }
        std::fprintf(f, "each line: min / median / max across the repeats\n");
        if (jobs > 1)
            std::fprintf(f, "note: CPU was not measured, the excerpts ran in parallel (--jobs %d).\n",
                         jobs);
        std::fprintf(f, "note: CPU numbers mean something only when run alone under taskset.\n");
    };

    print_summary(stdout);
    std::FILE* sum = std::fopen(summary_path.c_str(), "w");
    if (sum == nullptr) {
        std::fprintf(stderr, "cannot write %s\n", summary_path.c_str());
        return 1;
    }
    print_summary(sum);
    std::fclose(sum);
    std::fprintf(stderr, "wrote %s and %s\n", summary_path.c_str(), csv_path.c_str());
    return 0;
}
