// kan_chart_scribe — offline transcription helper: audio -> chart draft
// (.score), MIDI sketch (.mid) and a readable log (.log). Accepts a local
// audio file or a YouTube URL (via yt-dlp, if installed). The output is a
// DRAFT for human review (chart-transcription-evaluation.md): facts, not
// verdicts.
#include "libkan/transcribe.hpp"

#define MINIAUDIO_IMPLEMENTATION
#include "miniaudio.h"

#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <string>
#include <vector>

namespace fs = std::filesystem;

namespace {

bool load_audio(const char* path, std::vector<float>& sig) {
    ma_decoder_config cfg = ma_decoder_config_init(ma_format_f32, 1, 48000);
    ma_decoder dec;
    if (ma_decoder_init_file(path, &cfg, &dec) != MA_SUCCESS) return false;
    float chunk[4096];
    for (;;) {
        ma_uint64 got = 0;
        if (ma_decoder_read_pcm_frames(&dec, chunk, 4096, &got) != MA_SUCCESS) break;
        if (got == 0) break;
        sig.insert(sig.end(), chunk, chunk + got);
    }
    ma_decoder_uninit(&dec);
    return !sig.empty();
}

void write_file(const std::string& path, const std::string& text) {
    std::ofstream f(path);
    f << text;
}

bool run_selftest() {
    std::printf("kan_chart_scribe --selftest: synth -> transcribe -> files\n");
    // Synthesized melody: C5 D5 E5 (500 ms each) + a vibrato G5.
    const float sr = 48000.0f;
    std::vector<float> sig(size_t(3.2f * sr), 0.0f);
    struct P { float t, d, hz, vib; };
    const P perf[] = {{0.3f, 0.5f, 523.25f, 0.0f}, {1.0f, 0.5f, 587.33f, 0.0f},
                      {1.7f, 0.5f, 659.25f, 0.0f}, {2.4f, 0.6f, 783.99f, 5.0f}};
    uint32_t seed = 7;
    for (const auto& p : perf) {
        const size_t at = size_t(p.t * sr), n = size_t(p.d * sr);
        for (size_t i = 0; at + i < sig.size() && i < n; ++i) {
            const float t = float(i) / sr;
            const float dev = (p.vib > 0.0f)
                ? (p.hz * 0.03f / p.vib) * std::sin(6.2831853f * p.vib * t) : 0.0f;
            float s = 0.0f;
            for (int h = 1; h <= 5; ++h) {
                s += (0.5f / float(h)) * std::exp(-0.8f * t)
                   * std::sin(6.2831853f * p.hz * float(h) * t
                              + float(h) * dev + 0.2f * float(seed % 5u));
            }
            sig[at + i] += 0.5f * s;
        }
        ++seed;
    }
    const auto notes = kan::transcribe_audio(sig.data(), sig.size(), sr,
                                             kan::InstrumentProfile::keyboard());
    std::printf("  transcribed %zu notes\n", notes.size());
    bool ok = notes.size() >= 4;
    int good = 0;
    for (const auto& n : notes) {
        if (n.midi >= 60 && n.midi <= 84) ++good;
    }
    ok = ok && good >= 4;
    std::printf("  [%s] notes in melodic range: %d\n", ok ? " ok " : "FAIL", good);
    write_file("chart_scribe_selftest.score", kan::chart_text(notes));
    write_file("chart_scribe_selftest.log", kan::chart_log(notes));
    const auto mid = kan::chart_midi(notes);
    std::ofstream f("chart_scribe_selftest.mid", std::ios::binary);
    f.write(reinterpret_cast<const char*>(mid.data()), std::streamsize(mid.size()));
    std::printf("  wrote chart_scribe_selftest.{score,log,mid}\n");
    std::printf("%s\n", ok ? "SELFTEST PASS" : "SELFTEST FAIL");
    return ok;
}

} // namespace

int main(int argc, char** argv) {
    std::string src, out = "transcription", profile_name = "piano";
    std::string stem_name;
    bool selftest = false;
    for (int i = 1; i < argc; ++i) {
        const std::string a = argv[i];
        auto next = [&]() -> const char* { return (i + 1 < argc) ? argv[++i] : ""; };
        if (a == "--out") out = next();
        else if (a == "--instrument") profile_name = next();
        else if (a == "--stem") stem_name = next();
        else if (a == "--selftest") selftest = true;
        else if (a[0] != '-') src = a;
        else {
            std::fprintf(stderr,
                "usage: kan_chart_scribe <audio|youtube-url> [--out base] "
                "[--instrument piano|ocarina|flute|recorder|guitar] [--stem piano] [--selftest]\n");
            return 2;
        }
    }
    if (selftest) return run_selftest() ? 0 : 1;
    if (src.empty()) {
        std::fprintf(stderr, "[ERROR] give an audio file or a YouTube URL\n");
        return 2;
    }

    std::string audio = src;
    if (src.rfind("http", 0) == 0) {
        // YouTube (or any URL yt-dlp supports): fetch audio as wav.
        const std::string dl = kan::find_audio_downloader();
        if (dl.empty()) {
            std::fprintf(stderr,
                "[ERROR] no downloader (yt-dlp/youtube-dl) for URLs. "
                "Install with: pip install --user yt-dlp — or pass a "
                "local audio file.\n");
            return 1;
        }
        audio = out + "_source.wav";
        const std::string extra = std::getenv("KAN_YTDL_ARGS")
                ? std::getenv("KAN_YTDL_ARGS") : "";
        const std::string cmd = dl + " " + extra + " --remote-components ejs:github "
                "-x --audio-format wav --no-playlist "
                                "-o '" + out + "_source.%(ext)s' '" + src + "'";
        std::printf("[fetch] %s\n", cmd.c_str());
        if (std::system(cmd.c_str()) != 0) {
            std::fprintf(stderr, "[ERROR] download failed\n");
            return 1;
        }
    }

    // Layer 0: if --stem is given, separate first and transcribe only
    // that instrument's stem (the fix for full-band UNRESOLVED noise).
    if (!stem_name.empty()) {
        const std::string dir = kan::separate_stems(audio, out + "_stems", "6");
        if (dir.empty()) {
            std::fprintf(stderr, "[ERROR] separation failed (is Demucs "
                                 "installed? pip install --user "
                                 "--break-system-packages demucs)\n");
            return 1;
        }
        audio = dir + "/" + stem_name + ".wav";
        std::printf("[stem] transcribing only: %s\n", audio.c_str());
    }
    std::vector<float> sig;
    if (!load_audio(audio.c_str(), sig)) {
        std::fprintf(stderr, "[ERROR] cannot decode audio: %s\n", audio.c_str());
        return 1;
    }
    std::printf("[audio] %s — %.1f s @ 48 kHz\n", audio.c_str(),
                double(sig.size()) / 48000.0);

    kan::InstrumentProfile prof = kan::InstrumentProfile::keyboard();
    if (profile_name == "ocarina" || profile_name == "flute"
        || profile_name == "recorder") {
        prof = kan::InstrumentProfile::wind_bowed();
    } else if (profile_name == "guitar") {
        prof = kan::InstrumentProfile::plucked();
    }

    const auto notes = kan::transcribe_audio(sig.data(), sig.size(), 48000.0f, prof);
    write_file(out + ".score", kan::chart_text(notes));
    write_file(out + ".log", kan::chart_log(notes));
    const auto mid = kan::chart_midi(notes);
    {
        std::ofstream f(out + ".mid", std::ios::binary);
        f.write(reinterpret_cast<const char*>(mid.data()), std::streamsize(mid.size()));
    }
    std::printf("[done] %zu notes -> %s.{score,log,mid}\n", notes.size(), out.c_str());
    std::printf("       (draft for review — facts, not verdicts)\n");
    return 0;
}
