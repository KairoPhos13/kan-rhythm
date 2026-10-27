// Adapter over the GGUF RoFormer runtime (BSRoformer.cpp + GGML, both MIT).
// Third-party inference code is compiled into libkan_roformer so the
// official flow needs no installed separator (editor-pipeline s7b).
//
// DEVICE CORRECTNESS GUARD (2026-10-14): ggml-vulkan's q8_0 integer
// dot-product path produced WRONG separation on AMD RX 6800 / RADV
// NAVI21 (silent stem leakage, not an error). Hardening, in layers:
//   1. Safe by default: the integer dot-product path is disabled in the
//      process environment before ggml initialises Vulkan (verified:
//      correct stems at the same speed). User override respected.
//   2. One-time CPU/GPU PARITY GATE on first use per (model, build):
//      the built-in probe separates a fixed test signal on the default
//      device and on CPU; the stems must match at >= 30 dB SNR. On
//      failure the session falls back to CPU — a broken GPU path can
//      never silently produce a level's stems. Result cached in a
//      marker next to the model; rebuilds re-probe (protects against
//      future ggml updates).
//   3. ggml is PINNED in libkan/CMakeLists.txt (see also
//      docs/porting/ggml-vulkan-int-dot-product.md).
#include "libkan/roformer_runtime.hpp"
#include "libkan/verbose.hpp"

#include "bs_roformer/audio.h"
#include "bs_roformer/inference.h"
#include "ggml.h"

#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <fstream>
#include <iostream>
#include <streambuf>
#include <string>
#include <vector>

namespace kan {

namespace {

void set_env(const char* k, const char* v) {
#ifdef _WIN32
    _putenv_s(k, v);
#else
    setenv(k, v, 0);
#endif
}

const char* get_env(const char* k) { return std::getenv(k); }

// Called before ANY Inference construction (backend init reads env).
// ggml's own lines (devices found, backend chosen, graph sizes) only when
// verbose; its warnings and errors always.
void quiet_ggml_log(enum ggml_log_level level, const char* text, void*) {
    static enum ggml_log_level last = GGML_LOG_LEVEL_INFO;
    if (level != GGML_LOG_LEVEL_CONT) last = level;
    if (last >= GGML_LOG_LEVEL_WARN) std::fputs(text, stderr);
}

/// BSRoformer prints its progress on std::cout/std::cerr: held silent while it runs, unless verbose.
class QuietStreams {
public:
    QuietStreams() {
        if (verbose()) return;
        out_ = std::cout.rdbuf(&null_);
        err_ = std::cerr.rdbuf(&null_);
    }
    ~QuietStreams() {
        if (out_ != nullptr) std::cout.rdbuf(out_);
        if (err_ != nullptr) std::cerr.rdbuf(err_);
    }
    QuietStreams(const QuietStreams&) = delete;
    QuietStreams& operator=(const QuietStreams&) = delete;

private:
    struct Null : std::streambuf {
        int overflow(int c) override { return c; }
    } null_;
    std::streambuf* out_ = nullptr;
    std::streambuf* err_ = nullptr;
};

void ensure_runtime_configured() {
    static bool done = false;
    if (done) return;
    done = true;
    if (!verbose()) ggml_log_set(quiet_ggml_log, nullptr);
    // Safe by default (user override respected if already set).
    if (get_env("GGML_VK_DISABLE_INTEGER_DOT_PRODUCT") == nullptr) {
        set_env("GGML_VK_DISABLE_INTEGER_DOT_PRODUCT", "1");
    }
}

// Fixed probe signal: a dense chord + noise, stereo 44.1 kHz, 2 s.
std::vector<float> make_probe_signal() {
    const int sr = 44100;
    const size_t frames = size_t(sr * 2);
    std::vector<float> out(frames * 2);
    const float f0[5] = {220.0f, 277.18f, 329.63f, 440.0f, 554.37f};
    uint32_t rng = 0x12345678u;
    for (size_t i = 0; i < frames; ++i) {
        const float t = float(i) / float(sr);
        float s = 0.0f;
        for (int k = 0; k < 5; ++k) {
            s += 0.16f * std::sin(6.2831853f * f0[k] * t + 0.3f * float(k));
            s += 0.05f * std::sin(6.2831853f * 2.0f * f0[k] * t);
        }
        rng = rng * 1664525u + 1013904223u;
        s += 0.01f * (float(int(rng >> 8 & 0xFFFF)) / 32767.5f - 1.0f);
        out[2 * i] = s;
        out[2 * i + 1] = s;
    }
    return out;
}

// Marker: "<model>.device-parity" — re-probes when the GPU code changes:
// the ggml and BSRoformer pins and the backend. Not on every rebuild of
// libkan (owner 2026-10-16: the probe's CPU pass takes minutes, and a
// build stamp made every editor rebuild pay it again).
#ifndef KAN_GGML_PIN
#define KAN_GGML_PIN "unpinned"
#endif
#ifndef KAN_BSR_PIN
#define KAN_BSR_PIN "unpinned"
#endif

const char* parity_backend_tag() {
#ifdef KAN_GGML_HAS_VULKAN
    return "vk";
#else
    return "cpu-only";
#endif
}

std::string parity_marker_path(const std::string& model) {
    return model + ".device-parity";
}

std::string parity_marker_line() {
    return std::string("ok v4 ") + parity_backend_tag() + " ggml " + KAN_GGML_PIN
         + " bsr " + KAN_BSR_PIN;
}

bool parity_marker_valid(const std::string& model) {
    std::ifstream f(parity_marker_path(model));
    if (!f) return false;
    std::string line;
    std::getline(f, line);
    return line == parity_marker_line();
}

void parity_marker_write(const std::string& model) {
    std::ofstream f(parity_marker_path(model));
    f << parity_marker_line() << "\n";
}

bool run_probe(const std::string& model_path, bool force_cpu,
               std::vector<std::vector<float>>& stems) {
    QuietStreams quiet;
    if (force_cpu) set_env("BSR_FORCE_CPU", "1");
    try {
        Inference engine(model_path);
        std::vector<float> probe = make_probe_signal();
        stems = engine.Process(probe, engine.GetDefaultChunkSize(),
                               engine.GetDefaultNumOverlap(), nullptr);
    } catch (...) {
        if (force_cpu) set_env("BSR_FORCE_CPU", "");
        return false;
    }
    if (force_cpu) set_env("BSR_FORCE_CPU", "");
    return !stems.empty();
}

} // namespace

double roformer_snr_db(const std::vector<float>& ref,
                       const std::vector<float>& test) {
    const size_t n = std::min(ref.size(), test.size());
    double sig = 0.0, err = 0.0;
    for (size_t i = 0; i < n; ++i) {
        const double r = double(ref[i]);
        const double d = r - double(test[i]);
        sig += r * r;
        err += d * d;
    }
    if (sig <= 1e-12) return 120.0;      // silence vs silence
    if (err <= 1e-12) return 120.0;      // identical
    return 10.0 * std::log10(sig / err);
}

bool roformer_device_parity_ok(const std::string& model_path,
                               std::string* detail) {
    ensure_runtime_configured();
    if (parity_marker_valid(model_path)) {
        if (detail) *detail = "cached pass (these ggml/BSRoformer pins)";
        return true;
    }
    std::vector<std::vector<float>> gpu_stems, cpu_stems;
    if (!run_probe(model_path, false, gpu_stems)) {
        if (detail) *detail = "probe failed on default device";
        return false;
    }
    if (!run_probe(model_path, true, cpu_stems)) {
        if (detail) *detail = "probe failed on CPU";
        return false;
    }
    if (gpu_stems.size() != cpu_stems.size()) {
        if (detail) *detail = "stem count mismatch";
        return false;
    }
    double worst = 120.0;
    for (size_t s = 0; s < gpu_stems.size(); ++s) {
        // Only stems that carry content are compared (empty stems can
        // match trivially or differ in denormal noise).
        double energy = 0.0;
        for (float x : cpu_stems[s]) energy += double(x) * double(x);
        if (energy / double(cpu_stems[s].size() + 1) < 1e-10) continue;
        const double snr = roformer_snr_db(cpu_stems[s], gpu_stems[s]);
        worst = std::min(worst, snr);
    }
    const bool ok = worst >= 30.0;
    if (detail) {
        char buf[96];
        std::snprintf(buf, sizeof(buf), "worst content-stem SNR %.1f dB (need >= 30)", worst);
        *detail = buf;
    }
    if (ok) parity_marker_write(model_path);
    return ok;
}

bool roformer_runtime_available() {
    ensure_runtime_configured();
    return true;
}

int roformer_sample_rate() { return 44100; }
int roformer_stem_count() { return 6; }

namespace {

// Linear resampler (the models are fixed at 44.1 kHz; our pipeline uses
// any rate). Adequate for separation input conditioning; a proper
// polyphase resampler is a documented follow-up (editor-pipeline s7b).
std::vector<float> resample_stereo(const std::vector<float>& in, int ch,
                                   double from, double to) {
    if (std::fabs(from - to) < 1.0) return in;
    const size_t frames_in = in.size() / size_t(ch);
    const size_t frames_out = size_t(double(frames_in) * to / from);
    std::vector<float> out(frames_out * size_t(ch));
    for (size_t i = 0; i < frames_out; ++i) {
        const double pos = double(i) * from / to;
        const size_t i0 = size_t(pos);
        const double frac = pos - double(i0);
        for (int c = 0; c < ch; ++c) {
            const float a = in[i0 * size_t(ch) + size_t(c)];
            const float b = (i0 + 1 < frames_in) ? in[(i0 + 1) * size_t(ch) + size_t(c)] : a;
            out[i * size_t(ch) + size_t(c)] = float(double(a) + frac * double(b - a));
        }
    }
    return out;
}

} // namespace

bool roformer_separate_wav(const std::string& input_wav,
                           const std::string& out_dir,
                           const std::string& model_path,
                           const std::function<void(float)>& progress) {
    ensure_runtime_configured();
    QuietStreams quiet;
    // Correctness gate: if this (model, build, device) combination does not
    // match CPU, the session falls back to CPU — a broken GPU path can
    // never silently produce a level's stems.
    static bool force_cpu_session = false;
    std::string why;
    if (!force_cpu_session && !roformer_device_parity_ok(model_path, &why)) {
        std::fprintf(stderr, "[roformer] parity gate FAILED (%s) - "
                             "falling back to CPU\n", why.c_str());
        force_cpu_session = true;
    } else if (!why.empty()) {
        if (verbose()) std::printf("[roformer] parity gate: %s\n", why.c_str());
    }
    if (force_cpu_session) set_env("BSR_FORCE_CPU", "1");

    try {
        Inference engine(model_path);
        AudioBuffer buf = AudioFile::Load(input_wav);
        const int want_sr = engine.GetSampleRate();
        const int src_ch = int(buf.channels) >= 2 ? 2 : 1;

        std::vector<float> stereo(buf.data);
        if (src_ch == 1) {
            stereo.resize(buf.data.size() * 2);
            for (size_t i = buf.data.size(); i-- > 0;) {
                stereo[2 * i] = buf.data[i];
                stereo[2 * i + 1] = buf.data[i];
            }
        }
        if (double(buf.sampleRate) != double(want_sr)) {
            stereo = resample_stereo(stereo, 2, double(buf.sampleRate), double(want_sr));
        }
        if (progress) progress(0.05f);

        std::function<void(float)> fwd;
        if (progress) {
            fwd = [&](float p) { progress(0.05f + 0.9f * p); };
        }
        std::vector<std::vector<float>> stems =
            engine.Process(stereo, engine.GetDefaultChunkSize(),
                           engine.GetDefaultNumOverlap(), fwd);
        if (stems.empty()) return false;

        static const char* N[6] = {"bass", "drums", "other", "vocals", "guitar", "piano"};
        for (size_t s = 0; s < stems.size() && s < 6; ++s) {
            AudioBuffer out;
            out.data = std::move(stems[s]);
            out.channels = 2;
            out.sampleRate = unsigned(want_sr);
            out.samples = out.data.size();
            AudioFile::Save(out_dir + "/" + N[s] + ".wav", out);
        }
        if (progress) progress(1.0f);
        return true;
    } catch (const std::exception& e) {
        std::fprintf(stderr, "[roformer] %s\n", e.what());
        std::fprintf(stderr, "[roformer] note: the runtime reads WAV only - "
                             "decode MP3/OGG/FLAC to WAV first\n");
        return false;
    } catch (...) {
        return false;
    }
}

} // namespace kan
