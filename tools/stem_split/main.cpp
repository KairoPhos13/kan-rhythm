// kan_stem_split — Layer 0 helper: music source separation (Demucs) and
// stem remixing for game modes. Separate a song into instrument stems,
// then rebuild audio from selected stems: "autoplay" = all; "piano mode" =
// all minus the piano (the player supplies it); ensemble modes = backing
// with each player's stem removed. Separation output is a DRAFT for
// authoring (AI model: weak labels / approximate audio), the remix math
// is ours and deterministic.
#include "libkan/transcribe.hpp"

#define MINIAUDIO_IMPLEMENTATION
#include "miniaudio.h"

#include <cstdio>
#include <cstring>
#include <filesystem>
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

bool write_wav(const std::string& path, const std::vector<float>& sig) {
    ma_encoder_config cfg =
        ma_encoder_config_init(ma_encoding_format_wav, ma_format_f32, 1, 48000);
    ma_encoder enc;
    if (ma_encoder_init_file(path.c_str(), &cfg, &enc) != MA_SUCCESS) return false;
    ma_uint64 written = 0;
    ma_encoder_write_pcm_frames(&enc, sig.data(), sig.size(), &written);
    ma_encoder_uninit(&enc);
    return written == sig.size();
}

const char* kStems[6] = {"drums", "bass", "vocals", "guitar", "piano", "other"};

// Remix: sum stems whose name is selected (or all-minus when `minus`).
bool remix(const std::string& dir, const std::vector<std::string>& sel,
           bool minus, const std::string& out) {
    std::vector<float> acc;
    int used = 0;
    for (const char* name : kStems) {
        const std::string p = dir + "/" + name + ".wav";
        if (!fs::exists(p)) continue;
        bool wanted = false;
        for (const auto& s : sel) wanted = wanted || (s == name);
        // --only with an empty list = everything (the full mix).
        const bool include = minus ? !wanted : (sel.empty() ? true : wanted);
        if (!include) continue;
        std::vector<float> sig;
        if (!load_audio(p.c_str(), sig)) continue;
        if (acc.size() < sig.size()) acc.resize(sig.size(), 0.0f);
        for (size_t i = 0; i < sig.size(); ++i) acc[i] += sig[i];
        ++used;
    }
    if (used == 0) {
        std::fprintf(stderr, "[ERROR] no stems matched in %s\n", dir.c_str());
        return false;
    }
    return write_wav(out, acc);
}

float rms(const std::vector<float>& v) {
    double a = 0.0;
    for (float x : v) a += double(x) * double(x);
    return v.empty() ? 0.0f : float(std::sqrt(a / double(v.size())));
}

bool run_selftest() {
    std::printf("kan_stem_split --selftest: remix math (separation needs "
                "demucs; not self-tested here)\n");
    // Three synthetic stems with distinct energies; remix minus one must
    // be quieter than the full mix and louder than any single stem.
    const fs::path dir = fs::path("stem_split_selftest");
    fs::create_directories(dir);
    std::vector<float> s1(48000, 0.5f), s2(48000, 0.2f), s3(48000, 0.1f);
    bool ok = write_wav((dir / "drums.wav").string(), s1)
           && write_wav((dir / "bass.wav").string(), s2)
           && write_wav((dir / "piano.wav").string(), s3);
    std::vector<float> full, minus_piano;
    if (!remix(dir.string(), {}, false, "stem_split_full.wav")) ok = false;
    if (!remix(dir.string(), {"piano"}, true, "stem_split_minus_piano.wav")) ok = false;
    load_audio("stem_split_full.wav", full);
    load_audio("stem_split_minus_piano.wav", minus_piano);
    const float r_full = rms(full), r_minus = rms(minus_piano);
    std::printf("  full %.3f  minus-piano %.3f\n", double(r_full), double(r_minus));
    ok = ok && r_full > r_minus && r_minus > 0.05f;
    // --minus with no selection must equal the full mix (all-minus-none).
    std::vector<float> none;
    remix(dir.string(), {}, true, "stem_split_none.wav");
    load_audio("stem_split_none.wav", none);
    ok = ok && std::fabs(rms(none) - r_full) < 1e-6f;
    std::printf("  [%s] remix sums deterministic and minus-stem correct\n",
                ok ? " ok " : "FAIL");
    std::printf("%s\n", ok ? "SELFTEST PASS" : "SELFTEST FAIL");
    return ok;
}

} // namespace

int main(int argc, char** argv) {
    std::string src, out_dir = "stems", out = "";
    std::vector<std::string> sel;
    bool minus = false, selftest = false, do_remix = false;
    for (int i = 1; i < argc; ++i) {
        const std::string a = argv[i];
        auto next = [&]() -> const char* { return (i + 1 < argc) ? argv[++i] : ""; };
        if (a == "--out-dir") out_dir = next();
        else if (a == "--minus" || a == "--only") {
            minus = (a == "--minus");
            do_remix = true;
            std::string list = next(), tok;
            for (char c : list + ",") {
                if (c == ',' || c == ' ') { if (!tok.empty()) sel.push_back(tok); tok.clear(); }
                else tok += c;
            }
        }
        else if (a == "--out") out = next();
        else if (a == "--selftest") selftest = true;
        else if (a == "--stems") {
            std::string list = next(), tok;
            for (char c : list + ",") {
                if (c == ',' || c == ' ') { if (!tok.empty()) sel.push_back(tok); tok.clear(); }
                else tok += c;
            }
        } else if (a[0] != '-') src = a;
        else {
            std::fprintf(stderr,
                "usage: kan_stem_split <audio> [--out-dir stems]          "
                "  (separate into stems)\n"
                "       kan_stem_split <stem-dir> --minus piano --out backing.wav\n"
                "       kan_stem_split <stem-dir> --only drums,bass --out mix.wav\n"
                "       kan_stem_split --selftest\n");
            return 2;
        }
    }
    if (selftest) return run_selftest() ? 0 : 1;
    if (src.empty()) {
        std::fprintf(stderr, "[ERROR] give an audio file or a stems directory\n");
        return 2;
    }

    if (!do_remix) {
        // Separate mode.
        const std::string sep = kan::find_separator();
        if (sep.empty()) {
            std::fprintf(stderr,
                "[ERROR] no separator backend. Needs bs_roformer-cli + a\n"
                "                GGUF model (KAN_ROFORMER_CLI / KAN_ROFORMER_GGUF).\n");
            return 1;
        }
        const std::string dir = kan::separate_stems(src, out_dir, "6");
        if (dir.empty()) {
            std::fprintf(stderr, "[ERROR] separation failed\n");
            return 1;
        }
        std::printf("[done] stems in %s/\n", dir.c_str());
        for (const char* n : kStems) {
            const std::string p = dir + "/" + n + ".wav";
            if (fs::exists(p)) std::printf("  %s.wav\n", n);
        }
        return 0;
    }

    // Remix mode: src is the stems directory.
    const std::string base = out.empty() ? std::string("remix.wav") : out;
    if (!remix(src, sel, minus, base)) return 1;
    std::printf("[done] %s (%s: %s)\n", base.c_str(),
                minus ? "all minus" : "only", sel.empty() ? "-" : sel[0].c_str());
    return 0;
}
