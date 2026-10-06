// Offline test of the non-consuming detection tap (kanfx input bus API):
// independent readers, overwrite/drop semantics, and the const-input
// contract of the detection chain. No microphone required.
#include "libkan/tap.hpp"
#include "libkan/dsp_detector.hpp"

#include <cmath>
#include <cstdio>
#include <vector>

namespace {

constexpr size_t kBlock = 128;
constexpr size_t kDepth = 8;

using Tap = kan::TapBuffer<float, kBlock, kDepth>;

void fill_block(float* dst, float base) {
    for (size_t i = 0; i < kBlock; ++i) dst[i] = base + float(i);
}

} // namespace

int main() {
    bool pass = true;

    // --- 1. Publish / read: sequences and contents -----------------------
    {
        Tap tap;
        Tap::Reader r(tap);
        float block[kBlock];
        for (int b = 0; b < 3; ++b) {
            fill_block(block, 100.0f * float(b));
            tap.publish(block);
        }
        kan::TapFrame f;
        int seen = 0;
        bool data_ok = true;
        while (r.next(f)) {
            if (f.sequence != uint64_t(seen)) data_ok = false;
            if (f.count != kBlock) data_ok = false;
            if (std::fabs(f.samples[0] - 100.0f * float(seen)) > 1e-6f) data_ok = false;
            if (std::fabs(f.samples[kBlock - 1] - (100.0f * float(seen) + 127.0f)) > 1e-6f) data_ok = false;
            ++seen;
        }
        printf("publish/read: seen=%d data_ok=%d available=%llu\n",
               seen, int(data_ok), (unsigned long long)r.available());
        pass &= seen == 3 && data_ok && r.available() == 0;
    }

    // --- 2. Non-consuming: independent readers ---------------------------
    {
        Tap tap;
        Tap::Reader r1(tap);
        Tap::Reader r2(tap);
        float block[kBlock];
        for (int b = 0; b < 4; ++b) {
            fill_block(block, float(b));
            tap.publish(block);
        }
        kan::TapFrame f;
        int n1 = 0, n2 = 0;
        while (r1.next(f)) ++n1;   // r1 drains everything...
        while (r2.next(f)) ++n2;   // ...r2 still sees all of it
        printf("two readers: r1=%d r2=%d (non-consuming)\n", n1, n2);
        pass &= n1 == 4 && n2 == 4;
    }

    // --- 3. Overwrite semantics: lagging reader loses only the gap -------
    {
        Tap tap;
        float block[kBlock];
        for (size_t b = 0; b < kDepth + 3; ++b) {
            fill_block(block, float(b));
            tap.publish(block);
        }
        Tap::Reader r(tap); // created late: starts at the newest block
        kan::TapFrame f;
        int seen = 0;
        while (r.next(f)) ++seen;
        printf("late reader: seen=%d dropped=%llu (expect 0, 0)\n",
               seen, (unsigned long long)r.dropped());
        pass &= seen == 0 && r.dropped() == 0ull;

        // A reader that was there all along but stalls loses exactly the gap.
        Tap tap2;
        Tap::Reader early(tap2);
        for (size_t b = 0; b < kDepth + 3; ++b) {
            fill_block(block, float(b));
            tap2.publish(block);
        }
        seen = 0;
        uint64_t first_seq = 0;
        bool first = true;
        while (early.next(f)) {
            if (first) { first_seq = f.sequence; first = false; }
            ++seen;
        }
        printf("stalled reader: seen=%d dropped=%llu first_seq=%llu "
               "(expect %d, 3, %d)\n",
               seen, (unsigned long long)early.dropped(),
               (unsigned long long)first_seq, int(kDepth), int(3));
        pass &= seen == int(kDepth) && early.dropped() == 3ull && first_seq == 3ull;
    }

    // --- 4. Const-input contract: detectors never touch the tap data -----
    {
        Tap tap;
        Tap::Reader r(tap); // join before publishing (readers start at latest)
        float block[kBlock];
        for (size_t i = 0; i < kBlock; ++i) block[i] = 0.5f * std::sin(0.1f * float(i));
        tap.publish(block);
        kan::TapFrame f;
        pass &= r.next(f);

        std::vector<float> snapshot(f.samples, f.samples + kBlock);
        kan::HybridDetector det(48000.0f);
        det.process_onset_block(f.samples, f.count);   // const input
        det.detect_pitch(std::vector<float>(f.samples, f.samples + kBlock));
        bool unchanged = true;
        for (size_t i = 0; i < kBlock; ++i) {
            if (f.samples[i] != snapshot[i]) unchanged = false;
        }
        printf("const-input contract: buffer unchanged after detection: %d\n",
               int(unchanged));
        pass &= unchanged;
    }

    // --- 5. skip_to_latest ------------------------------------------------
    {
        Tap tap;
        float block[kBlock];
        for (int b = 0; b < 5; ++b) {
            fill_block(block, float(b));
            tap.publish(block);
        }
        Tap::Reader r(tap);
        r.skip_to_latest();
        kan::TapFrame f;
        pass &= !r.next(f) && r.available() == 0;
        printf("skip_to_latest: ok\n");
    }

    printf(pass ? "TEST PASS\n" : "TEST FAIL\n");
    return pass ? 0 : 1;
}
