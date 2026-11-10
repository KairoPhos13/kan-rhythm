#pragma once
// Minimal WAV (RIFF) reader/writer for libkan offline tests and fixture
// generation: mono 16-bit PCM, the format kan_cli --loopback and the
// regression harness agree on. Deliberately tiny and dependency-free; the
// production apps use miniaudio's decoder instead.
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>
#include <vector>

namespace kan_test {

namespace wav_detail {

inline void put_u32(std::vector<unsigned char>& v, uint32_t x) {
    v.push_back(static_cast<unsigned char>(x & 0xFF));
    v.push_back(static_cast<unsigned char>((x >> 8) & 0xFF));
    v.push_back(static_cast<unsigned char>((x >> 16) & 0xFF));
    v.push_back(static_cast<unsigned char>((x >> 24) & 0xFF));
}

inline void put_u16(std::vector<unsigned char>& v, uint16_t x) {
    v.push_back(static_cast<unsigned char>(x & 0xFF));
    v.push_back(static_cast<unsigned char>((x >> 8) & 0xFF));
}

inline uint32_t get_u32(const unsigned char* p) {
    return uint32_t(p[0]) | (uint32_t(p[1]) << 8) | (uint32_t(p[2]) << 16) | (uint32_t(p[3]) << 24);
}

inline uint16_t get_u16(const unsigned char* p) {
    return uint16_t(uint32_t(p[0]) | (uint32_t(p[1]) << 8));
}

} // namespace wav_detail

/// Write mono 16-bit PCM samples to `path`. Returns false on I/O failure.
inline bool write_wav(const std::string& path, const std::vector<float>& samples,
                      uint32_t sample_rate) {
    std::vector<unsigned char> b;
    b.reserve(44 + samples.size() * 2);
    const uint32_t data_bytes = uint32_t(samples.size() * 2);
    const char* riff = "RIFF";
    b.insert(b.end(), riff, riff + 4);
    wav_detail::put_u32(b, 36 + data_bytes);
    const char* wave = "WAVE";
    b.insert(b.end(), wave, wave + 4);
    const char* fmt = "fmt ";
    b.insert(b.end(), fmt, fmt + 4);
    wav_detail::put_u32(b, 16);          // PCM fmt chunk size
    wav_detail::put_u16(b, 1);           // PCM
    wav_detail::put_u16(b, 1);           // mono
    wav_detail::put_u32(b, sample_rate);
    wav_detail::put_u32(b, sample_rate * 2); // byte rate
    wav_detail::put_u16(b, 2);           // block align
    wav_detail::put_u16(b, 16);          // bits per sample
    const char* data = "data";
    b.insert(b.end(), data, data + 4);
    wav_detail::put_u32(b, data_bytes);
    for (float s : samples) {
        float c = s < -1.0f ? -1.0f : (s > 1.0f ? 1.0f : s);
        const int16_t q = int16_t(c * 32767.0f);
        wav_detail::put_u16(b, uint16_t(int32_t(q)));
    }
    FILE* f = std::fopen(path.c_str(), "wb");
    if (!f) return false;
    const size_t n = std::fwrite(b.data(), 1, b.size(), f);
    std::fclose(f);
    return n == b.size();
}

/// Read a mono/stereo (downmixed) 16-bit PCM WAV into floats.
/// Returns false on parse/IO failure.
inline bool read_wav(const std::string& path, std::vector<float>& samples,
                     uint32_t& sample_rate) {
    FILE* f = std::fopen(path.c_str(), "rb");
    if (!f) return false;
    std::fseek(f, 0, SEEK_END);
    const long len = std::ftell(f);
    std::fseek(f, 0, SEEK_SET);
    if (len < 44) { std::fclose(f); return false; }
    std::vector<unsigned char> b;
    b.resize(size_t(len));
    const size_t got = std::fread(b.data(), 1, b.size(), f);
    std::fclose(f);
    if (got != b.size()) return false;

    if (std::memcmp(b.data(), "RIFF", 4) != 0 || std::memcmp(b.data() + 8, "WAVE", 4) != 0) {
        return false;
    }
    size_t pos = 12;
    uint16_t channels = 0, bits = 0, fmt_tag = 0;
    uint32_t rate = 0;
    bool have_fmt = false, have_data = false;
    const unsigned char* data_ptr = nullptr;
    uint32_t data_bytes = 0;
    while (pos + 8 <= b.size()) {
        const unsigned char* hdr = b.data() + pos;
        const uint32_t chunk = wav_detail::get_u32(hdr + 4);
        if (std::memcmp(hdr, "fmt ", 4) == 0 && chunk >= 16 && pos + 8 + 16 <= b.size()) {
            fmt_tag = wav_detail::get_u16(hdr + 8);
            channels = wav_detail::get_u16(hdr + 10);
            rate = wav_detail::get_u32(hdr + 12);
            bits = wav_detail::get_u16(hdr + 22);
            have_fmt = true;
        } else if (std::memcmp(hdr, "data", 4) == 0) {
            data_ptr = hdr + 8;
            data_bytes = uint32_t(b.size() - (pos + 8) < chunk ? b.size() - (pos + 8) : chunk);
            have_data = true;
        }
        pos += 8 + chunk + (chunk & 1u); // chunks are word-aligned
    }
    // 16-bit PCM or 32-bit IEEE float, mono/stereo (downmixed).
    const bool fmt_ok = (fmt_tag == 1 && bits == 16) || (fmt_tag == 3 && bits == 32);
    if (!have_fmt || !have_data || !fmt_ok || channels < 1) return false;

    sample_rate = rate;
    const size_t bytes_per = size_t(bits / 8);
    const size_t frames = data_bytes / (bytes_per * channels);
    samples.resize(frames);
    for (size_t i = 0; i < frames; ++i) {
        float acc = 0.0f;
        for (uint16_t c = 0; c < channels; ++c) {
            const unsigned char* p = data_ptr + (i * channels + c) * bytes_per;
            if (bits == 16) {
                acc += float(int16_t(wav_detail::get_u16(p))) / 32768.0f;
            } else {
                float v = 0.0f;
                std::memcpy(&v, p, sizeof(v));
                acc += v;
            }
        }
        samples[i] = acc / float(channels);
    }
    return true;
}

} // namespace kan_test
