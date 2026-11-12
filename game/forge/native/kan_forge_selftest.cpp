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
#include <string>
#include <vector>

namespace {

// Minimal PCM16 mono WAV writer (48 kHz), no external dependencies.
bool write_wav(const std::string& path, const std::vector<float>& sig, int sr) {
    std::FILE* f = std::fopen(path.c_str(), "wb");
    if (f == nullptr) return false;
    const uint32_t data_bytes = uint32_t(sig.size() * 2);
    const uint32_t byte_rate = uint32_t(sr) * 2;
    const uint16_t block_align = 2;
    const uint16_t bits = 16;
    std::fwrite("RIFF", 1, 4, f);
    const uint32_t riff_size = 36 + data_bytes;
    std::fwrite(&riff_size, 4, 1, f);
    std::fwrite("WAVEfmt ", 1, 8, f);
    const uint32_t fmt_size = 16;
    std::fwrite(&fmt_size, 4, 1, f);
    const uint16_t format = 1, channels = 1;
    std::fwrite(&format, 2, 1, f);
    std::fwrite(&channels, 2, 1, f);
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

// Reader for the PCM16 mono WAV files written above (self-test round-trip).
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
