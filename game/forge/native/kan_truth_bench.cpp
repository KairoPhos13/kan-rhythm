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
//   [--paths blind,expect,core,oracle,yes]  (default blind,core)
//   [--cases all | right,semitone,...] [--seed N] [--repeat N]
//   [--out <dir>]           writes summary.txt and notes.csv
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

#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <filesystem>
#include <memory>
#include <string>
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

std::unique_ptr<Path> make_path(const std::string& name, const std::vector<TruthNote>& truth) {
    if (name == "blind") return std::make_unique<BlindPath>();
    if (name == "expect") return std::make_unique<ExpectPath>();
    if (name == "core") return std::make_unique<CorePath>();
    if (name == "oracle") return std::make_unique<OraclePath>(truth);
    return std::make_unique<YesPath>();
}

} // namespace

int main(int argc, char** argv) {
    std::string maestro, manifest, audio_path, truth_path, paths_arg = "blind,core",
                cases_arg = "all", out_dir, dump_audio, dump_profile, make_manifests_csv;
    double from_s = 0.0, to_s = -1.0;
    unsigned seed = 7;
    int repeat = 1;

    for (int i = 1; i + 1 < argc; i += 2) {
        const std::string k = argv[i], v = argv[i + 1];
        if (k == "--maestro") maestro = v;
        else if (k == "--manifest") manifest = v;
        else if (k == "--audio") audio_path = v;
        else if (k == "--truth") truth_path = v;
        else if (k == "--from") from_s = std::atof(v.c_str());
        else if (k == "--to") to_s = std::atof(v.c_str());
        else if (k == "--paths") paths_arg = v;
        else if (k == "--cases") cases_arg = v;
        else if (k == "--seed") seed = unsigned(std::atoi(v.c_str()));
        else if (k == "--repeat") repeat = std::max(1, std::atoi(v.c_str()));
        else if (k == "--out") out_dir = v;
        else if (k == "--dump-events") dump_audio = v;
        else if (k == "--profile") dump_profile = v;
        else if (k == "--make-manifests") make_manifests_csv = v;
        else {
            std::fprintf(stderr, "unknown option %s\n", k.c_str());
            return 2;
        }
    }

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
    for (size_t a = 0;;) {
        const size_t b = paths_arg.find(',', a);
        const std::string name = paths_arg.substr(a, b == std::string::npos ? b : b - a);
        if (name != "blind" && name != "expect" && name != "core" && name != "oracle" &&
            name != "yes") {
            std::fprintf(stderr, "unknown path %s (blind, expect, core, oracle, yes)\n", name.c_str());
            return 2;
        }
        path_names.push_back(name);
        if (b == std::string::npos) break;
        a = b + 1;
    }

    std::vector<Case> cases;
    if (cases_arg == "all") {
        for (size_t i = 0; i < kCaseCount; ++i) cases.push_back(kAllCases[i]);
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
                     "[--paths blind,core] [--cases all] [--seed N] [--repeat N] [--out <dir>]\n");
        return 2;
    }

    if (!out_dir.empty()) std::filesystem::create_directories(out_dir);
    const std::string csv_path = out_dir.empty() ? "notes.csv" : out_dir + "/notes.csv";
    const std::string summary_path = out_dir.empty() ? "summary.txt" : out_dir + "/summary.txt";
    std::FILE* csv = std::fopen(csv_path.c_str(), "w");
    if (csv == nullptr) {
        std::fprintf(stderr, "cannot write %s\n", csv_path.c_str());
        return 1;
    }
    std::fprintf(csv, "excerpt,path,case,outcome,right,sounding,score_onset_ms,truth_onset_ms,"
                      "measured_onset_ms,midi,reported_midi\n");

    // Reports pooled over the excerpts, one per path and case.
    struct Pool {
        CaseStats stats;
        long sets = 0, refused = 0, late = 0; // expect, summed over excerpts
    };
    std::vector<std::vector<Pool>> pools(path_names.size(), std::vector<Pool>(kCaseCount));
    // CPU of the right case, one point per repeat and excerpt.
    std::vector<std::vector<CpuPoint>> cpu_points(path_names.size());

    for (const Excerpt& ex : excerpts) {
        std::fprintf(stderr, "excerpt %s %.3g-%.3g s\n", ex.audio.c_str(), ex.from_s, ex.to_s);
        std::vector<float> full;
        if (!bench_io::read_audio(ex.audio, full)) {
            std::fprintf(stderr, "cannot decode audio %s\n", ex.audio.c_str());
            return 1;
        }
        std::vector<bench_io::Note> midi;
        if (!bench_io::read_midi(ex.midi, midi)) {
            std::fprintf(stderr, "cannot read MIDI %s\n", ex.midi.c_str());
            return 1;
        }

        // The excerpt: the audio and the truth in its time. A note needs
        // 500 ms past its attack to be judged fairly (polybench's cut of
        // the excerpt tail), so the truth stops there.
        const double from_ms = ex.from_s * 1000.0, to_ms = ex.to_s * 1000.0;
        const size_t s0 = size_t(from_ms * 48.0), s1 = std::min(full.size(), size_t(to_ms * 48.0));
        if (s0 >= s1) {
            std::fprintf(stderr, "empty excerpt range\n");
            return 1;
        }
        const std::vector<float> audio(full.begin() + s0, full.begin() + s1);
        const double audio_ms = 1000.0 * double(audio.size()) / 48000.0;

        std::vector<TruthNote> truth;
        for (const auto& n : midi) {
            if (n.onset_ms < from_ms || n.onset_ms >= to_ms - 500.0) continue;
            truth.push_back({n.onset_ms - from_ms, n.dur_ms, n.midi});
        }
        if (truth.empty()) {
            std::fprintf(stderr, "no truth notes in range\n");
            continue;
        }

        // Fresh path instances per excerpt (the blind pass is cached per
        // audio buffer; the oracle gets the truth here and only here).
        std::vector<std::unique_ptr<Path>> run_paths;
        for (const auto& name : path_names) run_paths.push_back(make_path(name, truth));

        const char* label_base = ex.audio.c_str();
        for (size_t ci = 0; ci < kCaseCount; ++ci) {
            const Case c = kAllCases[ci];
            if (!selected(c)) continue;
            const BuiltCase built = build_case(c, truth, seed);

            for (size_t pi = 0; pi < run_paths.size(); ++pi) {
                CpuStats cpu;
                std::vector<NoteResult> results;
                run_paths[pi]->run(audio, 48000.0f, built.notes, results, cpu);
                annotate_cpu(cpu, truth);
                merge(pools[pi][ci].stats, score_case(truth, built.notes, results));
                if (path_names[pi] == "expect") {
                    const ExpectPath* ep = dynamic_cast<const ExpectPath*>(run_paths[pi].get());
                    pools[pi][ci].sets += ep->last_sets;
                    pools[pi][ci].refused += ep->last_refused;
                    pools[pi][ci].late += ep->last_late;
                }

                const ScoreWindows w = compute_windows(built.notes);
                (void)w;
                for (size_t i = 0; i < built.notes.size(); ++i) {
                    const ScoreNote& sn = built.notes[i];
                    const NoteResult& r = results[i];
                    const double truth_onset =
                        sn.truth_index >= 0 ? truth[size_t(sn.truth_index)].onset_ms
                                            : std::numeric_limits<double>::quiet_NaN();
                    int sounding = 0;
                    const double at =
                        sn.truth_index >= 0 ? truth[size_t(sn.truth_index)].onset_ms : sn.onset_ms;
                    for (const auto& g : truth)
                        if (g.onset_ms <= at && at < g.onset_ms + g.dur_ms) ++sounding;
                    char label[768];
                    std::snprintf(label, sizeof(label), "%s@%g-%g", label_base, ex.from_s, ex.to_s);
                    char truth_s[32] = "na", meas_s[32] = "na", rep_s[32] = "na";
                    if (std::isfinite(truth_onset))
                        std::snprintf(truth_s, sizeof(truth_s), "%.3f", truth_onset);
                    if (std::isfinite(r.onset_ms))
                        std::snprintf(meas_s, sizeof(meas_s), "%.3f", r.onset_ms);
                    if (std::isfinite(r.midi)) std::snprintf(rep_s, sizeof(rep_s), "%.2f", double(r.midi));
                    std::fprintf(csv, "%s,%s,%s,%s,%d,%d,%.3f,%s,%s,%d,%s\n", label,
                                 path_names[pi].c_str(), case_name(c), outcome_name(r.outcome),
                                 sn.right ? 1 : 0, sounding, sn.onset_ms, truth_s, meas_s, sn.midi,
                                 rep_s);
                }
            }
        }

        // CPU measuring pass of the right case, repeated.
        const BuiltCase right = build_case(Case::Right, truth, seed);
        for (size_t pi = 0; pi < path_names.size(); ++pi) {
            for (int rep = 0; rep < repeat; ++rep) {
                std::unique_ptr<Path> p = make_path(path_names[pi], truth);
                CpuStats cpu;
                std::vector<NoteResult> results;
                p->run(audio, 48000.0f, right.notes, results, cpu);
                annotate_cpu(cpu, truth);
                cpu_points[pi].push_back(cpu_point(cpu, audio_ms));
            }
        }
    }
    std::fclose(csv);

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
