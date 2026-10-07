// kan_dataset_bench - large-scale evaluation of libkan against datasets with
// millisecond-aligned ground truth (MAESTRO first). The manifest format keeps
// it open for other datasets and instruments.
//
// What it measures: score-anchored verification (the same
// kan::score_verify::measure_score core the probe and the counterexample
// suite use) — per-note similarity, cents vs ground truth, duration error,
// peak-time offset, and unmatched strikes. It measures; it never judges.
// The measured invariants and their balance live in
// docs/architecture/measurement-invariants.md.
//
// License note: dataset audio/MIDI is never committed to this repository.
// MAESTRO (Google Magenta) is CC BY-NC-SA 4.0, research/non-commercial.
#include "libkan/score_verify.hpp"
#include "midi_reader.hpp"

#define MINIAUDIO_IMPLEMENTATION
#include "miniaudio.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <mutex>
#include <sstream>
#include <string>
#include <thread>
#include <vector>

namespace fs = std::filesystem;
using kan::score_verify::ScoreNote;
using dataset_bench::MidiNote;
using dataset_bench::parse_smf;
using dataset_bench::write_smf;

namespace {

struct Entry {
    std::string audio;
    std::string gt;    // .mid (SMF) or .score (score text)
    std::string label;
};

struct NoteRow {
    std::string label;
    float onset_ms = 0.0f, dur_ms = 0.0f;
    int midi = 0;
    float sim = 0.0f, cents = 0.0f, amp = 0.0f;
    float meas_dur = 0.0f, dur_err = 0.0f, t_peak = 0.0f, onset_err = 0.0f;
    bool found = false;
};

struct FileRow {
    std::string label;
    int notes = 0, found = 0;
    float cents_abs_mean = 0.0f, cents_abs_max = 0.0f;
    float dur_err_mean = 0.0f, dur_err_max = 0.0f;
    int extras = 0;
    double audio_s = 0.0, wall_s = 0.0;
};

bool load_audio(const char* path, std::vector<float>& sig) {
    ma_decoder_config cfg = ma_decoder_config_init(ma_format_f32, 1, 48000);
    ma_decoder dec;
    if (ma_decoder_init_file(path, &cfg, &dec) != MA_SUCCESS) return false;
    std::vector<float> buf;
    float chunk[4096];
    for (;;) {
        ma_uint64 got = 0;
        if (ma_decoder_read_pcm_frames(&dec, chunk, 4096, &got) != MA_SUCCESS) break;
        if (got == 0) break;
        buf.insert(buf.end(), chunk, chunk + got);
    }
    ma_decoder_uninit(&dec);
    sig.swap(buf);
    return !sig.empty();
}

bool load_ground_truth(const std::string& path, std::vector<ScoreNote>& notes) {
    const bool is_midi = (path.size() > 4) &&
        (path.compare(path.size() - 4, 4, ".mid") == 0 ||
         path.compare(path.size() - 5, 5, ".midi") == 0);
    if (!is_midi) return kan::score_verify::parse_score(path.c_str(), notes);

    std::ifstream f(path, std::ios::binary);
    if (!f) return false;
    const std::vector<uint8_t> bytes((std::istreambuf_iterator<char>(f)),
                                     std::istreambuf_iterator<char>());
    std::vector<MidiNote> mn;
    std::string err;
    if (!parse_smf(bytes, mn, &err)) return false;
    notes.clear();
    for (const auto& m : mn) {
        ScoreNote n;
        n.onset_ms = float(m.onset_ms);
        n.dur_ms = float(m.dur_ms);
        n.midi = m.midi;
        notes.push_back(n);
    }
    return !notes.empty();
}

/// Comma-separated line with "quoted, fields" support (MAESTRO titles have commas).
std::vector<std::string> split_csv(const std::string& line) {
    std::vector<std::string> out;
    std::string cur;
    bool quoted = false;
    for (size_t i = 0; i < line.size(); ++i) {
        const char c = line[i];
        if (quoted) {
            if (c == '"') {
                if (i + 1 < line.size() && line[i + 1] == '"') { cur += '"'; ++i; }
                else quoted = false;
            } else cur += c;
        } else if (c == '"') quoted = true;
        else if (c == ',') { out.push_back(cur); cur.clear(); }
        else cur += c;
    }
    out.push_back(cur);
    return out;
}

std::string find_audio_for_midi(const std::string& midi_path) {
    static const char* EXT[2] = {".wav", ".flac"};
    const size_t dot = midi_path.rfind('.');
    const std::string stem = (dot == std::string::npos) ? midi_path : midi_path.substr(0, dot);
    for (const char* e : EXT) {
        const std::string cand = stem + e;
        if (fs::exists(cand)) return cand;
    }
    return {};
}

/// MAESTRO layout: root/maestro-v*.csv listing midi_filename (+ split).
/// Falls back to a recursive *.mid scan when no CSV is present.
bool discover_maestro(const std::string& root, const std::string& split,
                      std::vector<Entry>& out, std::string* err) {
    fs::path csv;
    for (const auto& e : fs::directory_iterator(root)) {
        const std::string name = e.path().filename().string();
        if (name.rfind("maestro", 0) == 0 && name.size() > 4 &&
            name.compare(name.size() - 4, 4, ".csv") == 0) {
            csv = e.path();
            break;
        }
    }
    if (!csv.empty()) {
        std::ifstream f(csv);
        std::string line;
        if (!std::getline(f, line)) { *err = "empty metadata CSV"; return false; }
        const auto hdr = split_csv(line);
        auto col = [&](const char* n) -> int {
            for (size_t i = 0; i < hdr.size(); ++i) {
                if (hdr[i] == n) return int(i);
            }
            return -1;
        };
        const int c_midi = col("midi_filename");
        const int c_split = col("split");
        if (c_midi < 0) { *err = "metadata CSV lacks midi_filename"; return false; }
        while (std::getline(f, line)) {
            if (line.empty()) continue;
            const auto f2 = split_csv(line);
            if (int(f2.size()) <= c_midi) continue;
            if (!split.empty() && c_split >= 0 && int(f2.size()) > c_split && f2[size_t(c_split)] != split) {
                continue;
            }
            const fs::path mid = fs::path(root) / f2[size_t(c_midi)];
            const std::string audio = find_audio_for_midi(mid.string());
            if (audio.empty()) continue;
            out.push_back({audio, mid.string(), mid.filename().string()});
        }
        return !out.empty();
    }
    // Fallback: recursive scan for .mid files with a sibling audio file.
    for (const auto& e : fs::recursive_directory_iterator(root)) {
        if (!e.is_regular_file()) continue;
        const std::string p = e.path().string();
        if (p.size() < 4 || p.compare(p.size() - 4, 4, ".mid") != 0) continue;
        const std::string audio = find_audio_for_midi(p);
        if (audio.empty()) continue;
        out.push_back({audio, p, e.path().filename().string()});
    }
    return !out.empty();
}

bool read_manifest(const std::string& path, const std::string& audio_root,
                   std::vector<Entry>& out, std::string* err) {
    std::ifstream f(path);
    if (!f) { *err = "cannot open manifest"; return false; }
    const fs::path base = audio_root.empty() ? fs::path(path).parent_path() : fs::path(audio_root);
    std::string line;
    while (std::getline(f, line)) {
        if (line.empty() || line[0] == '#') continue;
        std::istringstream is(line);
        std::string audio, gt;
        if (!(is >> audio >> gt)) continue;
        out.push_back({(base / audio).string(), (base / gt).string(), audio});
    }
    return !out.empty();
}

std::vector<NoteRow> bench_entry(const Entry& e, float fit_step_ms,
                                 FileRow& fr, std::string* err) {
    const auto t0 = std::chrono::steady_clock::now();
    std::vector<float> sig;
    if (!load_audio(e.audio.c_str(), sig)) {
        *err = "cannot load audio: " + e.audio;
        return {};
    }
    std::vector<ScoreNote> notes;
    if (!load_ground_truth(e.gt, notes)) {
        *err = "cannot load ground truth: " + e.gt;
        return {};
    }
    std::vector<float> extras;
    kan::score_verify::measure_score(sig, 48000.0f, notes, &extras, fit_step_ms);

    std::vector<NoteRow> rows;
    float c_sum = 0.0f, c_max = 0.0f, d_sum = 0.0f, d_max = 0.0f;
    int found = 0, n_abs = 0;
    for (const auto& n : notes) {
        const float sim = n.sim_w > 0.0f ? n.sim_wsum / n.sim_w : 0.0f;
        const float cents = n.sim_w > 0.0f ? n.cents_wsum / n.sim_w : 0.0f;
        const float meas = (n.measured_end_ms > 0.0f) ? n.measured_end_ms - n.onset_ms : 0.0f;
        NoteRow r;
        r.label = e.label;
        r.onset_ms = n.onset_ms;
        r.dur_ms = n.dur_ms;
        r.midi = n.midi;
        r.sim = sim;
        r.cents = cents;
        r.amp = n.amp_peak;
        r.meas_dur = meas;
        r.dur_err = meas - n.dur_ms;
        r.t_peak = (n.peak_ms > 0.0f) ? n.peak_ms - n.onset_ms : -1.0f;
        r.onset_err = n.onset_err_ms;
        r.found = sim >= 0.60f;
        rows.push_back(r);
        if (r.found) {
            ++found;
            const float ca = std::fabs(cents);
            if (ca <= 250.0f) {
                c_sum += ca;
                c_max = std::max(c_max, ca);
                d_sum += r.dur_err;
                d_max = std::max(d_max, std::fabs(r.dur_err));
                ++n_abs;
            }
        }
    }
    const auto t1 = std::chrono::steady_clock::now();
    fr.label = e.label;
    fr.notes = int(notes.size());
    fr.found = found;
    fr.cents_abs_mean = n_abs ? c_sum / n_abs : 0.0f;
    fr.cents_abs_max = c_max;
    fr.dur_err_mean = n_abs ? d_sum / n_abs : 0.0f;
    fr.dur_err_max = d_max;
    fr.extras = int(extras.size());
    fr.audio_s = double(sig.size()) / 48000.0;
    fr.wall_s = std::chrono::duration<double>(t1 - t0).count();
    return rows;
}

// ---------------------------------------------------------------------------
// Self-test: a synthesized performance, its SMF ground truth, and the full
// pipeline (SMF round-trip -> wav -> measure) with known expectations. No
// dataset required; this is what ctest runs.
// ---------------------------------------------------------------------------

void add_note(std::vector<float>& sig, float t0, float dur, int midi, float amp, uint32_t seed) {
    const float sr = 48000.0f;
    const float f = 440.0f * std::pow(2.0f, (float(midi) - 69.0f) / 12.0f);
    const size_t at = size_t(t0 / 1000.0f * sr);
    const size_t n = size_t(dur / 1000.0f * sr);
    for (size_t i = 0; at + i < sig.size() && i < n; ++i) {
        const float t = float(i) / sr;
        float s = 0.0f;
        if (t < 0.005f) {
            const uint32_t h = uint32_t(i) * 2654435761u + seed;
            s = 0.25f * (float(int((h ^ (h >> 13)) % 2000)) / 1000.0f - 1.0f) * (1.0f - t / 0.005f);
        }
        for (int h = 1; h <= 5; ++h) {
            const float decay = std::exp(-(1.2f + 0.25f * float(h)) * t);
            const uint32_t ph = seed * 2654435761u + uint32_t(h) * 40503u;
            const float phase = float((ph >> 8) % 6283u) / 1000.0f;
            s += (0.5f / float(h)) * decay * std::sin(2.0f * float(M_PI) * f * float(h) * t + phase);
        }
        sig[at + i] += amp * s;
    }
}

bool write_wav(const std::string& path, const std::vector<float>& sig) {
    ma_encoder_config ecfg = ma_encoder_config_init(ma_encoding_format_wav,
                                                    ma_format_f32, 1, 48000);
    ma_encoder enc;
    if (ma_encoder_init_file(path.c_str(), &ecfg, &enc) != MA_SUCCESS) return false;
    ma_uint64 written = 0;
    ma_encoder_write_pcm_frames(&enc, sig.data(), sig.size(), &written);
    ma_encoder_uninit(&enc);
    return written == sig.size();
}

bool run_selftest(const std::string& out_dir) {
    std::printf("=== kan_dataset_bench --selftest ===\n");
    fs::create_directories(out_dir);
    struct P { float t, d; int midi; };
    const P perf[] = {{0, 1000, 57}, {0, 1000, 60}, {1500, 800, 55}, {2500, 800, 64},
                      {3500, 1200, 62}, {5000, 1000, 53}, {5000, 1000, 57},
                      {6500, 1500, 48}, {8500, 1000, 55}};
    std::vector<float> sig(size_t(10.5f * 48000.0f), 0.0f);
    std::vector<MidiNote> gt;
    uint32_t seed = 1;
    for (const auto& p : perf) {
        add_note(sig, p.t, p.d, p.midi, 0.5f, seed++);
        gt.push_back({double(p.t), double(p.d), p.midi});
    }
    for (size_t i = 0; i < sig.size(); ++i) {  // deterministic noise floor
        seed = seed * 1664525u + 1013904223u;
        sig[i] += 0.005f * (float(int(seed >> 8 & 0xFFFF)) / 32767.5f - 1.0f);
    }

    // SMF round-trip (ground-truth ingestion is part of the product).
    const std::string mid = (fs::path(out_dir) / "selftest.mid").string();
    const auto bytes = write_smf(gt);
    {
        std::ofstream f(mid, std::ios::binary);
        f.write(reinterpret_cast<const char*>(bytes.data()), std::streamsize(bytes.size()));
    }
    std::vector<MidiNote> back;
    std::string err;
    bool ok = true;
    if (!parse_smf(bytes, back, &err) || back.size() != gt.size()) {
        std::printf("  [FAIL] SMF round-trip parse (%s)\n", err.c_str());
        ok = false;
    } else {
        for (size_t i = 0; i < gt.size(); ++i) {
            if (back[i].midi != gt[i].midi ||
                std::fabs(back[i].onset_ms - gt[i].onset_ms) > 2.0 ||
                std::fabs(back[i].dur_ms - gt[i].dur_ms) > 2.0) {
                std::printf("  [FAIL] SMF round-trip note %zu\n", i);
                ok = false;
            }
        }
        if (ok) std::printf("  [ ok ] SMF round-trip: %zu notes within 2 ms\n", gt.size());
    }

    const std::string wav = (fs::path(out_dir) / "selftest.wav").string();
    if (!write_wav(wav, sig)) {
        std::printf("  [FAIL] cannot write selftest wav\n");
        return false;
    }
    Entry e{wav, mid, "selftest"};
    FileRow fr;
    const auto rows = bench_entry(e, 75.0f, fr, &err);
    if (rows.empty()) {
        std::printf("  [FAIL] pipeline run: %s\n", err.c_str());
        return false;
    }
    float c_sum = 0.0f;
    int found = 0, n_abs = 0;
    for (const auto& r : rows) {
        if (r.found) {
            ++found;
            if (std::fabs(r.cents) <= 250.0f) { c_sum += std::fabs(r.cents); ++n_abs; }
        }
    }
    const float c_mean = n_abs ? c_sum / n_abs : 999.0f;
    const bool pass_found = found == int(rows.size());
    const bool pass_cents = c_mean <= 20.0f;
    std::printf("  [%s] notes found: %d/%d (sim >= 0.60)\n",
                pass_found ? " ok " : "FAIL", found, int(rows.size()));
    std::printf("  [%s] |cents| mean: %.1f c (<= 20)\n", pass_cents ? " ok " : "FAIL", double(c_mean));
    std::printf("  [%s] duration err mean: %.0f ms (release bias visible: I8)\n",
                " ok ", double(fr.dur_err_mean));
    ok = ok && pass_found && pass_cents;
    std::printf("%s\n", ok ? "SELFTEST PASS" : "SELFTEST FAIL");
    return ok;
}

} // namespace

int main(int argc, char** argv) {
    std::string manifest, maestro_root, out_dir = "dataset_bench_out", audio_root, split;
    std::string dump_score, selftest_dir = "dataset_bench_selftest_out";
    bool selftest = false;
    int jobs = 6, limit = 0, offset = 0;
    float fit_step_ms = 75.0f;

    for (int i = 1; i < argc; ++i) {
        const std::string a = argv[i];
        auto next = [&]() -> const char* { return (i + 1 < argc) ? argv[++i] : ""; };
        if (a == "--manifest") manifest = next();
        else if (a == "--maestro") maestro_root = next();
        else if (a == "--out") out_dir = next();
        else if (a == "--audio-root") audio_root = next();
        else if (a == "--split") split = next();
        else if (a == "--jobs") jobs = std::atoi(next());
        else if (a == "--limit") limit = std::atoi(next());
        else if (a == "--offset") offset = std::atoi(next());
        else if (a == "--fit-step") fit_step_ms = float(std::atof(next()));
        else if (a == "--dump-score") dump_score = next();
        else if (a == "--selftest") selftest = true;
        else if (a == "--selftest-dir") selftest_dir = next();
        else {
            std::fprintf(stderr, "unknown option: %s\n", a.c_str());
            return 2;
        }
    }

    if (selftest) return run_selftest(selftest_dir) ? 0 : 1;

    if (!dump_score.empty()) {
        std::vector<MidiNote> notes;
        std::string err;
        std::ifstream f(dump_score, std::ios::binary);
        const std::vector<uint8_t> bytes((std::istreambuf_iterator<char>(f)),
                                         std::istreambuf_iterator<char>());
        if (!parse_smf(bytes, notes, &err)) {
            std::fprintf(stderr, "[ERROR] %s: %s\n", dump_score.c_str(), err.c_str());
            return 1;
        }
        for (const auto& n : notes) {
            std::printf("%.0f %.0f %d\n", n.onset_ms, n.dur_ms, n.midi);
        }
        return 0;
    }

    std::vector<Entry> entries;
    std::string err;
    bool ok = false;
    if (!manifest.empty()) ok = read_manifest(manifest, audio_root, entries, &err);
    else if (!maestro_root.empty()) ok = discover_maestro(maestro_root, split, entries, &err);
    if (!ok) {
        std::fprintf(stderr, "[ERROR] no dataset entries (%s)\n", err.c_str());
        std::fprintf(stderr, "usage: kan_dataset_bench --maestro <root> | --manifest <file>\n"
                             "       [--jobs N] [--limit N] [--offset N] [--split name]\n"
                             "       [--fit-step ms] [--out dir] [--audio-root dir]\n"
                             "       --dump-score <file.mid> | --selftest\n");
        return 1;
    }
    std::sort(entries.begin(), entries.end(),
              [](const Entry& a, const Entry& b) { return a.label < b.label; });
    if (offset > 0 && size_t(offset) < entries.size()) {
        entries.erase(entries.begin(), entries.begin() + offset);
    } else if (offset > 0) {
        entries.clear();
    }
    if (limit > 0 && size_t(limit) < entries.size()) entries.resize(size_t(limit));

    fs::create_directories(out_dir);
    std::printf("# kan_dataset_bench: %zu entries, jobs %d, fit step %.0f ms\n",
                entries.size(), jobs, double(fit_step_ms));

    std::vector<std::vector<NoteRow>> rows(entries.size());
    std::vector<FileRow> files(entries.size());
    std::atomic<size_t> next{0};
    std::mutex log;
    std::vector<std::thread> pool;
    const int n_jobs = std::max(1, std::min(jobs, 32));
    for (int j = 0; j < n_jobs; ++j) {
        pool.emplace_back([&]() {
            for (;;) {
                const size_t i = next.fetch_add(1);
                if (i >= entries.size()) break;
                std::string e2;
                rows[i] = bench_entry(entries[i], fit_step_ms, files[i], &e2);
                if (!e2.empty()) {
                    std::lock_guard<std::mutex> lk(log);
                    std::fprintf(stderr, "[WARN] %s\n", e2.c_str());
                } else {
                    std::lock_guard<std::mutex> lk(log);
                    std::printf("  %s: %d notes, %d found\n",
                                files[i].label.c_str(), files[i].notes, files[i].found);
                }
            }
        });
    }
    for (auto& t : pool) t.join();

    // Outputs: per-note rows, per-file summary, dataset aggregate.
    double audio_s = 0.0, wall_s = 0.0;
    long notes = 0, found = 0;
    double c_sum = 0.0, c_max = 0.0, d_sum = 0.0, d_max = 0.0;
    long n_abs = 0;
    long extras = 0;
    {
        std::ofstream fn(out_dir + "/notes.csv");
        std::ofstream ff(out_dir + "/files.csv");
        fn << "file,onset_ms,dur_written_ms,midi,found,sim,cents,amp,"
              "meas_dur_ms,dur_err_ms,t_peak_ms,onset_err_ms\n";
        ff << "file,notes,found,cents_abs_mean,cents_abs_max,"
              "dur_err_mean_ms,dur_err_max_ms,extra_strikes,audio_s,wall_s\n";
        for (size_t i = 0; i < entries.size(); ++i) {
            const auto& fr = files[i];
            ff << fr.label << ',' << fr.notes << ',' << fr.found << ','
               << fr.cents_abs_mean << ',' << fr.cents_abs_max << ','
               << fr.dur_err_mean << ',' << fr.dur_err_max << ','
               << fr.extras << ',' << fr.audio_s << ',' << fr.wall_s << '\n';
            for (const auto& r : rows[i]) {
                fn << r.label << ',' << r.onset_ms << ',' << r.dur_ms << ',' << r.midi << ','
                   << (r.found ? 1 : 0) << ',' << r.sim << ',' << r.cents << ',' << r.amp << ','
                   << r.meas_dur << ',' << r.dur_err << ',' << r.t_peak << ',' << r.onset_err << '\n';
            }
            notes += fr.notes;
            found += fr.found;
            extras += fr.extras;
            audio_s += fr.audio_s;
            wall_s += fr.wall_s;
            if (fr.notes > 0 && fr.found > 0) {
                c_sum += double(fr.cents_abs_mean) * fr.found;
                c_max = std::max(c_max, double(fr.cents_abs_max));
                d_sum += double(fr.dur_err_mean) * fr.found;
                d_max = std::max(d_max, double(fr.dur_err_max));
                n_abs += fr.found;
            }
        }
    }

    const double c_mean = n_abs ? c_sum / n_abs : 0.0;
    const double d_mean = n_abs ? d_sum / n_abs : 0.0;
    FILE* so = std::fopen((out_dir + "/summary.txt").c_str(), "w");
    auto emit = [&](const char* fmt, auto... args) {
        std::printf(fmt, args...);
        if (so != nullptr) { std::fprintf(so, fmt, args...); }
    };
    emit("# dataset summary (%zu entries)\n", entries.size());
    emit("# notes: %ld written, %ld found (sim >= 0.60): %.1f%%\n",
         notes, found, notes ? 100.0 * double(found) / double(notes) : 0.0);
    emit("# |cents| vs ground truth: mean %.1f c, max %.1f c  [I3: <= 30 mean]\n",
         c_mean, c_max);
    emit("# duration err (measured-written): mean %+.0f ms, max %.0f ms  "
         "[I8: release-bias estimate]\n", d_mean, d_max);
    emit("# extra strikes (played, not in ground truth): %ld\n", extras);
    emit("# throughput: %.0f s audio in %.0f s wall (jobs %d, %.2fx real time)\n",
         audio_s, wall_s, n_jobs, wall_s > 0.0 ? audio_s / wall_s : 0.0);
    if (so != nullptr) std::fclose(so);
    std::printf("# outputs: %s/{notes.csv,files.csv,summary.txt}\n", out_dir.c_str());
    return 0;
}
