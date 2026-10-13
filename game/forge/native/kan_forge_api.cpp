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

} // extern "C"
