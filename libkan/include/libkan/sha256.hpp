#pragma once
// Minimal SHA-256 (FIPS 180-4) for the model-download pin (transcribe.hpp,
// audit C4): one small, allocation-light, dependency-free implementation
// so the shipped separation model can be verified against a pinned digest
// before the GGUF loader sees it. Not real-time code: it runs on the
// offline paths (model acquisition, tooling).
//
// Scope note: the repository had no C++ SHA-256 to reuse (only
// libkan/tools/basic_pitch_export.py, a dev script); this is the one
// implementation, kept here so every consumer shares it.

#include <cstddef>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>

namespace kan {
namespace sha256 {

struct Ctx {
    uint32_t h[8];
    uint64_t total = 0;      // bytes fed so far
    uint8_t buf[64] = {};
    size_t buf_len = 0;
};

inline uint32_t rotr(uint32_t x, int n) {
    return (x >> n) | (x << (32 - n));
}

inline void compress(Ctx& c, const uint8_t* p) {
    static const uint32_t K[64] = {
        0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5,
        0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
        0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3,
        0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
        0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc,
        0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
        0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7,
        0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
        0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13,
        0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
        0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3,
        0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
        0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5,
        0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
        0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208,
        0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2};
    uint32_t w[64];
    for (int i = 0; i < 16; ++i) {
        w[i] = (uint32_t(p[i * 4]) << 24) | (uint32_t(p[i * 4 + 1]) << 16) |
               (uint32_t(p[i * 4 + 2]) << 8) | uint32_t(p[i * 4 + 3]);
    }
    for (int i = 16; i < 64; ++i) {
        const uint32_t s0 = rotr(w[i - 15], 7) ^ rotr(w[i - 15], 18) ^ (w[i - 15] >> 3);
        const uint32_t s1 = rotr(w[i - 2], 17) ^ rotr(w[i - 2], 19) ^ (w[i - 2] >> 10);
        w[i] = w[i - 16] + s0 + w[i - 7] + s1;
    }
    uint32_t a = c.h[0], b = c.h[1], cc = c.h[2], d = c.h[3];
    uint32_t e = c.h[4], f = c.h[5], g = c.h[6], hh = c.h[7];
    for (int i = 0; i < 64; ++i) {
        const uint32_t S1 = rotr(e, 6) ^ rotr(e, 11) ^ rotr(e, 25);
        const uint32_t ch = (e & f) ^ ((~e) & g);
        const uint32_t t1 = hh + S1 + ch + K[i] + w[i];
        const uint32_t S0 = rotr(a, 2) ^ rotr(a, 13) ^ rotr(a, 22);
        const uint32_t maj = (a & b) ^ (a & cc) ^ (b & cc);
        const uint32_t t2 = S0 + maj;
        hh = g; g = f; f = e; e = d + t1;
        d = cc; cc = b; b = a; a = t1 + t2;
    }
    c.h[0] += a; c.h[1] += b; c.h[2] += cc; c.h[3] += d;
    c.h[4] += e; c.h[5] += f; c.h[6] += g; c.h[7] += hh;
}

inline void init(Ctx& c) {
    c.h[0] = 0x6a09e667; c.h[1] = 0xbb67ae85;
    c.h[2] = 0x3c6ef372; c.h[3] = 0xa54ff53a;
    c.h[4] = 0x510e527f; c.h[5] = 0x9b05688c;
    c.h[6] = 0x1f83d9ab; c.h[7] = 0x5be0cd19;
    c.total = 0;
    c.buf_len = 0;
}

inline void update(Ctx& c, const void* data, size_t n) {
    const uint8_t* p = static_cast<const uint8_t*>(data);
    c.total += n;
    while (n > 0) {
        const size_t take = (n < 64 - c.buf_len) ? n : 64 - c.buf_len;
        std::memcpy(c.buf + c.buf_len, p, take);
        c.buf_len += take;
        p += take;
        n -= take;
        if (c.buf_len == 64) {
            compress(c, c.buf);
            c.buf_len = 0;
        }
    }
}

/// Finish and return the digest as lowercase hex.
inline std::string final_hex(Ctx& c) {
    const uint64_t bits = c.total * 8;
    uint8_t pad[72];
    size_t pad_len = (c.buf_len < 56) ? (56 - c.buf_len) : (120 - c.buf_len);
    std::memset(pad, 0, sizeof(pad));
    pad[0] = 0x80;
    for (int i = 0; i < 8; ++i) {
        pad[pad_len + size_t(i)] = uint8_t(bits >> (56 - 8 * i));
    }
    update(c, pad, pad_len + 8);
    static const char* const HEX = "0123456789abcdef";
    char out[64];
    for (int i = 0; i < 8; ++i) {
        for (int b = 0; b < 4; ++b) {
            const uint8_t byte = uint8_t(c.h[i] >> (24 - 8 * b));
            out[size_t(i) * 8 + size_t(b) * 2] = HEX[byte >> 4];
            out[size_t(i) * 8 + size_t(b) * 2 + 1] = HEX[byte & 0x0f];
        }
    }
    return std::string(out, 64);
}

inline std::string hex_of(const void* data, size_t n) {
    Ctx c;
    init(c);
    update(c, data, n);
    return final_hex(c);
}

/// SHA-256 of a file, streamed (a model file is hundreds of MB). "" when
/// the file cannot be read.
inline std::string file_hex(const std::string& path) {
    std::FILE* f = std::fopen(path.c_str(), "rb");
    if (f == nullptr) return "";
    Ctx c;
    init(c);
    uint8_t buf[65536];
    for (;;) {
        const size_t got = std::fread(buf, 1, sizeof(buf), f);
        if (got > 0) update(c, buf, got);
        if (got < sizeof(buf)) break;
    }
    const bool bad = std::ferror(f) != 0;
    std::fclose(f);
    if (bad) return "";
    return final_hex(c);
}

} // namespace sha256
} // namespace kan
