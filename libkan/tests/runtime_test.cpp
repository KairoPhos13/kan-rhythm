// Runtime correctness guard tests: SNR math + the parity gate's decision
// logic. The FULL device parity (GPU vs CPU on real hardware) runs when
// KAN_ROFORMER_PARITY=1 with the model available — it is the gate that
// makes the ggml-vulkan q8_0 bug (docs/porting/) impossible to ship.
#include "libkan/roformer_runtime.hpp"

#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <vector>

namespace {
bool g_pass = true;
void check(bool ok, const char* what) {
    std::printf("  [%s] %s\n", ok ? " ok " : "FAIL", what);
    if (!ok) g_pass = false;
}
} // namespace

int main() {
    std::printf("=== roformer runtime test ===\n");
    // SNR math: identical -> 120; known-noise -> expected value.
    std::vector<float> a(1000), b(1000);
    for (size_t i = 0; i < a.size(); ++i) {
        a[i] = std::sin(0.01f * float(i));
        b[i] = a[i];
    }
    check(std::fabs(kan::roformer_snr_db(a, b) - 120.0) < 1e-6,
          "identical signals = 120 dB");
    for (size_t i = 0; i < b.size(); ++i) b[i] += 0.01f; // -40 dB error
    const double snr = kan::roformer_snr_db(a, b);
    check(snr > 35.0 && snr < 45.0, "known noise lands near 40 dB");
    std::vector<float> z(1000, 0.0f);
    check(kan::roformer_snr_db(z, z) == 120.0, "silence vs silence = 120 dB");

    // Full device parity (the shipping gate) on request.
    const char* model = std::getenv("KAN_ROFORMER_GGUF");
    if (std::getenv("KAN_ROFORMER_PARITY") != nullptr && model != nullptr) {
        std::string detail;
        const bool ok = kan::roformer_device_parity_ok(model, &detail);
        std::printf("  parity: %s (%s)\n", ok ? "PASS" : "FAIL", detail.c_str());
        check(ok, "device parity >= 30 dB on content stems");
    } else {
        std::printf("  (full device parity: set KAN_ROFORMER_PARITY=1 and "
                    "KAN_ROFORMER_GGUF to run)\n");
    }
    std::printf("%s\n", g_pass ? "ALL PASS" : "FAILURES");
    return g_pass ? 0 : 1;
}
