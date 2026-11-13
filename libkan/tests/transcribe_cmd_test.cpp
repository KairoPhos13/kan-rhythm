// Offline unit test of the shell-command construction in transcribe.hpp
// (model download, tool probes): pure string building, no network, no
// shell is ever run. The POSIX expectations pin the historical command
// strings (the POSIX path must stay unchanged); the Windows ones pin
// cmd.exe quoting (double quotes; single quotes are literal there) and
// its `where` PATH probe.
#include "libkan/transcribe.hpp"

#include <cstdio>
#include <string>

int main() {
    bool pass = true;
    auto expect = [&](const std::string& got, const std::string& want,
                      const char* what) {
        const bool ok = got == want;
        printf("%s: %s\n", ok ? "ok  " : "FAIL", what);
        if (!ok) printf("     got:  %s\n     want: %s\n", got.c_str(), want.c_str());
        pass &= ok;
    };

    // --- quoting: POSIX is unchanged single-quote wrapping ----------------
    expect(kan::shell_quote("models/6stem_q8.gguf", false),
           "'models/6stem_q8.gguf'", "posix quote: plain path");
    expect(kan::shell_quote("https://example.invalid/m.gguf", false),
           "'https://example.invalid/m.gguf'", "posix quote: url");

    // --- quoting: cmd.exe needs double quotes -----------------------------
    expect(kan::shell_quote("models\\6stem q8.gguf", true),
           R"("models\6stem q8.gguf")", "windows quote: spaces");
    expect(kan::shell_quote(R"(say "hi")", true),
           R"("say \"hi\"")", "windows quote: embedded quote");

    // --- model download commands -----------------------------------------
    expect(kan::curl_download_cmd("models/6stem_q8.gguf",
                                  "https://example.invalid/m.gguf", false),
           "curl -L --fail --progress-bar -o 'models/6stem_q8.gguf' "
           "'https://example.invalid/m.gguf'",
           "posix curl download");
    expect(kan::wget_download_cmd("models/6stem_q8.gguf",
                                  "https://example.invalid/m.gguf", false),
           "wget -q --show-progress -O 'models/6stem_q8.gguf' "
           "'https://example.invalid/m.gguf'",
           "posix wget download");
    expect(kan::curl_download_cmd("models\\6stem q8.gguf",
                                  "https://example.invalid/m.gguf", true),
           R"(curl.exe -L --fail --progress-bar -o "models\6stem q8.gguf" )"
           R"("https://example.invalid/m.gguf")",
           "windows curl download");

    // --- tool probes ------------------------------------------------------
    expect(kan::tool_probe_cmd("curl", false),
           "command -v curl > /dev/null 2>&1", "posix probe");
    expect(kan::tool_probe_cmd("curl.exe", true),
           "where curl.exe > NUL 2>&1", "windows probe");

    // --- model URL mapping (owner decision 2026-11-11): the default grade
    // is f16 (MIT source); q8 stays an explicit opt-in. Both URLs are
    // pinned to immutable revisions (audit C4). Pure strings, no network.
    expect(kan::roformer_default_grade(), "f16", "default grade is f16");
    expect(kan::roformer_model_url_for("f16", ""),
           "https://huggingface.co/studio409/BSRoformer-SW-6Stem-GGUF"
           "/resolve/fcb2eb0f60cb1135a4cdffe6c40de43c53ee07a9"
           "/bs_roformer_sw_6stem-F16.gguf",
           "f16 grade: the MIT studio409 source, pinned revision");
    expect(kan::roformer_model_url_for("q8", ""),
           "https://huggingface.co/FractalSurfer/BS-RoFormer-SW-6stem"
           "/resolve/2b98254b19bdab07ce225d884ec4aa9dd8aafce1"
           "/gguf/BS-RoFormer-SW-6stem-q8_0.gguf",
           "q8 grade: opt-in source (licence unknown), pinned revision");
    expect(kan::roformer_model_url_for("q4", ""), "",
           "q4 grade: no public source");
    expect(kan::roformer_model_url_for("q8", "https://example.invalid/m.gguf"),
           "https://example.invalid/m.gguf",
           "KAN_MODEL_URL self-hosting wins over the grade");

    // --- pinned SHA-256 per grade (audit C4): verified before use ------
    expect(kan::roformer_model_sha256_for("f16", ""),
           "f4bb0d024db4cbb1d38c043652f7277d830e25babafcf013403ee7344641a82a",
           "f16 grade: pinned digest");
    expect(kan::roformer_model_sha256_for("q8", ""),
           "e4c53c3389c4426bf8b6cdbcc4e3d7d813323ef8a3e4f1f157084b71960bdf63",
           "q8 grade: pinned digest");
    expect(kan::roformer_model_sha256_for("q4", ""), "",
           "q4 grade: no pin known (used unverified, said out loud)");
    expect(kan::roformer_model_sha256_for("f16", "ab"),
           "ab", "KAN_MODEL_SHA256 pins the operator's own mirror");

    printf(pass ? "TEST PASS\n" : "TEST FAIL\n");
    return pass ? 0 : 1;
}
