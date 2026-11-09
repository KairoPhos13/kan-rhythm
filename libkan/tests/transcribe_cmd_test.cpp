// Offline unit test of the shell-command construction in transcribe.hpp
// (model download, tool probes): pure string building — no network, no
// shell is ever run. The POSIX expectations pin the historical command
// strings (the POSIX path must stay unchanged); the Windows ones pin
// cmd.exe quoting (double quotes — single quotes are literal there) and
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

    printf(pass ? "TEST PASS\n" : "TEST FAIL\n");
    return pass ? 0 : 1;
}
