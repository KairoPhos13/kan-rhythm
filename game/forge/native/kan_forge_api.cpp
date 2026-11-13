// kan_forge_api: implementation. See kan_forge_api.h for the contract.
// libkan does the measurement work (transcribe.hpp); this file only adapts
// it to a C ABI for the KAN client. Progress flows libkan -> shim ->
// kan_forge_progress_fn (worker thread), never console scraping.
//
// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
#include "kan_forge_api.h"

#include "libkan/loudness.hpp"
#include "libkan/transcribe.hpp"

#define MINIAUDIO_IMPLEMENTATION
#include "miniaudio.h"

#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <exception>
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

// Run one export's body with every C++ exception turned into a first-class
// error (audit B9): a C ABI must never unwind across the boundary, or the
// host process dies. The bodies live in *_impl functions so their code is
// untouched by the guard.
template <class F>
kan_forge_status guarded(char* err, int32_t err_cap, const char* what, F&& body) {
    try {
        return body();
    } catch (const std::exception& e) {
        set_err(err, err_cap, (std::string(what) + ": " + e.what()).c_str());
        return KAN_FORGE_ERR_INTERNAL;
    } catch (...) {
        set_err(err, err_cap, (std::string(what) + ": unexpected internal failure").c_str());
        return KAN_FORGE_ERR_INTERNAL;
    }
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
    // Same family mapping as tools/chart_scribe (families per I10).
    kan::InstrumentProfile p = kan::InstrumentProfile::keyboard();
    if (name != nullptr) {
        const std::string n(name);
        if (n == "ocarina" || n == "flute" || n == "recorder" || n == "wind_bowed")
            p = kan::InstrumentProfile::wind_bowed();
        else if (n == "guitar" || n == "plucked")
            p = kan::InstrumentProfile::plucked();
    }

    // Open pitch range in the editor: one name covers
    // many sizes (bass to sopranino ocarinas), so the family keeps only
    // its attack/legato behaviour; wrong notes are deleted by hand later.
    p.min_freq_hz = 27.0f;    // A0
    p.max_freq_hz = 4300.0f;  // C8
    return p;
}

} // namespace

extern "C" {

void kan_forge_set_progress(kan_forge_progress_fn fn, void* user) {
    try {
        g_fn = fn;
        g_user = user;
        kan::set_progress(fn != nullptr ? progress_trampoline : nullptr, nullptr);
    } catch (...) {
        // A progress hook is never worth an exception crossing the ABI.
    }
}

kan_forge_status fetch_audio_impl(const char* url, const char* out_wav,
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
    // std::system runs through cmd.exe on Windows, where single quotes are
    // literal characters: quote with " there, ' on POSIX shells.
#ifdef _WIN32
    const char* q = "\"";
#else
    const char* q = "'";
#endif
    const std::string extra = std::getenv("KAN_YTDL_ARGS") ? std::getenv("KAN_YTDL_ARGS") : "";
    const std::string out(out_wav);
    const std::string stem = out.substr(0, out.size() - 4); // strip ".wav"
    const std::string cmd = dl + " " + extra +
                            " --remote-components ejs:github -x --audio-format wav --no-playlist -o " +
                            q + stem + ".%(ext)s" + q + " " + q + url + q;
    std::printf("[fetch] %s\n", cmd.c_str());
    std::fflush(stdout);

    if (std::system(cmd.c_str()) != 0) {
        set_err(err, err_cap, "fetch: download failed (see log for the downloader output)");
        return KAN_FORGE_ERR_FETCH;
    }
    return KAN_FORGE_OK;
}

kan_forge_status kan_forge_fetch_audio(const char* url, const char* out_wav,
                                       char* err, int32_t err_cap) {
    return guarded(err, err_cap, "fetch", [&] {
        return fetch_audio_impl(url, out_wav, err, err_cap);
    });
}

kan_forge_status separate_stems_impl(const char* audio_path, const char* out_dir,
                                     char* err, int32_t err_cap) {
    if (audio_path == nullptr || out_dir == nullptr) {
        set_err(err, err_cap, "separate: missing audio path or output directory");
        return KAN_FORGE_ERR_ARGS;
    }

    // libkan (since its self-contained separation, 2026-10-13) probes its
    // compiled RoFormer runtime honestly: "runtime:<model>". The
    // "bsroformer:<cli>:<model>" token is the developer-tooling fallback.
    const std::string sep = kan::find_separator();
    if (sep.empty()) {
        set_err(err, err_cap,
                "separate: no separator runtime; expected the compiled RoFormer runtime "
                "(libkan KAN_ENABLE_ROFORMER) with its auto-downloaded model");
        return KAN_FORGE_ERR_SEPARATOR;
    }

    if (sep.rfind("runtime:", 0) != 0) {
        // Developer-tooling fallback: absolutise the model path (libkan
        // resolves it against the process working directory) so the CLI
        // invocation is stable regardless of where the game was launched.
        const size_t c1 = sep.find(':', 11);
        std::error_code ec;
        auto abs = std::filesystem::absolute(sep.substr(c1 + 1), ec);
        if (!ec) {
#ifdef _WIN32
            _putenv_s("KAN_ROFORMER_GGUF", abs.string().c_str()); // setenv is POSIX
#else
            setenv("KAN_ROFORMER_GGUF", abs.string().c_str(), 1);
#endif
        }
    }

    // Device-correctness gate visibility (editor-pipeline s7b.1): the first
    // run after a runtime change compares GPU vs CPU once (the CPU half is
    // heavy). Give it its own progress phase so the UI never shows a frozen
    // bar, and state the verdict (GPU ok / CPU fallback) out loud.
    if (sep.rfind("runtime:", 0) == 0) {
#ifdef KAN_HAVE_ROFORMER_RUNTIME
        kan::report_progress("device check", 0);
        std::string why;
        const bool parity_ok = kan::roformer_device_parity_ok(sep.substr(8), &why);
        kan::report_progress(parity_ok ? "device check (GPU ok)" : "device check failed (CPU fallback)", 100);
#endif
    }

    const std::string dir = kan::separate_stems(audio_path, out_dir, "6");
    if (dir.empty()) {
        set_err(err, err_cap,
                "separate: separation produced no stems; see the log above "
                "(the model or the input may be invalid)");
        return KAN_FORGE_ERR_SEPARATOR;
    }
    return KAN_FORGE_OK;
}

kan_forge_status kan_forge_separate_stems(const char* audio_path, const char* out_dir,
                                          char* err, int32_t err_cap) {
    return guarded(err, err_cap, "separate", [&] {
        return separate_stems_impl(audio_path, out_dir, err, err_cap);
    });
}

kan_forge_status transcribe_file_with_impl(const char* audio_path, const char* profile,
                                           const char* engine,
                                           kan_forge_note* out, int32_t cap,
                                           int32_t* out_count,
                                           char* err, int32_t err_cap) {
    if (audio_path == nullptr || out_count == nullptr) {
        set_err(err, err_cap, "transcribe: missing audio path or out_count");
        return KAN_FORGE_ERR_ARGS;
    }
    *out_count = 0;
    const std::string which = engine != nullptr ? engine : "describer";
    if (which != "describer" && which != "neural") {
        set_err(err, err_cap, "transcribe: unknown engine (describer | neural)");
        return KAN_FORGE_ERR_ARGS;
    }

    std::vector<float> sig;
    if (!load_audio(audio_path, sig)) {
        set_err(err, err_cap, "transcribe: cannot decode audio (missing file or unsupported codec)");
        return KAN_FORGE_ERR_DECODE;
    }

    const std::vector<kan::TranscribedNote> notes = which == "neural"
        ? kan::transcribe_audio_neural(sig.data(), sig.size(), 48000.0f, profile_for(profile))
        : kan::transcribe_audio(sig.data(), sig.size(), 48000.0f, profile_for(profile));

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

kan_forge_status kan_forge_transcribe_file_with(const char* audio_path, const char* profile,
                                                const char* engine,
                                                kan_forge_note* out, int32_t cap,
                                                int32_t* out_count,
                                                char* err, int32_t err_cap) {
    return guarded(err, err_cap, "transcribe", [&] {
        return transcribe_file_with_impl(audio_path, profile, engine, out, cap, out_count, err, err_cap);
    });
}

kan_forge_status kan_forge_transcribe_file(const char* audio_path, const char* profile,
                                           kan_forge_note* out, int32_t cap,
                                           int32_t* out_count,
                                           char* err, int32_t err_cap) {
    return guarded(err, err_cap, "transcribe", [&] {
        return transcribe_file_with_impl(audio_path, profile, "describer", out, cap, out_count, err, err_cap);
    });
}

const char* kan_forge_version(void) {
    // A literal: nothing here can throw.
    return "kan_forge_api 0.1.0 (Phase: level-creation pipeline)";
}

int32_t kan_forge_has_separator(void) {
    try {
        // libkan's probe is honest since its self-contained separation
        // (2026-10-13): it checks the compiled runtime and the model.
        return kan::find_separator().empty() ? 0 : 1;
    } catch (...) {
        return 0;
    }
}

int32_t kan_forge_has_downloader(void) {
    try {
        return kan::find_audio_downloader().empty() ? 0 : 1;
    } catch (...) {
        return 0;
    }
}

namespace {

// Minimal PCM16 mono WAV writer (48 kHz), same format as the self-test.
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

kan_forge_status edit_track_impl(const char* audio_path, float start_ms, float end_ms,
                                 int32_t mode, const char* out_wav,
                                 char* err, int32_t err_cap) {
    if (audio_path == nullptr || out_wav == nullptr) {
        set_err(err, err_cap, "edit: missing audio path or output path");
        return KAN_FORGE_ERR_ARGS;
    }
    if (end_ms < start_ms || start_ms < 0.0f) {
        set_err(err, err_cap, "edit: invalid range (need 0 <= start <= end)");
        return KAN_FORGE_ERR_ARGS;
    }

    std::vector<float> sig;
    if (!load_audio(audio_path, sig)) {
        set_err(err, err_cap, "edit: cannot decode audio (missing file or unsupported codec)");
        return KAN_FORGE_ERR_DECODE;
    }

    const size_t sr = 48000;
    size_t a = size_t(start_ms * 0.001f * float(sr));
    size_t b = size_t(end_ms * 0.001f * float(sr));
    if (a > sig.size()) a = sig.size();
    if (b > sig.size()) b = sig.size();

    std::vector<float> out;
    switch (mode) {
        case KAN_FORGE_EDIT_KEEP:
            // Keep the range, silence the rest (original timing preserved).
            out.assign(sig.size(), 0.0f);
            std::copy(sig.begin() + std::ptrdiff_t(a), sig.begin() + std::ptrdiff_t(b), out.begin() + std::ptrdiff_t(a));
            break;

        case KAN_FORGE_EDIT_MUTE:
            // Silence the range, keep the rest (original timing preserved).
            out = sig;
            std::fill(out.begin() + std::ptrdiff_t(a), out.begin() + std::ptrdiff_t(b), 0.0f);
            break;

        case KAN_FORGE_EDIT_TRIM:
            // Tight crop to the range (offset lost by design).
            out.assign(sig.begin() + std::ptrdiff_t(a), sig.begin() + std::ptrdiff_t(b));
            break;

        default:
            set_err(err, err_cap, "edit: unknown mode");
            return KAN_FORGE_ERR_ARGS;
    }

    if (!write_wav_pcm16(out_wav, out, 48000)) {
        set_err(err, err_cap, "edit: cannot write output file");
        return KAN_FORGE_ERR_TRANSCRIBE;
    }
    return KAN_FORGE_OK;
}

kan_forge_status kan_forge_edit_track(const char* audio_path, float start_ms, float end_ms,
                                      int32_t mode, const char* out_wav,
                                      char* err, int32_t err_cap) {
    return guarded(err, err_cap, "edit", [&] {
        return edit_track_impl(audio_path, start_ms, end_ms, mode, out_wav, err, err_cap);
    });
}

kan_forge_status analyze_track_impl(const char* audio_path, float* peaks, int32_t bucket_count,
                                    float* out_peak, float* out_rms,
                                    char* err, int32_t err_cap) {
    if (audio_path == nullptr || out_peak == nullptr || out_rms == nullptr) {
        set_err(err, err_cap, "analyze: missing audio path or outputs");
        return KAN_FORGE_ERR_ARGS;
    }
    *out_peak = 0.0f;
    *out_rms = 0.0f;

    std::vector<float> sig;
    if (!load_audio(audio_path, sig)) {
        set_err(err, err_cap, "analyze: cannot decode audio (missing file or unsupported codec)");
        return KAN_FORGE_ERR_DECODE;
    }

    double sum_sq = 0.0;
    float peak = 0.0f;
    for (float s : sig) {
        const float a = std::fabs(s);
        if (a > peak) peak = a;
        sum_sq += double(s) * double(s);
    }
    *out_peak = peak;
    *out_rms = float(std::sqrt(sum_sq / double(sig.size())));

    if (peaks != nullptr && bucket_count > 0) {
        for (int32_t i = 0; i < bucket_count; ++i) peaks[i] = 0.0f;
        const size_t per = (sig.size() + size_t(bucket_count) - 1) / size_t(bucket_count);
        for (size_t i = 0; i < sig.size(); ++i) {
            const size_t b = i / per;
            const float a = std::fabs(sig[i]);
            if (b < size_t(bucket_count) && a > peaks[b]) peaks[b] = a;
        }
    }
    return KAN_FORGE_OK;
}

kan_forge_status kan_forge_analyze_track(const char* audio_path, float* peaks, int32_t bucket_count,
                                         float* out_peak, float* out_rms,
                                         char* err, int32_t err_cap) {
    return guarded(err, err_cap, "analyze", [&] {
        return analyze_track_impl(audio_path, peaks, bucket_count, out_peak, out_rms, err, err_cap);
    });
}

kan_forge_status kan_forge_mix_tracks(const char* const* paths, const float* gains,
                                      int32_t count, const char* out_wav,
                                      char* err, int32_t err_cap) {
    return guarded(err, err_cap, "mix", [&] {
        // The guarded export below: one guard is enough for the work it does.
        return kan_forge_mix_tracks_at(paths, gains, nullptr, count, out_wav, err, err_cap);
    });
}

kan_forge_status mix_tracks_at_impl(const char* const* paths, const float* gains,
                                    const double* offsets_ms, int32_t count,
                                    const char* out_wav, char* err, int32_t err_cap) {
    if (paths == nullptr || gains == nullptr || out_wav == nullptr || count <= 0) {
        set_err(err, err_cap, "mix: missing inputs (need at least one track)");
        return KAN_FORGE_ERR_ARGS;
    }

    constexpr double sample_rate = 48000.0;

    std::vector<float> mix;
    for (int32_t i = 0; i < count; ++i) {
        if (paths[i] == nullptr) {
            set_err(err, err_cap, "mix: null track path");
            return KAN_FORGE_ERR_ARGS;
        }
        const double off_ms = offsets_ms != nullptr ? offsets_ms[i] : 0.0;

        // The offset sizes the whole mix buffer at once: bound it (audit B9,
        // KAN_FORGE_MAX_TRACK_OFFSET_MS), or a corrupt set.json allocates
        // the whole timeline in one go and takes the process down.
        if (!(off_ms >= 0.0) || off_ms > KAN_FORGE_MAX_TRACK_OFFSET_MS) {  // also rejects NaN
            set_err(err, err_cap, "mix: track offsets must be between 0 ms and 3 hours");
            return KAN_FORGE_ERR_ARGS;
        }
        std::vector<float> sig;
        if (!load_audio(paths[i], sig)) {
            set_err(err, err_cap, "mix: cannot decode track (missing file or unsupported codec)");
            return KAN_FORGE_ERR_DECODE;
        }
        const size_t start = size_t(std::llround(off_ms * 0.001 * sample_rate));
        if (start + sig.size() > mix.size()) mix.resize(start + sig.size(), 0.0f);
        const float g = gains[i];
        for (size_t s = 0; s < sig.size(); ++s) mix[start + s] += sig[s] * g;
    }

    // Peak protection: scale down instead of clipping (keeps the render
    // playable without distortion artifacts).
    float peak = 0.0f;
    for (float s : mix) peak = std::fabs(s) > peak ? std::fabs(s) : peak;
    if (peak > 0.99f) {
        const float scale = 0.99f / peak;
        for (float& s : mix) s *= scale;
    }

    if (!write_wav_pcm16(out_wav, mix, int(sample_rate))) {
        set_err(err, err_cap, "mix: cannot write output file");
        return KAN_FORGE_ERR_TRANSCRIBE;
    }
    return KAN_FORGE_OK;
}

kan_forge_status kan_forge_mix_tracks_at(const char* const* paths, const float* gains,
                                         const double* offsets_ms, int32_t count,
                                         const char* out_wav, char* err, int32_t err_cap) {
    return guarded(err, err_cap, "mix", [&] {
        return mix_tracks_at_impl(paths, gains, offsets_ms, count, out_wav, err, err_cap);
    });
}

} // extern "C"

// ---- Live description ------------------------------------------------------
// The describer exactly as transcribe_audio drives it (128-sample blocks,
// measurements drained after each block), plus a 10 ms pitch reading of the
// latest 2048 samples for the gameplay trail. All buffers are allocated in
// open; process only copies and measures.

struct kan_forge_live {
    static constexpr size_t kBlock = 128;
    static constexpr size_t kWindow = 2048;

    kan::NoteDescriber desc;
    kan::NoteEventStream stream{1024};
    kan::HybridDetector pitch;
    float sr;
    size_t frame_hop;

    std::vector<float> pending = std::vector<float>(kBlock);
    size_t pending_n = 0;
    std::vector<float> window = std::vector<float>(kWindow, 0.0f); // ring
    size_t window_pos = 0;
    std::vector<float> ordered = std::vector<float>(kWindow);
    std::vector<float> scratch = std::vector<float>(kWindow);
    uint64_t samples = 0;
    size_t since_frame = 0;

    kan_forge_live(const kan::InstrumentProfile& profile, float sample_rate)
        : desc(sample_rate, profile), pitch(sample_rate), sr(sample_rate),
          frame_hop(size_t(sample_rate / 100.0f)) {
        desc.subscribe(stream);
        pitch.set_profile(profile);
    }
};

namespace {

void live_drain(kan_forge_live* live, kan_forge_live_event* out, int32_t cap, int32_t& count) {
    kan::NoteEvent batch[64];
    for (;;) {
        const size_t got = live->stream.pop(batch, 64);
        if (got == 0) break;
        for (size_t k = 0; k < got; ++k) {
            const kan::NoteEvent& e = batch[k];
            kan_forge_live_event d{};
            d.note_id = e.note_id;
            d.t_ms = e.t_ms;
            d.flags = e.flags;
            switch (e.kind) {
            case kan::NoteKind::Hit:
                d.kind = KAN_FORGE_LIVE_HIT;
                d.onset_ms = e.u.hit.onset_ms;
                d.midi = e.u.hit.midi;
                d.group = e.u.hit.strike_group;
                break;
            case kan::NoteKind::Pitch:
                d.kind = KAN_FORGE_LIVE_PITCH;
                d.stage = int32_t(e.stage);
                d.midi = e.u.pitch.midi;
                d.cents = e.u.pitch.cents;
                break;
            case kan::NoteKind::Close:
                d.kind = KAN_FORGE_LIVE_CLOSE;
                d.end_ms = e.u.close.end_ms;
                break;
            case kan::NoteKind::Content:
                // Class statements are always Mono/Polyphonic; Unknown marks a
                // candidate measurement (make_content_candidate leaves it unset).
                if (e.klass != kan::ContentClass::Unknown) continue;
                d.kind = KAN_FORGE_LIVE_CANDIDATE;
                d.midi = e.u.candidate.midi;
                d.cents = e.u.candidate.cents;
                d.presence = e.u.candidate.presence;
                d.similarity = e.u.candidate.similarity;
                d.group = e.u.candidate.strike_group;
                break;
            default:
                continue; // content/expression facts: not used live yet
            }
            if (count < cap) out[count++] = d;
        }
    }
}

void live_frame(kan_forge_live* live, kan_forge_live_event* out, int32_t cap, int32_t& count) {
    const size_t n = kan_forge_live::kWindow;
    for (size_t i = 0; i < n; ++i)
        live->ordered[i] = live->window[(live->window_pos + i) % n];

    double acc = 0.0;
    for (size_t i = n - live->frame_hop; i < n; ++i) acc += double(live->ordered[i]) * live->ordered[i];

    const kan::PitchEstimate est =
        live->pitch.detect_pitch_into(live->ordered.data(), n, live->scratch.data(), live->scratch.size());

    kan_forge_live_event d{};
    d.kind = KAN_FORGE_LIVE_FRAME;
    d.t_ms = float(double(live->samples) * 1000.0 / live->sr);
    d.midi = est.valid ? est.midi + est.cents_offset / 100.0f : 0.0f;
    d.level = float(std::sqrt(acc / double(live->frame_hop)));
    if (count < cap) out[count++] = d;
}

} // namespace

extern "C" {

kan_forge_live* kan_forge_live_open(const char* profile, float sample_rate) {
    if (sample_rate < 8000.0f) return nullptr;
    try {
        return new kan_forge_live(profile_for(profile), sample_rate);
    } catch (...) {
        return nullptr;
    }
}

int32_t live_process_impl(kan_forge_live* live, const float* samples, int32_t n,
                          kan_forge_live_event* out, int32_t cap) {
    if (live == nullptr || samples == nullptr || n <= 0) return 0;
    int32_t count = 0;
    const size_t wn = kan_forge_live::kWindow;

    for (int32_t i = 0; i < n; ++i) {
        const float s = samples[i];
        live->pending[live->pending_n++] = s;
        live->window[live->window_pos] = s;
        live->window_pos = (live->window_pos + 1) % wn;
        ++live->samples;

        if (live->pending_n == kan_forge_live::kBlock) {
            live->desc.process_block(live->pending.data(), kan_forge_live::kBlock);
            while (live->desc.finalize_due()) {}
            live->pending_n = 0;
            live_drain(live, out, cap, count);
        }

        if (++live->since_frame >= live->frame_hop) {
            live->since_frame = 0;
            live_frame(live, out, cap, count);
        }
    }
    return count;
}

int32_t kan_forge_live_process(kan_forge_live* live, const float* samples, int32_t n,
                               kan_forge_live_event* out, int32_t cap) {
    try {
        return live_process_impl(live, samples, n, out, cap);
    } catch (...) {
        // A describer failure is not the game's crash (audit B9).
        return 0;
    }
}

int32_t kan_forge_live_expect(kan_forge_live* live, const float* midis, int32_t count) {
    if (live == nullptr || midis == nullptr || count <= 0) return 0;
    try {
        kan::NoteDescriber::CandidateSet set{};
        set.note_id = 0; // the next attack
        set.count = uint8_t(std::min<int32_t>(count, int32_t(kan::NoteDescriber::kMaxCandidates)));
        for (uint8_t k = 0; k < set.count; ++k) {
            set.entries[k].midi = midis[k];
            set.entries[k].struck = 1;
        }
        return live->desc.set_candidates(set) ? 1 : 0;
    } catch (...) {
        return 0;
    }
}

void kan_forge_live_set_noise_floor(kan_forge_live* live, float energy) {
    if (live == nullptr || !(energy > 0.0f)) return;
    try {
        live->desc.set_noise_floor(energy);
    } catch (...) {
        // A floor that cannot be set leaves the default (audit B9).
    }
}

void kan_forge_live_close(kan_forge_live* live) {
    try {
        delete live;
    } catch (...) {
        // A destructor is the last place for an exception (audit B9).
    }
}

kan_forge_status measure_loudness_impl(const char* audio_path, float* out_lufs, float* out_peak,
                                       char* err, int32_t err_cap) {
    if (audio_path == nullptr || out_lufs == nullptr || out_peak == nullptr) {
        set_err(err, err_cap, "loudness: missing audio path or outputs");
        return KAN_FORGE_ERR_ARGS;
    }
    std::vector<float> sig;
    if (!load_audio(audio_path, sig)) {
        set_err(err, err_cap, "loudness: cannot decode audio (missing file or unsupported codec)");
        return KAN_FORGE_ERR_DECODE;
    }
    *out_lufs = float(kan::integrated_loudness(sig.data(), sig.size(), 48000.0));
    *out_peak = kan::sample_peak(sig.data(), sig.size());
    return KAN_FORGE_OK;
}

kan_forge_status kan_forge_measure_loudness(const char* audio_path, float* out_lufs, float* out_peak,
                                            char* err, int32_t err_cap) {
    return guarded(err, err_cap, "loudness", [&] {
        return measure_loudness_impl(audio_path, out_lufs, out_peak, err, err_cap);
    });
}

kan_forge_status normalize_loudness_impl(const char* audio_path, const char* out_wav,
                                         float target_lufs, float ceiling_dbfs,
                                         float* out_before, float* out_after,
                                         char* err, int32_t err_cap) {
    if (audio_path == nullptr || out_wav == nullptr) {
        set_err(err, err_cap, "loudness: missing input or output path");
        return KAN_FORGE_ERR_ARGS;
    }
    std::vector<float> sig;
    if (!load_audio(audio_path, sig)) {
        set_err(err, err_cap, "loudness: cannot decode audio (missing file or unsupported codec)");
        return KAN_FORGE_ERR_DECODE;
    }
    const double before = kan::integrated_loudness(sig.data(), sig.size(), 48000.0);
    const double gain = kan::loudness_gain(before, kan::sample_peak(sig.data(), sig.size()), target_lufs,
                                           std::pow(10.0, ceiling_dbfs / 20.0));
    for (float& s : sig) s = float(s * gain);
    if (out_before != nullptr) *out_before = float(before);
    if (out_after != nullptr) *out_after = float(std::isfinite(before) ? before + 20.0 * std::log10(gain) : before);
    if (!write_wav_pcm16(out_wav, sig, 48000)) {
        set_err(err, err_cap, "loudness: cannot write output file");
        return KAN_FORGE_ERR_TRANSCRIBE;
    }
    return KAN_FORGE_OK;
}

kan_forge_status kan_forge_normalize_loudness(const char* audio_path, const char* out_wav,
                                              float target_lufs, float ceiling_dbfs,
                                              float* out_before, float* out_after,
                                              char* err, int32_t err_cap) {
    return guarded(err, err_cap, "loudness", [&] {
        return normalize_loudness_impl(audio_path, out_wav, target_lufs, ceiling_dbfs,
                                       out_before, out_after, err, err_cap);
    });
}

} // extern "C"
