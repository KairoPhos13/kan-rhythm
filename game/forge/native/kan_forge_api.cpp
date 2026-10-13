// kan_forge_api — implementation. See kan_forge_api.h for the contract.
// libkan does the measurement work (transcribe.hpp); this file only adapts
// it to a C ABI for the KAN client. Progress flows libkan -> shim ->
// kan_forge_progress_fn (worker thread) — never console scraping.
//
// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
#include "kan_forge_api.h"

#include "libkan/transcribe.hpp"

#define MINIAUDIO_IMPLEMENTATION
#include "miniaudio.h"

#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <vector>

namespace {

kan_forge_progress_fn g_fn = nullptr;
void* g_user = nullptr;

void progress_trampoline(void* user, const char* phase, int percent) {
    (void)user;
    if (g_fn != nullptr) g_fn(g_user, phase, int32_t(percent));
}

void set_err(char* err, int32_t cap, const char* msg) {
    if (err == nullptr || cap <= 0) return;
    std::snprintf(err, size_t(cap), "%s", msg);
}

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

kan::InstrumentProfile profile_for(const char* name) {
    // Same mapping as tools/chart_scribe (families per I10).
    if (name != nullptr) {
        const std::string n(name);
        if (n == "ocarina" || n == "flute" || n == "recorder" || n == "wind_bowed")
            return kan::InstrumentProfile::wind_bowed();
        if (n == "guitar" || n == "plucked")
            return kan::InstrumentProfile::plucked();
    }
    return kan::InstrumentProfile::keyboard();
}

} // namespace

extern "C" {

void kan_forge_set_progress(kan_forge_progress_fn fn, void* user) {
    g_fn = fn;
    g_user = user;
    kan::set_progress(fn != nullptr ? progress_trampoline : nullptr, nullptr);
}

kan_forge_status kan_forge_fetch_audio(const char* url, const char* out_wav,
                                       char* err, int32_t err_cap) {
    if (url == nullptr || out_wav == nullptr) {
        set_err(err, err_cap, "fetch: missing url or output path");
        return KAN_FORGE_ERR_ARGS;
    }

    const std::string dl = kan::find_audio_downloader();
    if (dl.empty()) {
        set_err(err, err_cap,
                "fetch: no downloader found (install yt-dlp: pip install --user yt-dlp)");
        return KAN_FORGE_ERR_FETCH;
    }

    // Same fetch flow as tools/chart_scribe: yt-dlp extracts audio as wav.
    const std::string extra = std::getenv("KAN_YTDL_ARGS") ? std::getenv("KAN_YTDL_ARGS") : "";
    const std::string out(out_wav);
    const std::string stem = out.substr(0, out.size() - 4); // strip ".wav"
    const std::string cmd = dl + " " + extra +
                            " --remote-components ejs:github -x --audio-format wav --no-playlist -o '" +
                            stem + ".%(ext)s' '" + url + "'";
    std::printf("[fetch] %s\n", cmd.c_str());
    std::fflush(stdout);

    if (std::system(cmd.c_str()) != 0) {
        set_err(err, err_cap, "fetch: download failed (see log for the downloader output)");
        return KAN_FORGE_ERR_FETCH;
    }
    return KAN_FORGE_OK;
}

kan_forge_status kan_forge_separate_stems(const char* audio_path, const char* out_dir,
                                          char* err, int32_t err_cap) {
    if (audio_path == nullptr || out_dir == nullptr) {
        set_err(err, err_cap, "separate: missing audio path or output directory");
        return KAN_FORGE_ERR_ARGS;
    }
    if (kan::find_separator().empty()) {
        set_err(err, err_cap,
                "separate: bs_roformer-cli or GGUF model not found "
                "(set KAN_ROFORMER_CLI / KAN_ROFORMER_GGUF)");
        return KAN_FORGE_ERR_SEPARATOR;
    }
    const std::string dir = kan::separate_stems(audio_path, out_dir, "6");
    if (dir.empty()) {
        set_err(err, err_cap, "separation produced no stems (see log)");
        return KAN_FORGE_ERR_SEPARATOR;
    }
    return KAN_FORGE_OK;
}

kan_forge_status kan_forge_transcribe_file(const char* audio_path, const char* profile,
                                           kan_forge_note* out, int32_t cap,
                                           int32_t* out_count,
                                           char* err, int32_t err_cap) {
    if (audio_path == nullptr || out_count == nullptr) {
        set_err(err, err_cap, "transcribe: missing audio path or out_count");
        return KAN_FORGE_ERR_ARGS;
    }
    *out_count = 0;

    std::vector<float> sig;
    if (!load_audio(audio_path, sig)) {
        set_err(err, err_cap, "transcribe: cannot decode audio (missing file or unsupported codec)");
        return KAN_FORGE_ERR_DECODE;
    }

    const std::vector<kan::TranscribedNote> notes =
        kan::transcribe_audio(sig.data(), sig.size(), 48000.0f, profile_for(profile));

    *out_count = int32_t(notes.size());
    if (out == nullptr || cap < int32_t(notes.size())) {
        set_err(err, err_cap, "transcribe: output buffer too small (required count in out_count)");
        return KAN_FORGE_ERR_CAPACITY;
    }

    for (size_t i = 0; i < notes.size(); ++i) {
        const kan::TranscribedNote& n = notes[i];
        kan_forge_note& d = out[i];
        d.onset_ms = n.onset_ms;
        d.end_ms = n.end_ms;
        d.dur_ms = n.dur_ms;
        d.midi = int32_t(n.midi);
        d.cents = n.cents;
        d.dyn_db = n.dyn_db;
        d.vib_rate = n.vib_rate;
        d.vib_depth = n.vib_depth;
        d.end_faded = n.end_faded ? 1 : 0;
        d.transition = n.transition ? 1 : 0;
    }
    return KAN_FORGE_OK;
}

const char* kan_forge_version(void) {
    return "kan_forge_api 0.1.0 (Phase: level-creation pipeline)";
}

int32_t kan_forge_has_separator(void) {
    return kan::find_separator().empty() ? 0 : 1;
}

int32_t kan_forge_has_downloader(void) {
    return kan::find_audio_downloader().empty() ? 0 : 1;
}

namespace {

// Minimal PCM16 mono WAV writer (48 kHz) — same format as the self-test.
bool write_wav_pcm16(const std::string& path, const std::vector<float>& sig, int sr) {
    std::FILE* f = std::fopen(path.c_str(), "wb");
    if (f == nullptr) return false;
    const uint32_t data_bytes = uint32_t(sig.size() * 2);
    const uint32_t byte_rate = uint32_t(sr) * 2;
    const uint16_t block_align = 2, bits = 16, format = 1, channels = 1;
    std::fwrite("RIFF", 1, 4, f);
    const uint32_t riff_size = 36 + data_bytes;
    std::fwrite(&riff_size, 4, 1, f);
    std::fwrite("WAVEfmt ", 1, 8, f);
    const uint32_t fmt_size = 16;
    std::fwrite(&fmt_size, 4, 1, f);
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

} // namespace

kan_forge_status kan_forge_mix_tracks(const char* const* paths, const float* gains,
                                      int32_t count, const char* out_wav,
                                      char* err, int32_t err_cap) {
    if (paths == nullptr || gains == nullptr || out_wav == nullptr || count <= 0) {
        set_err(err, err_cap, "mix: missing inputs (need at least one track)");
        return KAN_FORGE_ERR_ARGS;
    }

    std::vector<float> mix;
    for (int32_t i = 0; i < count; ++i) {
        if (paths[i] == nullptr) {
            set_err(err, err_cap, "mix: null track path");
            return KAN_FORGE_ERR_ARGS;
        }
        std::vector<float> sig;
        if (!load_audio(paths[i], sig)) {
            set_err(err, err_cap, "mix: cannot decode track (missing file or unsupported codec)");
            return KAN_FORGE_ERR_DECODE;
        }
        if (sig.size() > mix.size()) mix.resize(sig.size(), 0.0f);
        const float g = gains[i];
        for (size_t s = 0; s < sig.size(); ++s) mix[s] += sig[s] * g;
    }

    // Peak protection: scale down instead of clipping (keeps the render
    // playable without distortion artifacts).
    float peak = 0.0f;
    for (float s : mix) peak = std::fabs(s) > peak ? std::fabs(s) : peak;
    if (peak > 0.99f) {
        const float scale = 0.99f / peak;
        for (float& s : mix) s *= scale;
    }

    if (!write_wav_pcm16(out_wav, mix, 48000)) {
        set_err(err, err_cap, "mix: cannot write output file");
        return KAN_FORGE_ERR_TRANSCRIBE;
    }
    return KAN_FORGE_OK;
}

} // extern "C"
