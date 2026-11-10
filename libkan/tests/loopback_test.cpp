// Loopback regression harness: feeds the generated WAV fixtures through the
// full detection pipeline (stage 0 -> onset -> stage 2 blanking -> YIN
// pitch) exactly as a microphone stream would arrive, and asserts the
// expected detections + file-based onset latency against the fixture's
// known onset grid. This is the offline half of the ≤4.5 ms onset gate;
// the hardware half is the loopback-cable procedure in
// docs/dsp/latency-measurements.md. No microphone required.
#include "fixture_layout.hpp"
#include "wav_io.hpp"
#include "libkan/precondition.hpp"
#include "libkan/dsp_detector.hpp"
#include "libkan/chord_goertzel.hpp"

#include <cmath>
#include <cstdio>
#include <cstring>
#include <string>
#include <vector>

namespace {

using namespace kan_test;

constexpr size_t kBlock = 128;

struct Detection {
    float t_ms = 0.0f;       ///< onset time (block-quantized)
    float latency_ms = -1.0f;///< vs expected grid position
    float freq_hz = 0.0f;
    std::string note;
    bool pitch = false;
};

// Run one fixture through the pipeline; returns all detections.
std::vector<Detection> run_pipeline(const std::vector<float>& sig) {
    std::vector<Detection> out;
    kan::Preconditioner pre{float(kFixtureRate)};
    kan::HybridDetector det{float(kFixtureRate)};
    std::vector<float> rolling(2048, 0.0f);

    bool waiting = false;
    int since_onset = 0;
    Detection cur;

    for (size_t off = 0; off + kBlock <= sig.size(); off += kBlock) {
        float cond[kBlock];
        pre.process_block(&sig[off], cond, kBlock);

        std::move(rolling.begin() + kBlock, rolling.end(), rolling.begin());
        std::copy(cond, cond + kBlock, rolling.end() - kBlock);

        const float t_ms = 1000.0f * float(off + kBlock) / kFixtureRate;
        if (det.process_onset_block(cond, kBlock)) {
            if (waiting) out.push_back(cur); // pending onset had no pitch yet
            cur = Detection{};
            cur.t_ms = t_ms;
            waiting = true;
            since_onset = 0;
        }
        if (waiting) {
            ++since_onset;
            const float ms_after = float(since_onset) * 1000.0f * float(kBlock) / kFixtureRate;
            if (!det.in_transient_blanking(ms_after)) {
                auto p = det.detect_pitch(rolling);
                if (p.valid) {
                    cur.pitch = true;
                    cur.freq_hz = p.frequency_hz;
                    cur.note = p.note_name;
                    waiting = false;
                    out.push_back(cur);
                } else if (ms_after > 250.0f) {
                    waiting = false;
                    out.push_back(cur); // percussive: onset without pitch
                }
            }
        }
    }
    return out;
}

float load(const std::string& dir, const char* name, std::vector<float>& sig) {
    uint32_t rate = 0;
    const std::string path = dir + "/" + name;
    if (!read_wav(path, sig, rate) || rate != kFixtureRate) {
        std::fprintf(stderr, "[loopback] FAILED to load %s\n", path.c_str());
        std::exit(1);
    }
    return 1000.0f / float(rate);
}

int match_onset(const std::vector<Detection>& dets, float expected_ms, float tol_ms) {
    for (size_t i = 0; i < dets.size(); ++i) {
        if (std::fabs(dets[i].t_ms - expected_ms) <= tol_ms) return int(i);
    }
    return -1;
}

} // namespace

int main(int argc, char** argv) {
    const std::string dir = (argc > 1) ? argv[1] : "fixtures";
    bool pass = true;

    // --- noise: ZERO ghost onsets (stage 0 gate + onset floor) ------------
    {
        std::vector<float> sig;
        load(dir, kFxNoise, sig);
        auto dets = run_pipeline(sig);
        printf("noise: onsets=%zu (expect 0)\n", dets.size());
        pass &= dets.empty();
    }

    // --- percussive: 3 onsets, no pitch confirmations ---------------------
    {
        std::vector<float> sig;
        load(dir, kFxPercussive, sig);
        auto dets = run_pipeline(sig);
        int pitched = 0;
        for (const auto& d : dets) {
            if (d.pitch) ++pitched;
        }
        int grid_hits = 0;
        for (const float t0 : kPercTimes) {
            if (match_onset(dets, t0 * 1000.0f, 6.0f) >= 0) ++grid_hits;
        }
        printf("percussive: onsets=%zu grid_hits=%d pitched=%d (expect 3,3,0)\n",
               dets.size(), grid_hits, pitched);
        pass &= dets.size() == kPercCount && grid_hits == int(kPercCount) && pitched == 0;
    }

    // --- detuned notes: 3 onsets + correct pitches ------------------------
    {
        std::vector<float> sig;
        load(dir, kFxDetuned, sig);
        auto dets = run_pipeline(sig);
        int ok = 0;
        for (const auto& e : kDetunedNotes) {
            const int idx = match_onset(dets, e.t_seconds * 1000.0f, 6.0f);
            if (idx >= 0 && dets[size_t(idx)].pitch &&
                std::fabs(dets[size_t(idx)].freq_hz - e.freq_hz) < 6.0f) {
                ++ok;
                printf("  %s: onset +%.1f ms, %.2f Hz (true %.2f)\n",
                       e.name, dets[size_t(idx)].t_ms - e.t_seconds * 1000.0f,
                       dets[size_t(idx)].freq_hz, e.freq_hz);
            }
        }
        printf("detuned notes: %d/%zu correct (onset within 6 ms + pitch < 6 Hz)\n",
               ok, kDetunedCount);
        pass &= ok == int(kDetunedCount);
    }

    // --- chords: onsets near each strum + major triad verified ------------
    // A strummed chord legitimately produces several onset events (one per
    // string transient after the debounce). Assert presence per chord time,
    // not an exact count.
    {
        std::vector<float> sig;
        load(dir, kFxChords, sig);
        auto dets = run_pipeline(sig);
        int grid_hits = 0;
        for (const float t0 : kChordTimes) {
            if (match_onset(dets, t0 * 1000.0f, 30.0f) >= 0) ++grid_hits;
        }
        // Score-informed chord check on the major segment (C4 E4 G4).
        kan::GoertzelChordValidator chord{float(kFixtureRate)};
        const float major[3] = {60.0f, 64.0f, 67.0f};
        chord.set_expected_chord(major, 3);
        const size_t at = size_t(kChordTimes[0] * kFixtureRate) + 384;
        const size_t W = chord.recommended_window(float(kFixtureRate));
        auto cr = chord.validate(&sig[at], W);
        printf("chords: onsets=%zu grid_hits=%d major sim=%.3f valid=%d "
               "(expect >=3,3,>=0.8,1)\n",
               dets.size(), grid_hits, cr.similarity, int(cr.valid));
        pass &= dets.size() >= kChordCount && grid_hits == int(kChordCount) && cr.valid;
    }

    // --- onset grid: file-based onset latency (the offline gate half) -----
    {
        std::vector<float> sig;
        load(dir, kFxOnsetGrid, sig);
        auto dets = run_pipeline(sig);
        float worst = 0.0f, sum = 0.0f;
        int hit = 0;
        for (size_t g = 0; g < kGridCount; ++g) {
            const float expected = (kGridFirst + float(g) * kGridPeriod) * 1000.0f;
            const int idx = match_onset(dets, expected, 20.0f);
            if (idx < 0) continue;
            const float lat = dets[size_t(idx)].t_ms - expected;
            printf("  grid burst %zu: onset latency %+.2f ms (block-quantized)\n", g, lat);
            worst = std::max(worst, lat);
            sum += lat;
            ++hit;
        }
        // Detection fires on the block containing the rise -> latency is
        // bounded by one block (2.67 ms @ 128) plus report quantization.
        const float block_ms = 1000.0f * float(kBlock) / kFixtureRate;
        printf("onset latency: %d/%zu hits, avg %.2f ms, worst %.2f ms "
               "(one block = %.2f ms)\n",
               hit, kGridCount, hit ? sum / float(hit) : -1.0f, worst, block_ms);
        pass &= hit == int(kGridCount) && worst < block_ms + 0.01f;
    }

    // --- sweep: pitch sanity at fixed instants ----------------------------
    {
        std::vector<float> sig;
        load(dir, kFxSineSweep, sig);
        kan::HybridDetector det{float(kFixtureRate)};
        std::vector<float> rolling(2048, 0.0f);
        bool ok = true;
        for (const float t : {1.0f, 1.5f, 2.0f, 2.5f}) {
            const size_t center = size_t(t * kFixtureRate);
            const size_t start = center - 1024;
            std::copy(sig.begin() + start, sig.begin() + center + 1024, rolling.begin());
            auto p = det.detect_pitch(rolling);
            // Sweep frequency at time t: 80*(15)^(t/3).
            const float expected = 80.0f * std::pow(15.0f, t / kSweepDuration);
            const bool near = p.valid && std::fabs(p.frequency_hz - expected) < 0.08f * expected;
            printf("  sweep t=%.1f s: %.1f Hz (true %.1f) %s\n",
                   t, p.frequency_hz, expected, near ? "ok" : "MISS");
            ok &= near;
        }
        pass &= ok;
    }

    printf(pass ? "TEST PASS\n" : "TEST FAIL\n");
    return pass ? 0 : 1;
}
