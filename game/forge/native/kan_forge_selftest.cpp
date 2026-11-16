// kan_forge_selftest: offline test of the C-ABI bridge with known input
// and expected output (repo rule 5): synthesize a melody, write a WAV,
// transcribe it through kan_forge_transcribe_file and check the facts.
//
// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
#include "kan_forge_api.h"

#include <algorithm>
#include <cmath>
#include <cstdlib>
#include <cstdio>
#include <cstdint>
#include <cstring>
#include <string>
#include <vector>

namespace {

// Minimal PCM16 interleaved WAV writer (48 kHz), no external dependencies.
// One channel by default: the callers writing a melody pass mono samples.
bool write_wav(const std::string& path, const std::vector<float>& sig, int sr, int channels = 1) {
    std::FILE* f = std::fopen(path.c_str(), "wb");
    if (f == nullptr) return false;
    const uint32_t data_bytes = uint32_t(sig.size() * 2);
    const uint32_t byte_rate = uint32_t(sr) * 2 * uint32_t(channels);
    const uint16_t block_align = uint16_t(2 * channels);
    const uint16_t bits = 16;
    std::fwrite("RIFF", 1, 4, f);
    const uint32_t riff_size = 36 + data_bytes;
    std::fwrite(&riff_size, 4, 1, f);
    std::fwrite("WAVEfmt ", 1, 8, f);
    const uint32_t fmt_size = 16;
    std::fwrite(&fmt_size, 4, 1, f);
    const uint16_t format = 1;
    const uint16_t channels_field = uint16_t(channels);
    std::fwrite(&format, 2, 1, f);
    std::fwrite(&channels_field, 2, 1, f);
    const uint32_t sr_u = uint32_t(sr);
    std::fwrite(&sr_u, 4, 1, f);
    std::fwrite(&byte_rate, 4, 1, f);
    std::fwrite(&block_align, 2, 1, f);
    std::fwrite(&bits, 2, 1, f);
    std::fwrite("data", 1, 4, f);
    std::fwrite(&data_bytes, 4, 1, f);
    for (float s : sig) {
        float v = s < -1.0f ? -1.0f : (s > 1.0f ? 1.0f : s);
        const int16_t pcm = int16_t(v * 32767.0f);
        std::fwrite(&pcm, 2, 1, f);
    }
    std::fclose(f);
    return true;
}

// Reader for the PCM16 WAV files written above (self-test round-trip):
// every sample, interleaved when the file has more than one channel.
bool read_wav(const std::string& path, std::vector<float>& sig) {
    std::FILE* f = std::fopen(path.c_str(), "rb");
    if (f == nullptr) return false;
    std::fseek(f, 0, SEEK_END);
    const long size = std::ftell(f);
    std::fseek(f, 44, SEEK_SET); // header of the files write_wav produces
    for (long pos = 44; pos + 1 < size; pos += 2) {
        int16_t pcm = 0;
        if (std::fread(&pcm, 2, 1, f) != 1) break;
        sig.push_back(float(pcm) / 32767.0f);
    }
    std::fclose(f);
    return !sig.empty();
}

// Known input: C5 D5 E5 (500 ms each) + vibrato G5, same shape as
// tools/chart_scribe --selftest. Expected: at least 3 struck notes.
std::vector<float> synth_melody(float sr) {
    std::vector<float> sig(size_t(3.2f * sr), 0.0f);
    struct P { float t, d, hz, vib; };
    const P perf[] = {{0.3f, 0.5f, 523.25f, 0.0f}, {1.0f, 0.5f, 587.33f, 0.0f},
                      {1.7f, 0.5f, 659.25f, 0.0f}, {2.4f, 0.6f, 783.99f, 5.0f}};
    for (const auto& p : perf) {
        const size_t at = size_t(p.t * sr), n = size_t(p.d * sr);
        for (size_t i = 0; at + i < sig.size() && i < n; ++i) {
            const float t = float(i) / sr;
            const float dev = (p.vib > 0.0f)
                                  ? (p.hz * 0.03f / p.vib) * std::sin(6.2831853f * p.vib * t)
                                  : 0.0f;
            float s = 0.0f;
            for (int h = 1; h <= 5; ++h)
                s += std::sin(6.2831853f * (p.hz + dev) * h * t) / float(h);
            // 10 ms fade in/out to avoid click artifacts.
            const float env = (i < n / 50 ? float(i) / float(n / 50)
                                          : (i > n - n / 50 ? float(n - i) / float(n / 50) : 1.0f));
            sig[at + i] += 0.25f * s * env;
        }
    }
    return sig;
}

} // namespace

int main() {
    int failures = 0;

    const float sr = 48000.0f;
    const std::string wav = "kan_forge_selftest.wav";
    if (!write_wav(wav, synth_melody(sr), int(sr))) {
        std::fprintf(stderr, "SELFTEST FAIL: cannot write %s\n", wav.c_str());
        return 1;
    }

    // 1. Transcription through the C ABI: known input -> expected output.
    std::vector<kan_forge_note> notes(256);
    int32_t count = 0;
    char err[512] = {0};
    kan_forge_status st =
        kan_forge_transcribe_file(wav.c_str(), "keyboard", notes.data(),
                                  int32_t(notes.size()), &count, err, sizeof(err));
    if (st != KAN_FORGE_OK) {
        std::fprintf(stderr, "SELFTEST FAIL: transcribe status %d (%s)\n", int(st), err);
        ++failures;
    } else if (count < 3) {
        std::fprintf(stderr, "SELFTEST FAIL: expected >= 3 notes, got %d\n", count);
        ++failures;
    } else {
        // First note must be near C5 (523 Hz -> midi 72) at ~300 ms.
        if (notes[0].midi != 72 && notes[0].midi != 71 && notes[0].midi != 73) {
            std::fprintf(stderr, "SELFTEST FAIL: first note midi %d, expected ~72\n", notes[0].midi);
            ++failures;
        }
        if (notes[0].onset_ms < 250.0f || notes[0].onset_ms > 350.0f) {
            std::fprintf(stderr, "SELFTEST FAIL: first onset %.1f ms, expected ~300\n",
                         notes[0].onset_ms);
            ++failures;
        }
        std::printf("selftest: %d notes, first midi=%d onset=%.1f ms dyn=%.1f dB\n",
                    count, notes[0].midi, notes[0].onset_ms, notes[0].dyn_db);
    }

    // 2. Error path: missing file is a first-class decode error.
    count = 0;
    st = kan_forge_transcribe_file("does_not_exist.wav", "keyboard", notes.data(),
                                   int32_t(notes.size()), &count, err, sizeof(err));
    if (st != KAN_FORGE_ERR_DECODE) {
        std::fprintf(stderr, "SELFTEST FAIL: missing file should be ERR_DECODE, got %d\n", int(st));
        ++failures;
    }

    // 3. Capacity path: too-small buffer returns the required count.
    kan_forge_note one = {};
    count = 0;
    st = kan_forge_transcribe_file(wav.c_str(), "keyboard", &one, 1, &count, err, sizeof(err));
    if (st != KAN_FORGE_ERR_CAPACITY || count < 3) {
        std::fprintf(stderr, "SELFTEST FAIL: capacity path wrong (status %d count %d)\n", int(st),
                     count);
        ++failures;
    }

    // 4. Mixdown: two identical tracks at gain 0.5 must sum back to the
    //    original signal ("render a single track for playback").
    const std::string wav_b = "kan_forge_selftest_b.wav";
    const std::string mix_out = "kan_forge_selftest_mix.wav";
    const std::vector<float> melody = synth_melody(sr);
    if (!write_wav(wav_b, melody, int(sr))) {
        std::fprintf(stderr, "SELFTEST FAIL: cannot write %s\n", wav_b.c_str());
        return 1;
    }
    {
        const char* paths[2] = {wav.c_str(), wav_b.c_str()};
        const float gains[2] = {0.5f, 0.5f};
        st = kan_forge_mix_tracks(paths, gains, 2, mix_out.c_str(), err, sizeof(err));
        if (st != KAN_FORGE_OK) {
            std::fprintf(stderr, "SELFTEST FAIL: mix status %d (%s)\n", int(st), err);
            ++failures;
        } else {
            std::vector<float> got;
            if (!read_wav(mix_out, got)) {
                std::fprintf(stderr, "SELFTEST FAIL: cannot read mix output\n");
                ++failures;
            } else if (got.size() != melody.size()) {
                std::fprintf(stderr, "SELFTEST FAIL: mix length %zu != %zu\n", got.size(),
                             melody.size());
                ++failures;
            } else {
                float peak_in = 0.0f, peak_out = 0.0f;
                for (size_t i = 0; i < melody.size(); ++i) {
                    peak_in = std::fabs(melody[i]) > peak_in ? std::fabs(melody[i]) : peak_in;
                    peak_out = std::fabs(got[i]) > peak_out ? std::fabs(got[i]) : peak_out;
                }
                // PCM16 quantisation: allow 1% tolerance.
                if (std::fabs(peak_out - peak_in) > 0.01f * (peak_in + 1e-3f)) {
                    std::fprintf(stderr,
                                 "SELFTEST FAIL: mix peak %.4f != source peak %.4f (gains 0.5+0.5)\n",
                                 peak_out, peak_in);
                    ++failures;
                } else {
                    std::printf("selftest: mix peak %.4f == source peak %.4f\n", peak_out, peak_in);
                }
            }
        }
    }

    // 4b. Mixdown on a timeline: one track placed at 500 ms must come out
    //     as 500 ms of silence followed by the unchanged signal.
    {
        const std::string placed = "kan_forge_selftest_mix_at.wav";
        const char* paths[1] = {wav_b.c_str()};
        const float gains[1] = {1.0f};
        const double offsets[1] = {500.0};
        st = kan_forge_mix_tracks_at(paths, gains, offsets, 1, placed.c_str(), err, sizeof(err));
        const size_t lead = size_t(0.5 * sr);
        std::vector<float> got;
        if (st != KAN_FORGE_OK) {
            std::fprintf(stderr, "SELFTEST FAIL: mix_at status %d (%s)\n", int(st), err);
            ++failures;
        } else if (!read_wav(placed, got) || got.size() != lead + melody.size()) {
            std::fprintf(stderr, "SELFTEST FAIL: mix_at length %zu != %zu\n", got.size(),
                         lead + melody.size());
            ++failures;
        } else {
            float lead_peak = 0.0f, err_max = 0.0f;
            for (size_t i = 0; i < lead; ++i)
                lead_peak = std::fabs(got[i]) > lead_peak ? std::fabs(got[i]) : lead_peak;
            for (size_t i = 0; i < melody.size(); ++i) {
                const float d = std::fabs(got[lead + i] - melody[i]);
                err_max = d > err_max ? d : err_max;
            }
            // PCM16 quantisation tolerance.
            if (lead_peak > 1e-4f || err_max > 2e-3f) {
                std::fprintf(stderr, "SELFTEST FAIL: mix_at lead peak %.5f, max error %.5f\n",
                             lead_peak, err_max);
                ++failures;
            } else {
                std::printf("selftest: mix_at places the track at 500 ms (max error %.5f)\n", err_max);
            }
        }

        const double bad[1] = {-1.0};
        if (kan_forge_mix_tracks_at(paths, gains, bad, 1, placed.c_str(), err, sizeof(err)) != KAN_FORGE_ERR_ARGS) {
            std::fprintf(stderr, "SELFTEST FAIL: mix_at accepted a negative offset\n");
            ++failures;
        }
    }

    // 5. Track editor: KEEP / MUTE / TRIM on known audio.
    {
        const std::string edited = "kan_forge_selftest_edit.wav";
        auto read_peak = [&](float t0, float t1) {
            std::vector<float> v;
            if (!read_wav(edited, v)) return -1.0f;
            const size_t a = size_t(t0 * 0.001f * sr), b = size_t(t1 * 0.001f * sr);
            float p = 0.0f;
            for (size_t i = a; i < b && i < v.size(); ++i)
                p = std::fabs(v[i]) > p ? std::fabs(v[i]) : p;
            return p;
        };

        // MUTE [0, 300): the melody's first note region is silenced.
        st = kan_forge_edit_track(wav.c_str(), 0.0f, 300.0f, KAN_FORGE_EDIT_MUTE, edited.c_str(), err,
                                  sizeof(err));
        if (st != KAN_FORGE_OK || read_peak(0, 290) != 0.0f || read_peak(400, 900) <= 0.0f) {
            std::fprintf(stderr, "SELFTEST FAIL: MUTE range wrong (status %d)\n", int(st));
            ++failures;
        }

        // KEEP [1000, 1700): only the second note survives.
        st = kan_forge_edit_track(wav.c_str(), 1000.0f, 1700.0f, KAN_FORGE_EDIT_KEEP, edited.c_str(), err,
                                  sizeof(err));
        if (st != KAN_FORGE_OK || read_peak(0, 900) != 0.0f || read_peak(1100, 1600) <= 0.0f
            || read_peak(2000, 2900) != 0.0f) {
            std::fprintf(stderr, "SELFTEST FAIL: KEEP range wrong (status %d)\n", int(st));
            ++failures;
        }

        // TRIM [1000, 1700): tight 700 ms crop (offset lost by design).
        st = kan_forge_edit_track(wav.c_str(), 1000.0f, 1700.0f, KAN_FORGE_EDIT_TRIM, edited.c_str(), err,
                                  sizeof(err));
        std::vector<float> trimmed;
        if (st != KAN_FORGE_OK || !read_wav(edited, trimmed)
            || trimmed.size() != size_t(0.7f * sr)) {
            std::fprintf(stderr, "SELFTEST FAIL: TRIM length wrong (status %d size %zu)\n", int(st),
                         trimmed.size());
            ++failures;
        }
    }

    // 6. Analysis: waveform buckets + meaningful-audio levels.
    {
        float peak = 0.0f, rms = 0.0f;
        float buckets[64];
        st = kan_forge_analyze_track(wav.c_str(), buckets, 64, &peak, &rms, err, sizeof(err));
        if (st != KAN_FORGE_OK || peak <= 0.2f || rms <= 0.05f) {
            std::fprintf(stderr, "SELFTEST FAIL: analyze loud wrong (peak %.4f rms %.4f)\n", peak, rms);
            ++failures;
        } else {
            // The melody (0.3-0.8 / 1.0-1.5 / 1.7-2.2 / 2.4-3.0 s over 3.2 s
            // at 64 buckets = 50 ms each) has a silent gap at 0.85-0.9 s.
            float before = buckets[12], gap = buckets[17], after = buckets[22];
            if (!(before > 0.0f && gap < before * 0.25f && after > 0.0f)) {
                std::fprintf(stderr, "SELFTEST FAIL: waveform buckets wrong (%.4f %.4f %.4f)\n",
                             before, gap, after);
                ++failures;
            }
        }

        // A silent file must analyse as empty.
        std::vector<float> silence(size_t(0.5f * sr), 0.0f);
        write_wav("kan_forge_selftest_silence.wav", silence, int(sr));
        st = kan_forge_analyze_track("kan_forge_selftest_silence.wav", nullptr, 0, &peak, &rms, err,
                                     sizeof(err));
        if (st != KAN_FORGE_OK || peak != 0.0f || rms != 0.0f) {
            std::fprintf(stderr, "SELFTEST FAIL: silence not empty (peak %.6f rms %.6f)\n", peak, rms);
            ++failures;
        }
    }

    // 7. Decode to WAV: the source's rate and channels kept, every frame
    //    written; a missing file is a controlled error.
    {
        const std::string decoded = "kan_forge_selftest_decoded.wav";
        st = kan_forge_decode_to_wav(wav.c_str(), decoded.c_str(), err, sizeof(err));
        std::vector<float> in, out;
        if (st != KAN_FORGE_OK || !read_wav(wav, in) || !read_wav(decoded, out) || in.size() != out.size()) {
            std::fprintf(stderr, "SELFTEST FAIL: decode to wav (status %d, %zu vs %zu samples) %s\n",
                         int(st), in.size(), out.size(), err);
            ++failures;
        }
        st = kan_forge_decode_to_wav("definitely-not-here.flac", decoded.c_str(), err, sizeof(err));
        if (st != KAN_FORGE_ERR_DECODE) {
            std::fprintf(stderr, "SELFTEST FAIL: decode of a missing file gave status %d\n", int(st));
            ++failures;
        }
    }

    // 8. Ogg Vorbis (issue 42): the melody encoded is a real Ogg file,
    //    smaller than the WAV, the same bytes every time; decoded back (the
    //    decoder reads Ogg through stb_vorbis) it keeps its length and its
    //    sound, and gives the same notes. Bad input is a controlled error.
    {
        const std::string ogg = "kan_forge_selftest.ogg", ogg2 = "kan_forge_selftest_again.ogg";
        const std::string back = "kan_forge_selftest_from_ogg.wav";
        st = kan_forge_encode_ogg(wav.c_str(), ogg.c_str(), 0.6f, err, sizeof(err));
        auto bytes = [](const std::string& path) {
            std::vector<char> b;
            if (std::FILE* f = std::fopen(path.c_str(), "rb")) {
                char buf[4096];
                size_t n;
                while ((n = std::fread(buf, 1, sizeof buf, f)) > 0) b.insert(b.end(), buf, buf + n);
                std::fclose(f);
            }
            return b;
        };
        const std::vector<char> encoded = bytes(ogg), source = bytes(wav);
        if (st != KAN_FORGE_OK || encoded.size() < 4 || std::memcmp(encoded.data(), "OggS", 4) != 0
            || encoded.size() * 3 > source.size()) {
            std::fprintf(stderr, "SELFTEST FAIL: encode ogg (status %d, %zu bytes from %zu) %s\n", int(st),
                         encoded.size(), source.size(), err);
            ++failures;
        }

        st = kan_forge_encode_ogg(wav.c_str(), ogg2.c_str(), 0.6f, err, sizeof(err));
        if (st != KAN_FORGE_OK || bytes(ogg2) != encoded) {
            std::fprintf(stderr, "SELFTEST FAIL: encoding the same audio twice gave different bytes\n");
            ++failures;
        }

        std::vector<float> in, out;
        st = kan_forge_decode_to_wav(ogg.c_str(), back.c_str(), err, sizeof(err));
        if (st != KAN_FORGE_OK || !read_wav(wav, in) || !read_wav(back, out) || in.size() != out.size()) {
            std::fprintf(stderr, "SELFTEST FAIL: ogg decode (status %d, %zu vs %zu samples) %s\n", int(st),
                         in.size(), out.size(), err);
            ++failures;
        } else {
            double signal = 0, noise = 0;
            for (size_t i = 0; i < in.size(); ++i) {
                signal += double(in[i]) * in[i];
                noise += double(in[i] - out[i]) * (in[i] - out[i]);
            }
            const double snr_db = 10.0 * std::log10(signal / std::max(noise, 1e-12));
            if (!(snr_db > 30.0)) {
                std::fprintf(stderr, "SELFTEST FAIL: ogg round trip too far from the source (%.1f dB)\n", snr_db);
                ++failures;
            }
        }

        kan_forge_note from_wav[64], from_ogg[64];
        int32_t n_wav = 0, n_ogg = 0;
        kan_forge_transcribe_file(wav.c_str(), "keyboard", from_wav, 64, &n_wav, err, sizeof(err));
        kan_forge_transcribe_file(ogg.c_str(), "keyboard", from_ogg, 64, &n_ogg, err, sizeof(err));
        bool same = n_wav > 0 && n_wav == n_ogg;
        for (int32_t k = 0; same && k < n_wav; ++k)
            same = from_wav[k].midi == from_ogg[k].midi && std::fabs(from_wav[k].onset_ms - from_ogg[k].onset_ms) <= 10.0f;
        if (!same) {
            std::fprintf(stderr, "SELFTEST FAIL: the ogg file transcribes differently (%d vs %d notes)\n", n_wav, n_ogg);
            ++failures;
        }

        if (kan_forge_encode_ogg("definitely-not-here.flac", ogg2.c_str(), 0.6f, err, sizeof(err)) != KAN_FORGE_ERR_DECODE
            || kan_forge_encode_ogg(wav.c_str(), ogg2.c_str(), 2.0f, err, sizeof(err)) != KAN_FORGE_ERR_ARGS) {
            std::fprintf(stderr, "SELFTEST FAIL: bad encode input not refused\n");
            ++failures;
        }
    }

    // 8b. Stems (issue 42): the separated stems travel as Ogg Vorbis at
    //     quality 0.8, source material for other levels. A stereo signal
    //     with different content per channel keeps its frame count and its
    //     channels apart: each decoded channel stays above 30 dB SNR
    //     against its own source and clearly worse against the other
    //     channel. Quality 0.8 is at least as close to the source as the
    //     played files at 0.6.
    {
        const int channels = 2;
        const std::string stereo = "kan_forge_selftest_stereo.wav";
        std::vector<float> left(size_t(2.0f * sr)), right(size_t(2.0f * sr));
        for (size_t i = 0; i < left.size(); ++i) {
            // Two different harmonic stacks (220 Hz and 370 Hz share no
            // partial below 8 kHz), one with vibrato: music-like content,
            // each channel its own sound.
            const float t = float(i) / sr;
            const float env_left = 0.5f + 0.5f * std::sin(6.2831853f * 0.7f * t);
            const float env_right = 0.5f + 0.5f * std::sin(6.2831853f * 1.3f * t);
            const float vib = 2.0f * std::sin(6.2831853f * 2.0f * t);
            float l = 0, r = 0;
            for (int h = 1; h <= 5; ++h) {
                l += 0.3f * std::sin(6.2831853f * (220.0f + vib) * h * t) / float(h);
                r += 0.25f * std::sin(6.2831853f * 370.0f * h * t) / float(h);
            }
            left[i] = l * env_left;
            right[i] = r * env_right;
        }
        std::vector<float> source;
        source.reserve(left.size() * 2);
        for (size_t i = 0; i < left.size(); ++i) {
            source.push_back(left[i]);
            source.push_back(right[i]);
        }
        if (!write_wav(stereo, source, int(sr), channels)) {
            std::fprintf(stderr, "SELFTEST FAIL: cannot write %s\n", stereo.c_str());
            ++failures;
        }

        // Per-channel SNR of a decoded interleaved signal against the source.
        auto snr_db = [&](const std::vector<float>& decoded, int channel) {
            double signal = 0, noise = 0;
            for (size_t i = size_t(channel); i < source.size() && i < decoded.size(); i += size_t(channels)) {
                signal += double(source[i]) * source[i];
                const double d = double(source[i]) - double(decoded[i]);
                noise += d * d;
            }
            return 10.0 * std::log10(signal / std::max(noise, 1e-12));
        };

        auto decode_back = [&](const std::string& encoded, float quality, std::vector<float>& decoded) {
            const std::string ogg = encoded + std::to_string(int(quality * 10)) + ".ogg";
            const std::string back = encoded + std::to_string(int(quality * 10)) + "-back.wav";
            const kan_forge_status status = kan_forge_encode_ogg(stereo.c_str(), ogg.c_str(), quality, err, sizeof(err));
            decoded.clear();
            if (status != KAN_FORGE_OK || kan_forge_decode_to_wav(ogg.c_str(), back.c_str(), err, sizeof(err)) != KAN_FORGE_OK
                || !read_wav(back, decoded)) {
                std::fprintf(stderr, "SELFTEST FAIL: stem round trip at %.1f (status %d) %s\n", double(quality), int(status), err);
                ++failures;
                return false;
            }
            return true;
        };

        std::vector<float> at08, at06;
        if (decode_back("kan_forge_selftest_stem", 0.8f, at08) && decode_back("kan_forge_selftest_stem", 0.6f, at06)) {
            // The exact frame count, and the channels kept apart.
            if (at08.size() != source.size() || at08.size() / 2 != left.size()) {
                std::fprintf(stderr, "SELFTEST FAIL: stem frame count %zu vs %zu frames\n", at08.size() / 2, left.size());
                ++failures;
            }

            const double own_left = snr_db(at08, 0), own_right = snr_db(at08, 1);

            // The other channel's content lives at the odd (right) samples;
            // measured against it, the same decoded channel must look far
            // worse (the channels do not bleed into each other).
            double cross_left = 0, cross_right = 0;
            {
                double signal = 0, noise = 0;
                for (size_t i = 0; i < source.size() && i < at08.size(); i += 2) {
                    signal += double(source[i]) * source[i];
                    const double d = double(source[i]) - double(at08[i + 1]);
                    noise += d * d;
                }
                cross_left = 10.0 * std::log10(signal / std::max(noise, 1e-12));

                signal = 0, noise = 0;
                for (size_t i = 1; i < source.size() && i < at08.size(); i += 2) {
                    signal += double(source[i]) * source[i];
                    const double d = double(source[i]) - double(at08[i - 1]);
                    noise += d * d;
                }
                cross_right = 10.0 * std::log10(signal / std::max(noise, 1e-12));
            }

            if (!(own_left > 30.0) || !(own_right > 30.0) || !(own_left > cross_left + 20.0) || !(own_right > cross_right + 20.0)) {
                std::fprintf(stderr, "SELFTEST FAIL: stem channels not kept apart (own %.1f/%.1f dB, cross %.1f/%.1f dB)\n",
                             own_left, own_right, cross_left, cross_right);
                ++failures;
            }

            // Quality 0.8 (the stems) is at least as close to the source as
            // 0.6 (the played files): the source material never sounds worse.
            const double at08_snr = 0.5 * (own_left + own_right);
            const double at06_snr = 0.5 * (snr_db(at06, 0) + snr_db(at06, 1));
            if (!(at08_snr + 0.01 >= at06_snr)) {
                std::fprintf(stderr, "SELFTEST FAIL: stem quality 0.8 (%.1f dB) below 0.6 (%.1f dB)\n", at08_snr, at06_snr);
                ++failures;
            }
        }
    }

    // N. Live description: the melody fed in 10 ms chunks (as a microphone
    //    delivers it) gives the same onsets and pitches as the offline
    //    transcription of the same file, and a continuous pitch reading.
    {
        std::vector<float> sig;
        read_wav(wav, sig);
        std::vector<kan_forge_note> offline(256);
        int32_t off_count = 0;
        kan_forge_transcribe_file(wav.c_str(), "keyboard", offline.data(), int32_t(offline.size()),
                                  &off_count, err, sizeof(err));

        kan_forge_live* live = kan_forge_live_open("keyboard", sr);
        if (live == nullptr) {
            std::fprintf(stderr, "SELFTEST FAIL: live open\n");
            ++failures;
        } else {
            std::vector<kan_forge_live_event> ev(512);
            std::vector<float> hits;
            std::vector<int> finals;
            float frame_mid_note = 0.0f; // the reading during C5 (300..800 ms)
            int frames = 0;
            for (size_t at = 0; at < sig.size(); at += 480) {
                const int32_t n = int32_t(std::min<size_t>(480, sig.size() - at));
                const int32_t got = kan_forge_live_process(live, sig.data() + at, n, ev.data(), int32_t(ev.size()));
                for (int32_t k = 0; k < got; ++k) {
                    const kan_forge_live_event& e = ev[size_t(k)];
                    if (e.kind == KAN_FORGE_LIVE_HIT) hits.push_back(e.onset_ms);
                    if (e.kind == KAN_FORGE_LIVE_PITCH && e.stage == 1) finals.push_back(int(e.midi + 0.5f));
                    if (e.kind == KAN_FORGE_LIVE_FRAME) {
                        ++frames;
                        if (e.t_ms > 545.0f && e.t_ms < 555.0f) frame_mid_note = e.midi;
                    }
                }
            }
            kan_forge_live_close(live);

            bool same = int32_t(hits.size()) == off_count;
            for (size_t i = 0; same && i < hits.size(); ++i)
                same = std::fabs(hits[i] - offline[i].onset_ms) < 0.01f;
            if (!same) {
                std::fprintf(stderr, "SELFTEST FAIL: live onsets (%zu) differ from offline (%d)\n",
                             hits.size(), off_count);
                ++failures;
            }
            if (finals.empty() || finals[0] != offline[0].midi) {
                std::fprintf(stderr, "SELFTEST FAIL: live final pitch %d, offline %d\n",
                             finals.empty() ? -1 : finals[0], offline[0].midi);
                ++failures;
            }
            if (std::fabs(frame_mid_note - 72.0f) > 0.3f) {
                std::fprintf(stderr, "SELFTEST FAIL: live pitch reading %.2f during C5, expected ~72\n",
                             frame_mid_note);
                ++failures;
            }
            const int expected_frames = int(sig.size() / 480);
            if (std::abs(frames - expected_frames) > 1) {
                std::fprintf(stderr, "SELFTEST FAIL: %d pitch readings, expected %d (one per 10 ms)\n",
                             frames, expected_frames);
                ++failures;
            }
            std::printf("selftest: live %zu onsets (offline %d), C5 reading %.2f, %d readings\n",
                        hits.size(), off_count, frame_mid_note, frames);
        }
    }

    // N+1. Expected mode: the score's next note as a hypothesis. On the C5
    //      attack (300 ms) the right expectation is measured as present and a
    //      wrong one (D5, +2 semitones) is not; measurement, never verdict.
    {
        std::vector<float> sig;
        read_wav(wav, sig);
        auto measure = [&](float expect_midi, float& similarity) {
            kan_forge_live* live = kan_forge_live_open("keyboard", sr);
            if (live == nullptr) return false;
            std::vector<kan_forge_live_event> ev(512);
            bool armed = false, measured = false;
            for (size_t at = 0; at < size_t(0.9f * sr) && !measured; at += 480) {
                if (!armed && at >= size_t(0.2f * sr)) armed = kan_forge_live_expect(live, &expect_midi, 1) == 1;
                const int32_t got = kan_forge_live_process(live, sig.data() + at, 480, ev.data(), int32_t(ev.size()));
                for (int32_t k = 0; k < got; ++k) {
                    if (ev[size_t(k)].kind == KAN_FORGE_LIVE_CANDIDATE) {
                        similarity = ev[size_t(k)].similarity;
                        measured = true;
                        break;
                    }
                }
            }
            kan_forge_live_close(live);
            return measured;
        };

        float right = 0.0f, wrong = 1.0f;
        const bool got_right = measure(72.0f, right);
        const bool got_wrong = measure(74.0f, wrong);
        if (!got_right || !got_wrong || right < 0.8f || wrong >= 0.4f) {
            std::fprintf(stderr, "SELFTEST FAIL: expected mode similarity right %.2f (want >= 0.8), wrong %.2f (want < 0.4)\n",
                         right, wrong);
            ++failures;
        }
        std::printf("selftest: expected mode similarity (right C5 %.2f, wrong D5 %.2f)\n", right, wrong);
    }

    // N+2. Noise floor calibration (issue 17): a floor above the sound's own
    //      level stops it starting notes - the room's hum calibrated away -
    //      while the same melody with the default floor is heard as before.
    {
        std::vector<float> sig;
        read_wav(wav, sig);
        auto count_hits = [&](float floor_energy) {
            kan_forge_live* live = kan_forge_live_open("keyboard", sr);
            if (live == nullptr) return -1;
            if (floor_energy > 0.0f) kan_forge_live_set_noise_floor(live, floor_energy);
            std::vector<kan_forge_live_event> ev(512);
            int hits = 0;
            for (size_t at = 0; at < sig.size(); at += 480) {
                const int32_t n = int32_t(std::min<size_t>(480, sig.size() - at));
                const int32_t got = kan_forge_live_process(live, sig.data() + at, n, ev.data(), int32_t(ev.size()));
                for (int32_t k = 0; k < got; ++k)
                    if (ev[size_t(k)].kind == KAN_FORGE_LIVE_HIT) ++hits;
            }
            kan_forge_live_close(live);
            return hits;
        };

        const int heard = count_hits(0.0f);
        const int silenced = count_hits(100.0f); // a floor nothing can reach
        if (heard <= 0 || silenced != 0) {
            std::fprintf(stderr, "SELFTEST FAIL: noise floor: %d onsets with the default floor, %d with one above the sound\n",
                         heard, silenced);
            ++failures;
        }
        std::printf("selftest: noise floor calibrates the gate (%d onsets -> %d)\n", heard, silenced);
    }

    if (failures == 0) std::printf("kan_forge_selftest OK\n");
    return failures == 0 ? 0 : 1;
}
