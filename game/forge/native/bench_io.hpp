// bench_io: shared input readers for the forge measurement tools
// (kan_forge_bench and kan_truth_bench). Standard MIDI files to notes and
// any audio to 48 kHz mono float through miniaudio (MAESTRO is 44.1 kHz
// stereo). Header only, one definition per tool binary.
//
// The miniaudio decoder implementation is compiled exactly once, in
// kan_forge_api.cpp (MINIAUDIO_IMPLEMENTATION). This header only includes
// miniaudio.h and links against the symbols libkan_forge_native exports,
// so no second implementation is ever defined.
//
// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
#ifndef KAN_BENCH_IO_HPP
#define KAN_BENCH_IO_HPP

#include "miniaudio.h"

#include <algorithm>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <map>
#include <string>
#include <vector>

namespace bench_io {

/// One MIDI note: onset and duration in milliseconds from the start of the
/// file. midi is -1 for an unresolved pitch (never produced by read_midi;
/// kept for the found notes of kan_forge_bench).
struct Note {
    double onset_ms;
    double dur_ms;
    int midi;
};

inline std::vector<uint8_t> read_file(const std::string& path) {
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

/// Any audio file miniaudio decodes (WAV and friends, any rate and any
/// channel count), mixed to mono and resampled to 48 kHz float, the rate
/// the live API is fed at.
inline bool read_audio(const std::string& path, std::vector<float>& out) {
    out.clear();
    ma_decoder_config cfg = ma_decoder_config_init(ma_format_f32, 1, 48000);
    ma_decoder dec;
    if (ma_decoder_init_file(path.c_str(), &cfg, &dec) != MA_SUCCESS) return false;
    float chunk[4096];
    for (;;) {
        ma_uint64 got = 0;
        if (ma_decoder_read_pcm_frames(&dec, chunk, 4096, &got) != MA_SUCCESS) break;
        if (got == 0) break;
        out.insert(out.end(), chunk, chunk + got);
    }
    ma_decoder_uninit(&dec);
    return !out.empty();
}

inline uint32_t vlq(const uint8_t*& p, const uint8_t* end) {
    uint32_t v = 0;
    while (p < end) {
        const uint8_t b = *p++;
        v = (v << 7) | (b & 0x7F);
        if ((b & 0x80) == 0) break;
    }
    return v;
}

/// Standard MIDI file (format 0/1, PPQ division, tempo map) to notes in
/// milliseconds, sorted by onset.
inline bool read_midi(const std::string& path, std::vector<Note>& notes) {
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

} // namespace bench_io

#endif // KAN_BENCH_IO_HPP
