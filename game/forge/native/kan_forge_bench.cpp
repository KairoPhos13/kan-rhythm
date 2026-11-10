// kan_forge_bench: how well does the detector the game uses hear a level?
//
// Measures, against ground truth (a validation export of the compose tab:
// the reference audio + the corrected score as MIDI, or any audio + MIDI):
//   1. blind transcription (what the editor and the game hear today):
//      precision / recall / F1 of onset + pitch, pitch errors by kind,
//      duplicates, onset offset;
//   2. expected mode (libkan candidates, kan_forge_live_expect): the score
//      tells the describer which note comes next, how many it confirms;
//   3. decoys: the same with a WRONG expectation (±1, ±2, ±12 semitones),
//      how often an expectation is "confirmed" although another note was
//      played (the risk of telling the detector what to expect).
// Everything runs through the bridge's C ABI, exactly as the game calls it.
// Measurement only (the numbers feed game/docs/transcription-quality.md).
//
// Usage: kan_forge_bench --audio ref.wav --truth score.mid
//                        [--profile ocarina] [--offset auto|<ms>] [--csv out.csv]
//                        [--blind-only 1]   (skip the expected-mode passes)
//                        [--engine describer|neural]  (transcriber for section 1;
//                         neural = Basic Pitch, blind only)
//
// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
#include "kan_forge_api.h"

#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <map>
#include <string>
#include <vector>

namespace {

struct Note {
    double onset_ms;
    double dur_ms;
    int midi; // -1 = unresolved
};

// ---- input ------------------------------------------------------------------

uint32_t rd32(const uint8_t* p) { return uint32_t(p[0]) | uint32_t(p[1]) << 8 | uint32_t(p[2]) << 16 | uint32_t(p[3]) << 24; }
uint16_t rd16(const uint8_t* p) { return uint16_t(p[0] | p[1] << 8); }

std::vector<uint8_t> read_file(const std::string& path) {
    std::vector<uint8_t> data;
    std::FILE* f = std::fopen(path.c_str(), "rb");
    if (f == nullptr) return data;
    std::fseek(f, 0, SEEK_END);
    data.resize(size_t(std::ftell(f)));
    std::fseek(f, 0, SEEK_SET);
    if (std::fread(data.data(), 1, data.size(), f) != data.size()) data.clear();
    std::fclose(f);
    return data;
}

/// PCM16 / PCM24 / float32 WAV, any channel count, mixed to mono.
bool read_wav(const std::string& path, std::vector<float>& out, int& sr) {
    const auto d = read_file(path);
    if (d.size() < 12 || std::memcmp(d.data(), "RIFF", 4) != 0 || std::memcmp(d.data() + 8, "WAVE", 4) != 0) return false;
    int format = 0, channels = 0, bits = 0;
    size_t pos = 12;
    while (pos + 8 <= d.size()) {
        const uint32_t size = rd32(d.data() + pos + 4);
        const uint8_t* body = d.data() + pos + 8;
        if (std::memcmp(d.data() + pos, "fmt ", 4) == 0 && size >= 16) {
            format = rd16(body);
            channels = rd16(body + 2);
            sr = int(rd32(body + 4));
            bits = rd16(body + 14);
            if (format == 0xFFFE && size >= 26) format = rd16(body + 24); // extensible: sub-format
        } else if (std::memcmp(d.data() + pos, "data", 4) == 0 && channels > 0) {
            const size_t bytes = std::min<size_t>(size, d.size() - pos - 8);
            const int bps = bits / 8;
            const size_t frames = bytes / size_t(bps * channels);
            out.assign(frames, 0.0f);
            for (size_t i = 0; i < frames; ++i) {
                float acc = 0.0f;
                for (int c = 0; c < channels; ++c) {
                    const uint8_t* s = body + (i * size_t(channels) + size_t(c)) * size_t(bps);
                    if (format == 3 && bits == 32) {
                        float v;
                        std::memcpy(&v, s, 4);
                        acc += v;
                    } else if (format == 1 && bits == 16) {
                        acc += float(int16_t(rd16(s))) / 32768.0f;
                    } else if (format == 1 && bits == 24) {
                        const int32_t v = int32_t(uint32_t(s[0]) << 8 | uint32_t(s[1]) << 16 | uint32_t(s[2]) << 24) >> 8;
                        acc += float(v) / 8388608.0f;
                    } else {
                        return false;
                    }
                }
                out[i] = acc / float(channels);
            }
            return true;
        }
        pos += 8 + size + (size & 1);
    }
    return false;
}

uint32_t vlq(const uint8_t*& p, const uint8_t* end) {
    uint32_t v = 0;
    while (p < end) {
        const uint8_t b = *p++;
        v = (v << 7) | (b & 0x7F);
        if ((b & 0x80) == 0) break;
    }
    return v;
}

/// Standard MIDI file (format 0/1, PPQ division, tempo map) → notes in ms.
bool read_midi(const std::string& path, std::vector<Note>& notes) {
    const auto d = read_file(path);
    if (d.size() < 14 || std::memcmp(d.data(), "MThd", 4) != 0) return false;
    const int tracks = (d[10] << 8) | d[11];
    const int ppq = (d[12] << 8) | d[13];
    if (ppq <= 0 || (ppq & 0x8000) != 0) return false;

    struct Ev { uint64_t tick; int on; int midi; };
    std::vector<Ev> events;
    std::map<uint64_t, double> tempo; // tick -> µs per quarter
    size_t pos = 8 + ((d[4] << 24) | (d[5] << 16) | (d[6] << 8) | d[7]);

    for (int t = 0; t < tracks && pos + 8 <= d.size(); ++t) {
        if (std::memcmp(d.data() + pos, "MTrk", 4) != 0) return false;
        const uint32_t len = (d[pos + 4] << 24) | (d[pos + 5] << 16) | (d[pos + 6] << 8) | d[pos + 7];
        const uint8_t* p = d.data() + pos + 8;
        const uint8_t* end = std::min(p + len, d.data() + d.size());
        uint64_t tick = 0;
        uint8_t status = 0;
        while (p < end) {
            tick += vlq(p, end);
            if (p >= end) break;
            if (*p & 0x80) status = *p++;
            if (status == 0xFF) {
                const uint8_t type = *p++;
                const uint32_t mlen = vlq(p, end);
                if (type == 0x51 && mlen == 3) tempo[tick] = double((p[0] << 16) | (p[1] << 8) | p[2]);
                p += mlen;
            } else if (status == 0xF0 || status == 0xF7) {
                p += vlq(p, end);
            } else {
                const int kind = status & 0xF0;
                const int a = *p++;
                const int b = (kind == 0xC0 || kind == 0xD0) ? 0 : *p++;
                if (kind == 0x90 && b > 0) events.push_back({tick, 1, a});
                else if (kind == 0x80 || (kind == 0x90 && b == 0)) events.push_back({tick, 0, a});
            }
        }
        pos += 8 + len;
    }

    if (tempo.empty() || tempo.begin()->first != 0) tempo[0] = 500000.0;
    auto to_ms = [&](uint64_t tick) {
        double ms = 0.0;
        auto it = tempo.begin();
        while (it != tempo.end()) {
            auto next = std::next(it);
            const uint64_t until = (next == tempo.end() || next->first > tick) ? tick : next->first;
            ms += double(until - it->first) * it->second / 1000.0 / ppq;
            if (until == tick) break;
            it = next;
        }
        return ms;
    };

    std::stable_sort(events.begin(), events.end(), [](const Ev& x, const Ev& y) { return x.tick < y.tick; });
    std::map<int, std::vector<uint64_t>> open;
    for (const auto& e : events) {
        if (e.on) {
            open[e.midi].push_back(e.tick);
        } else if (!open[e.midi].empty()) {
            const uint64_t on = open[e.midi].front();
            open[e.midi].erase(open[e.midi].begin());
            notes.push_back({to_ms(on), to_ms(e.tick) - to_ms(on), e.midi});
        }
    }
    std::sort(notes.begin(), notes.end(), [](const Note& x, const Note& y) { return x.onset_ms < y.onset_ms; });
    return !notes.empty();
}

// ---- statistics -------------------------------------------------------------

double median(std::vector<double> v) {
    if (v.empty()) return 0.0;
    std::sort(v.begin(), v.end());
    return v[v.size() / 2];
}

double pct(size_t a, size_t b) { return b == 0 ? 0.0 : 100.0 * double(a) / double(b); }

/// Greedy one-to-one matching by time distance: pairs (truth, found) within tol
/// for which `ok(truth, found)` holds, closest first.
std::vector<std::pair<int, int>> match(const std::vector<Note>& truth, const std::vector<Note>& found,
                                       double tol, bool same_pitch) {
    struct P { double d; int t, f; };
    std::vector<P> cand;
    size_t lo = 0;
    for (int t = 0; t < int(truth.size()); ++t) {
        while (lo < found.size() && found[lo].onset_ms < truth[size_t(t)].onset_ms - tol) ++lo;
        for (size_t f = lo; f < found.size() && found[f].onset_ms <= truth[size_t(t)].onset_ms + tol; ++f) {
            if (same_pitch && found[f].midi != truth[size_t(t)].midi) continue;
            cand.push_back({std::fabs(found[f].onset_ms - truth[size_t(t)].onset_ms), t, int(f)});
        }
    }
    std::sort(cand.begin(), cand.end(), [](const P& x, const P& y) { return x.d < y.d; });
    std::vector<char> ut(truth.size()), uf(found.size());
    std::vector<std::pair<int, int>> pairs;
    for (const auto& c : cand) {
        if (ut[size_t(c.t)] || uf[size_t(c.f)]) continue;
        ut[size_t(c.t)] = uf[size_t(c.f)] = 1;
        pairs.push_back({c.t, c.f});
    }
    return pairs;
}

void prf(const char* label, size_t matched, size_t truth, size_t found) {
    const double p = pct(matched, found), r = pct(matched, truth);
    std::printf("  %-34s precision %5.1f%%  recall %5.1f%%  F1 %5.1f%%\n", label, p, r,
                p + r > 0 ? 2 * p * r / (p + r) : 0.0);
}

// ---- expected mode ----------------------------------------------------------

struct ExpectedResult {
    size_t armed = 0, confirmed = 0, unresolved = 0, no_attack = 0, bound = 0;
    std::vector<double> similarity;
};

/// Pitch confirmation in expected mode, separated from timing: each score note
/// that blind transcription heard (an attack within ±100 ms) is armed 60 ms
/// before that real attack, expecting its pitch shifted by `decoy` semitones.
/// The expectation's measurement (CANDIDATE) is linked to its attack (HIT) by
/// the strike group, since libkan may file it under no record.
ExpectedResult run_expected(const std::vector<float>& sig, int sr, const std::vector<Note>& truth,
                            const std::vector<std::pair<double, int>>& arms, const char* profile, int decoy) {
    ExpectedResult r;
    kan_forge_live* live = kan_forge_live_open(profile, float(sr));
    if (live == nullptr) return r;

    const size_t chunk = size_t(sr / 100);
    std::vector<kan_forge_live_event> ev(4096);

    struct Attack { double onset = 0; int note = -1; bool bound = false; float similarity = -1; uint32_t flags = 0; };
    std::map<uint32_t, Attack> attacks; // by strike group
    size_t next = 0;
    int armed_note = -1;
    bool pending = false;
    uint32_t last_group = 0, armed_after = 0;

    for (size_t at = 0; at < sig.size(); at += chunk) {
        const double now = 1000.0 * double(at) / sr;

        // Arm the next note once its time comes (a stale expectation blocks: libkan keeps one).
        if (!pending && next < arms.size() && now >= arms[next].first) {
            const float m = float(truth[size_t(arms[next].second)].midi + decoy);
            if (kan_forge_live_expect(live, &m, 1) == 1) {
                pending = true;
                armed_note = arms[next].second;
                armed_after = last_group;
            }
            ++next;
        }

        const int32_t n = int32_t(std::min(chunk, sig.size() - at));
        const int32_t got = kan_forge_live_process(live, sig.data() + at, n, ev.data(), int32_t(ev.size()));

        for (int32_t k = 0; k < got; ++k) {
            const auto& e = ev[size_t(k)];
            if (e.kind == KAN_FORGE_LIVE_HIT) {
                last_group = std::max(last_group, e.group);
                Attack& a = attacks[e.group];
                if (a.onset == 0) a.onset = e.onset_ms;
                if (e.midi > 0 && (e.flags & (1u << 7)) == 0) a.bound = true; // carries the expectation
            } else if (e.kind == KAN_FORGE_LIVE_CANDIDATE && pending && e.group > armed_after) {
                // The first measurement of an attack that began after arming (earlier
                // attacks keep re-measuring their own expectations while they sound).
                pending = false;
                Attack& a = attacks[e.group];
                a.note = armed_note;
                a.similarity = e.similarity;
                a.flags = e.flags;
            }
        }
    }
    kan_forge_live_close(live);

    std::vector<const Attack*> taken(truth.size(), nullptr);
    for (const auto& kv : attacks)
        if (kv.second.note >= 0) taken[size_t(kv.second.note)] = &kv.second;

    r.armed = arms.size();
    for (const auto& arm : arms) {
        const Attack* a = taken[size_t(arm.second)];
        if (a == nullptr) {
            ++r.no_attack; // the expectation was never measured for this note
            continue;
        }
        r.bound += a->bound;
        r.similarity.push_back(a->similarity);

        // libkan's own bar for claiming a pitch (probe fit): similarity >= 0.40
        // and the refined pitch inside the search band.
        if (a->similarity >= 0.40f && (a->flags & (1u << 6)) == 0) ++r.confirmed;
        else ++r.unresolved;
    }
    return r;
}


/// Expected mode per note in isolation: a fresh describer around each heard
/// attack (800 ms before to 500 ms after), the expectation armed 60 ms before
/// it. Measures whether the expectation confirms the right pitch and rejects a
/// wrong one, free of the streaming limits (one pending expectation that can't
/// be replaced; legato changes don't consume it).
ExpectedResult run_isolated(const std::vector<float>& sig, int sr, const std::vector<Note>& truth,
                            const std::vector<std::pair<double, int>>& arms, const char* profile, int decoy) {
    ExpectedResult r;
    const size_t chunk = size_t(sr / 100);
    std::vector<kan_forge_live_event> ev(4096);
    r.armed = arms.size();

    for (const auto& arm : arms) {
        const double attack = arm.first + 60;
        const size_t from = size_t(std::max(0.0, attack - 800) * sr / 1000);
        const size_t to = std::min(sig.size(), size_t((attack + 500) * sr / 1000));
        const double arm_at = arm.first - 1000.0 * double(from) / sr; // in the segment's time

        kan_forge_live* live = kan_forge_live_open(profile, float(sr));
        if (live == nullptr) return r;

        bool armed = false, measured = false, bound = false;
        uint32_t last_group = 0, armed_after = 0;
        float similarity = 0;
        uint32_t flags = 0;

        for (size_t at = from; at < to && !measured; at += chunk) {
            const double now = 1000.0 * double(at - from) / sr;
            if (!armed && now >= arm_at) {
                const float m = float(truth[size_t(arm.second)].midi + decoy);
                armed = kan_forge_live_expect(live, &m, 1) == 1;
                armed_after = last_group;
            }
            const int32_t n = int32_t(std::min(chunk, to - at));
            const int32_t got = kan_forge_live_process(live, sig.data() + at, n, ev.data(), int32_t(ev.size()));
            for (int32_t k = 0; k < got && !measured; ++k) {
                const auto& e = ev[size_t(k)];
                if (e.kind == KAN_FORGE_LIVE_HIT) {
                    last_group = std::max(last_group, e.group);
                    if (armed && e.group > armed_after && e.midi > 0 && (e.flags & (1u << 7)) == 0) bound = true;
                } else if (e.kind == KAN_FORGE_LIVE_CANDIDATE && armed && e.group > armed_after) {
                    measured = true;
                    similarity = e.similarity;
                    flags = e.flags;
                }
            }
        }
        kan_forge_live_close(live);

        if (!measured) {
            ++r.no_attack;
            continue;
        }
        r.bound += bound;
        r.similarity.push_back(similarity);
        if (similarity >= 0.40f && (flags & (1u << 6)) == 0) ++r.confirmed;
        else ++r.unresolved;
    }
    return r;
}

} // namespace

int main(int argc, char** argv) {
    std::string audio, truth_path, profile = "keyboard", offset_arg = "auto", csv, engine = "describer";
    bool blind_only = false;
    for (int i = 1; i + 1 < argc; i += 2) {
        const std::string k = argv[i], v = argv[i + 1];
        if (k == "--audio") audio = v;
        else if (k == "--truth") truth_path = v;
        else if (k == "--profile") profile = v;
        else if (k == "--offset") offset_arg = v;
        else if (k == "--csv") csv = v;
        else if (k == "--blind-only") blind_only = v == "1";
        else if (k == "--engine") engine = v;
    }
    if (engine != "describer") blind_only = true; // expected mode is the describer's
    if (audio.empty() || truth_path.empty()) {
        std::fprintf(stderr, "usage: kan_forge_bench --audio ref.wav --truth score.mid [--profile ocarina] [--offset auto|ms] [--csv out.csv]\n");
        return 2;
    }

    std::vector<Note> truth;
    if (!read_midi(truth_path, truth)) {
        std::fprintf(stderr, "cannot read MIDI %s\n", truth_path.c_str());
        return 1;
    }
    std::vector<float> sig;
    int sr = 0;
    if (!read_wav(audio, sig, sr)) {
        std::fprintf(stderr, "cannot read WAV %s (PCM16/24 or float32)\n", audio.c_str());
        return 1;
    }

    // 1. Blind transcription through the bridge (as the editor runs it).
    std::vector<kan_forge_note> raw(65536);
    int32_t count = 0;
    char err[512] = {0};
    const auto t0 = std::chrono::steady_clock::now();
    if (kan_forge_transcribe_file_with(audio.c_str(), profile.c_str(), engine.c_str(), raw.data(), int32_t(raw.size()), &count, err, sizeof(err)) != KAN_FORGE_OK) {
        std::fprintf(stderr, "transcription failed: %s\n", err);
        return 1;
    }
    const double transcribe_s = std::chrono::duration<double>(std::chrono::steady_clock::now() - t0).count();
    std::vector<Note> found;
    for (int32_t i = 0; i < count; ++i) found.push_back({raw[size_t(i)].onset_ms, raw[size_t(i)].dur_ms, raw[size_t(i)].midi});

    // Global offset: transcriber onsets vs the truth (same pitch, nearest).
    std::vector<double> diffs;
    for (const auto& t : truth) {
        double bestd = 1e9;
        for (const auto& f : found)
            if (f.midi == t.midi && std::fabs(f.onset_ms - t.onset_ms) < std::fabs(bestd) && std::fabs(f.onset_ms - t.onset_ms) <= 300) bestd = f.onset_ms - t.onset_ms;
        if (bestd < 1e8) diffs.push_back(bestd);
    }
    const double measured_offset = median(diffs);
    const double offset = offset_arg == "auto" ? measured_offset : std::atof(offset_arg.c_str());

    std::vector<Note> aligned = truth; // the truth in the transcriber's time
    for (auto& t : aligned) t.onset_ms += offset;

    size_t unresolved = 0;
    for (const auto& f : found) unresolved += f.midi < 0;
    size_t dups = 0;
    for (size_t i = 0; i < found.size(); ++i)
        for (size_t j = i + 1; j < found.size() && found[j].onset_ms - found[i].onset_ms <= 30; ++j)
            if (found[j].midi == found[i].midi) { ++dups; break; }

    std::printf("kan_forge_bench: %s\n", audio.c_str());
    std::printf("truth: %zu notes (%s); audio %.1f s at %d Hz; profile %s\n", truth.size(), truth_path.c_str(),
                double(sig.size()) / sr, sr, profile.c_str());
    std::printf("\n1. BLIND TRANSCRIPTION: engine %s (%.1f s)\n", engine.c_str(), transcribe_s);
    std::printf("  found %zu notes: %zu without a pitch (%.1f%%), %zu repeated within 30 ms\n", found.size(), unresolved,
                pct(unresolved, found.size()), dups);
    std::printf("  transcriber vs truth offset: median %+.0f ms over %zu same-pitch pairs (%s)\n", measured_offset, diffs.size(),
                offset_arg == "auto" ? "used to align" : "not used: --offset given");

    for (double tol : {50.0, 100.0}) {
        char label[64];
        std::snprintf(label, sizeof(label), "onset ±%.0f ms + right pitch:", tol);
        prf(label, match(aligned, found, tol, true).size(), aligned.size(), found.size());
    }
    const auto onset_pairs = match(aligned, found, 50, false);
    prf("onset ±50 ms, any pitch:", onset_pairs.size(), aligned.size(), found.size());

    size_t exact = 0, semi = 0, octave = 0, other = 0, unres = 0;
    for (const auto& pr : onset_pairs) {
        const int a = aligned[size_t(pr.first)].midi, b = found[size_t(pr.second)].midi;
        if (b < 0) ++unres;
        else if (a == b) ++exact;
        else if (std::abs(a - b) == 12 || std::abs(a - b) == 24) ++octave;
        else if (std::abs(a - b) <= 2) ++semi;
        else ++other;
    }
    std::printf("  pitch of the onset-matched notes: right %.1f%%, ±1-2 semitones %.1f%%, octave %.1f%%, other %.1f%%, unresolved %.1f%%\n",
                pct(exact, onset_pairs.size()), pct(semi, onset_pairs.size()), pct(octave, onset_pairs.size()),
                pct(other, onset_pairs.size()), pct(unres, onset_pairs.size()));
    std::vector<double> errs;
    for (const auto& pr : match(aligned, found, 100, true)) errs.push_back(found[size_t(pr.second)].onset_ms - aligned[size_t(pr.first)].onset_ms);
    std::vector<double> abs_dev;
    const double med = median(errs);
    for (double e : errs) abs_dev.push_back(std::fabs(e - med));
    std::printf("  onset error after alignment: median %+.1f ms, MAD %.1f ms\n", med, median(abs_dev));

    if (blind_only) return 0;

    // Where each heard score note really starts: its blind attack (any pitch, ±100 ms).
    std::vector<std::pair<double, int>> arms;
    for (const auto& pr : match(aligned, found, 100, false)) {
        arms.push_back({found[size_t(pr.second)].onset_ms - 60, pr.first});
    }
    std::sort(arms.begin(), arms.end());
    size_t blind_right = 0;
    for (const auto& pr : match(aligned, found, 100, false))
        blind_right += found[size_t(pr.second)].midi == aligned[size_t(pr.first)].midi;

    std::printf("\n2. EXPECTED MODE STREAMED (as a game would arm it, one note after another)\n");
    {
        const ExpectedResult r = run_expected(sig, sr, truth, arms, profile.c_str(), 0);
        std::printf("  right note: confirmed %.1f%%, not confirmed %.1f%%, never measured %.1f%% of %zu heard notes; median similarity %.2f\n",
                    pct(r.confirmed, r.armed), pct(r.unresolved, r.armed), pct(r.no_attack, r.armed), r.armed, median(r.similarity));
    }

    std::printf("\n3. EXPECTED MODE PER NOTE (fresh describer around each heard attack): right vs WRONG expectations\n");
    std::printf("  (blind mode named the right pitch for %.1f%% of these %zu notes)\n", pct(blind_right, arms.size()), arms.size());
    std::printf("  %-14s %10s %14s %13s %10s %12s\n", "expectation", "confirmed", "not confirmed", "not measured", "sim (med)", "bound (HIT)");
    FILE* out = csv.empty() ? nullptr : std::fopen(csv.c_str(), "w");
    if (out != nullptr) std::fprintf(out, "decoy,confirmed,not_confirmed,not_measured,notes,similarity_median\n");
    for (int decoy : {0, 1, -1, 2, -2, 12, -12}) {
        const ExpectedResult r = run_isolated(sig, sr, truth, arms, profile.c_str(), decoy);
        char label[48];
        if (decoy == 0) std::snprintf(label, sizeof(label), "right note");
        else std::snprintf(label, sizeof(label), "WRONG by %+d", decoy);
        std::printf("  %-14s %9.1f%% %13.1f%% %12.1f%% %10.2f %11.1f%%\n", label, pct(r.confirmed, r.armed), pct(r.unresolved, r.armed),
                    pct(r.no_attack, r.armed), median(r.similarity), pct(r.bound, r.armed - r.no_attack));
        if (out != nullptr)
            std::fprintf(out, "%d,%zu,%zu,%zu,%zu,%.3f\n", decoy, r.confirmed, r.unresolved, r.no_attack, r.armed, median(r.similarity));
    }
    if (out != nullptr) std::fclose(out);
    std::printf("\n  confirmed = an attack within ±100 ms took the expectation and libkan's own bar holds\n"
                "  (similarity >= 0.40, pitch inside the search band). For a WRONG expectation that is a\n"
                "  false acceptance. bound = the attack's HIT carried the expectation (else libkan filed\n"
                "  the measurement under no record: monophonic deferral).\n");
    return 0;
}
