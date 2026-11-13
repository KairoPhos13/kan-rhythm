// Offline behaviour test of the model pin (transcribe.hpp, audit C4):
// a model file whose bytes do not match the pinned SHA-256 is DELETED and
// refused calmly, one with a pin that matches is used, and a source with
// no pin at all is used unverified (said out loud). Every download in this
// test points at a local file:// URL or a nonexistent one: no network is
// touched and no model is fetched.
#include "libkan/transcribe.hpp"

#include <cstdio>
#include <filesystem>
#include <string>

#ifdef _WIN32
#include <stdlib.h>
static void set_env(const char* k, const char* v) { _putenv_s(k, v); }
#else
static void set_env(const char* k, const char* v) { setenv(k, v, 1); }
#endif

namespace {

bool pass = true;

void check(bool ok, const char* what) {
    printf("%s: %s\n", ok ? "ok  " : "FAIL", what);
    pass &= ok;
}

void write_file(const std::string& path, const std::string& body) {
    std::FILE* f = std::fopen(path.c_str(), "wb");
    if (f == nullptr) return;
    std::fwrite(body.data(), 1, body.size(), f);
    std::fclose(f);
}

} // namespace

int main() {
    namespace fs = std::filesystem;
    const fs::path tmp = fs::temp_directory_path() / "kan-model-pin-test";
    std::error_code ec;
    fs::remove_all(tmp, ec);
    fs::create_directories(tmp / "models", ec);
    fs::current_path(tmp);

    // The player's explicit file is trusted as-is: no pin, no check.
    set_env("KAN_ROFORMER_GGUF", (tmp / "player.gguf").string().c_str());
    write_file("player.gguf", "a model the player chose");
    check(kan::ensure_roformer_model() == (tmp / "player.gguf").string(),
          "KAN_ROFORMER_GGUF: trusted as-is");
    set_env("KAN_ROFORMER_GGUF", "");

    // A wrong file in models/ is deleted and refused; the re-download from
    // a dead source fails too and leaves no partial file behind.
    set_env("KAN_ROFORMER_GRADE", "f16");
    set_env("KAN_MODEL_URL", "file:///nonexistent/kan-model-pin-test.gguf");
    set_env("KAN_MODEL_SHA256", "");
    write_file("models/6stem_f16.gguf", "wrong bytes");
    check(kan::ensure_roformer_model() == "", "bad hash: refused");
    check(!fs::exists("models/6stem_f16.gguf", ec), "bad hash: the file is deleted");

    // A downloaded file with the wrong bytes is deleted too (the download
    // itself succeeds here: a real local file:// source).
    {
        set_env("KAN_ROFORMER_GRADE", "q8");
        write_file("served.gguf", "bytes that are not the pinned model");
        std::string abs = fs::absolute("served.gguf").generic_string();
        if (abs.empty() || abs[0] != '/') abs = "/" + abs; // file:///C:/...
        set_env("KAN_MODEL_URL", ("file://" + abs).c_str());
        check(kan::ensure_roformer_model() == "", "bad downloaded file: refused");
        check(!fs::exists("models/6stem_q8.gguf", ec), "bad downloaded file: deleted");
    }

    // The operator's pin (KAN_MODEL_SHA256) makes a vetted local mirror
    // pass: the file is kept and used without any download.
    set_env("KAN_ROFORMER_GRADE", "f16");
    set_env("KAN_MODEL_URL", "");
    write_file("models/6stem_f16.gguf", "the vetted model bytes");
    set_env("KAN_MODEL_SHA256",
            kan::sha256::hex_of("the vetted model bytes", 22).c_str());
    check(kan::ensure_roformer_model() == "models/6stem_f16.gguf",
          "pinned hash matches: the file is used");
    check(fs::exists("models/6stem_f16.gguf", ec), "pinned hash matches: the file stays");

    // A source with no pin at all (a self-hosted q4 conversion through
    // KAN_MODEL_URL) is used unverified rather than deleted.
    set_env("KAN_ROFORMER_GRADE", "q4");
    set_env("KAN_MODEL_URL", "file:///nonexistent/kan-model-pin-test.gguf");
    set_env("KAN_MODEL_SHA256", "");
    write_file("models/6stem_q4.gguf", "some q4 conversion");
    check(kan::ensure_roformer_model() == "models/6stem_q4.gguf",
          "no pin known: used (unverified)");
    check(fs::exists("models/6stem_q4.gguf", ec), "no pin known: not deleted");

    fs::current_path(fs::temp_directory_path());
    fs::remove_all(tmp, ec);
    printf(pass ? "TEST PASS\n" : "TEST FAIL\n");
    return pass ? 0 : 1;
}
