#pragma once
// Minimal Standard MIDI File (SMF) reader/writer — the ground-truth side of
// dataset benchmarking. Extracts note on/off pairs with millisecond accuracy
// through the tempo map. Durations are key-hold times (note_off), which is
// exactly what the release-bias invariant (measurement-invariants.md I8) is
// measured against: do NOT apply sustain pedal to durations here.
//
// Scope: format 0/1, metas tempo (FF 51) and end-of-track, sysex skipped,
// running status supported. SMPTE division is rejected (not used by MAESTRO).
#include <algorithm>
#include <array>
#include <cstdint>
#include <cstring>
#include <map>
#include <queue>
#include <string>
#include <utility>
#include <vector>

namespace dataset_bench {

struct MidiNote {
    double onset_ms = 0.0;
    double dur_ms = 0.0;
    int midi = -1;
    int vel = 0;  ///< strike velocity (note_on): dynamics ground truth
};

namespace detail {

inline uint16_t rd_u16(const uint8_t* p) {
    return uint16_t((uint16_t(p[0]) << 8) | p[1]);
}
inline uint32_t rd_u32(const uint8_t* p) {
    return (uint32_t(p[0]) << 24) | (uint32_t(p[1]) << 16) |
           (uint32_t(p[2]) << 8) | uint32_t(p[3]);
}
inline uint64_t rd_vlq(const uint8_t* data, size_t end, size_t& p) {
    uint64_t v = 0;
    for (int i = 0; i < 4 && p < end; ++i) {
        const uint8_t b = data[p++];
        v = (v << 7) | uint64_t(b & 0x7F);
        if (!(b & 0x80)) break;
    }
    return v;
}
inline void wr_vlq(std::vector<uint8_t>& o, uint32_t v) {
    uint8_t buf[5];
    int n = 0;
    buf[n++] = uint8_t(v & 0x7F);
    while (v >>= 7) buf[n++] = uint8_t(0x80 | (v & 0x7F));
    while (n--) o.push_back(buf[n]);
}

} // namespace detail

/// Parse SMF bytes into notes sorted by onset (FIFO pairing per channel+pitch,
/// so overlapping strikes of the same key resolve in order). False on a
/// malformed file.
inline bool parse_smf(const std::vector<uint8_t>& data, std::vector<MidiNote>& out,
                      std::string* err = nullptr) {
    auto fail = [&](const char* m) {
        if (err != nullptr) *err = m;
        return false;
    };
    if (data.size() < 14 || std::memcmp(data.data(), "MThd", 4) != 0) {
        return fail("not an SMF file");
    }
    const uint32_t hdr_len = detail::rd_u32(&data[4]);
    if (hdr_len < 6 || 8 + hdr_len > data.size()) return fail("bad MThd length");
    const uint16_t ntrks = detail::rd_u16(&data[10]);
    const uint16_t division = detail::rd_u16(&data[12]);
    if (division & 0x8000) return fail("SMPTE time division not supported");
    const double tpq = double(division);

    // Global tempo map (tick, us per quarter note); default 500000 at 0.
    std::vector<std::pair<uint64_t, double>> tempo;
    tempo.push_back({0, 500000.0});

    struct Raw {
        uint64_t tick;
        uint8_t status, d1, d2;
    };
    std::vector<Raw> raw;

    size_t pos = 8 + hdr_len;
    for (uint16_t tr = 0; tr < ntrks && pos + 8 <= data.size(); ++tr) {
        if (std::memcmp(&data[pos], "MTrk", 4) != 0) return fail("missing MTrk");
        const uint32_t tlen = detail::rd_u32(&data[pos + 4]);
        size_t p = pos + 8;
        const size_t pend = std::min(data.size(), p + size_t(tlen));
        uint64_t tick = 0;
        uint8_t running = 0;
        while (p < pend) {
            tick += detail::rd_vlq(data.data(), pend, p);
            if (p >= pend) break;
            uint8_t st = data[p];
            if (st & 0x80) {
                ++p;
                running = (st < 0xF0) ? st : 0;
            } else if (running != 0) {
                st = running;
            } else {
                return fail("dangling data byte");
            }
            if (st == 0xFF) {
                if (p >= pend) break;
                const uint8_t type = data[p++];
                const uint64_t len = detail::rd_vlq(data.data(), pend, p);
                if (type == 0x51 && len == 3 && p + 3 <= pend) {
                    const double us = double((uint32_t(data[p]) << 16) |
                                             (uint32_t(data[p + 1]) << 8) | data[p + 2]);
                    tempo.push_back({tick, us});
                }
                p += std::min<size_t>(len, pend - p);
                running = 0;
            } else if (st == 0xF0 || st == 0xF7) {
                const uint64_t len = detail::rd_vlq(data.data(), pend, p);
                p += std::min<size_t>(len, pend - p);
                running = 0;
            } else {
                const uint8_t hi = uint8_t(st & 0xF0);
                const int ndata = (hi == 0xC0 || hi == 0xD0) ? 1 : 2;
                uint8_t d1 = 0, d2 = 0;
                if (p < pend) d1 = data[p++];
                if (ndata == 2 && p < pend) d2 = data[p++];
                raw.push_back({tick, st, d1, d2});
            }
        }
        pos = pend;
    }

    std::stable_sort(tempo.begin(), tempo.end(),
                     [](const auto& a, const auto& b) { return a.first < b.first; });

    auto tick_to_ms = [&](uint64_t tick) {
        double ms = 0.0;
        uint64_t prev = 0;
        double us = 500000.0;
        for (const auto& [t, u] : tempo) {
            if (t >= tick) break;
            ms += double(t - prev) * us / 1000.0 / tpq;
            prev = t;
            us = u;
        }
        return ms + double(tick - prev) * us / 1000.0 / tpq;
    };

    struct Open {
        uint64_t tick;
        uint8_t vel;
    };
    std::map<int, std::queue<Open>> open;
    out.clear();
    for (const auto& r : raw) {
        const uint8_t hi = uint8_t(r.status & 0xF0);
        const int key = (int(r.status & 0x0F) << 8) | int(r.d1);
        if (hi == 0x90 && r.d2 > 0) {
            open[key].push({r.tick, r.d2});
        } else if (hi == 0x80 || (hi == 0x90 && r.d2 == 0)) {
            auto it = open.find(key);
            if (it != open.end() && !it->second.empty()) {
                const uint64_t on_tick = it->second.front().tick;
                const uint8_t on_vel = it->second.front().vel;
                it->second.pop();
                MidiNote n;
                n.midi = int(r.d1);
                n.vel = int(on_vel);
                n.onset_ms = tick_to_ms(on_tick);
                n.dur_ms = std::max(1.0, tick_to_ms(r.tick) - n.onset_ms);
                out.push_back(n);
            }
        }
    }
    std::sort(out.begin(), out.end(), [](const MidiNote& a, const MidiNote& b) {
        return a.onset_ms != b.onset_ms ? a.onset_ms < b.onset_ms : a.midi < b.midi;
    });
    return !out.empty();
}

/// Write a format-0 SMF at fixed 500 ms per quarter note (round-trippable by
/// parse_smf; used by --selftest and for dataset fixtures).
inline std::vector<uint8_t> write_smf(const std::vector<MidiNote>& notes, int tpq = 480) {
    std::vector<std::pair<uint64_t, std::array<uint8_t, 3>>> ev;
    for (const auto& n : notes) {
        const uint64_t on = uint64_t(n.onset_ms * double(tpq) / 500.0 + 0.5);
        const uint64_t off = uint64_t((n.onset_ms + n.dur_ms) * double(tpq) / 500.0 + 0.5);
        ev.push_back({on, {0x90, uint8_t(n.midi), 80}});
        ev.push_back({std::max(off, on + 1), {0x80, uint8_t(n.midi), 0}});
    }
    std::stable_sort(ev.begin(), ev.end(),
                     [](const auto& a, const auto& b) { return a.first < b.first; });

    std::vector<uint8_t> track;
    uint64_t prev = 0;
    for (const auto& [tick, msg] : ev) {
        detail::wr_vlq(track, uint32_t(tick - prev));
        prev = tick;
        track.insert(track.end(), msg.begin(), msg.end());
    }
    detail::wr_vlq(track, 0);
    track.insert(track.end(), {0xFF, 0x2F, 0x00});

    std::vector<uint8_t> f = {'M', 'T', 'h', 'd', 0, 0, 0, 6,
                              0, 0, 0, 1,
                              uint8_t(tpq >> 8), uint8_t(tpq & 0xFF)};
    const uint32_t tl = uint32_t(track.size());
    const uint8_t thdr[8] = {'M', 'T', 'r', 'k',
                             uint8_t(tl >> 24), uint8_t(tl >> 16),
                             uint8_t(tl >> 8), uint8_t(tl)};
    f.insert(f.end(), thdr, thdr + 8);
    f.insert(f.end(), track.begin(), track.end());
    return f;
}

} // namespace dataset_bench
