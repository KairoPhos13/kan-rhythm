// Offline A/B benchmark: Cycfi Q BACF (score-informed and blind) vs the
// lightweight YIN detector, over a synthesized note suite. Reports
// confirmation latency, cents accuracy, success rate, and per-block CPU
// cost. Results feed docs/dsp/bacf-vs-yin.md and the Milestone-1 gates
// (docs/ROADMAP.md). Runs as a ctest (known input -> asserted bounds).
#include "libkan/bacf_detector.hpp"
#include "libkan/bounded_pitch.hpp"
#include "libkan/dsp_detector.hpp"
#include "libkan/instrument_profile.hpp"
#include "test_signals.hpp"

#include <chrono>
#include <cmath>
#include <cstdio>
#include <algorithm>
#include <vector>

namespace {

const float SR = 48000.0f;
const size_t BLOCK = 128;
const size_t WINDOW = 2048;

struct Note {
    const char* name;
    float freq;
    float cents;   // detuning applied to the synthesized input
    float seconds;
};

const Note SUITE[] = {
    {"E2",  82.41f,  +8.0f, 0.40f},
    {"A2", 110.00f, -12.0f, 0.40f},
    {"C3", 130.81f, +15.0f, 0.35f},
    {"G3", 196.00f,  -5.0f, 0.35f},
    {"C4", 261.63f, +10.0f, 0.30f},
    {"E4", 329.63f,   0.0f, 0.30f},
    {"A4", 440.00f, +10.0f, 0.30f},
    {"C5", 523.25f, -20.0f, 0.25f},
    {"E5", 659.26f, +12.0f, 0.25f},
    {"A5", 880.00f,  -8.0f, 0.25f},
};

std::vector<float> synth_note(const Note& n) {
    return kan_test::synth_note(n.freq, n.cents, n.seconds, 1u);
}

float true_freq(const Note& n) { return n.freq * std::pow(2.0f, n.cents / 1200.0f); }

struct Stats {
    int confirmed = 0;
    int within_30c = 0;
    double latency_sum_ms = 0.0f;
    float latency_max_ms = 0.0f;
    double abs_err_sum_c = 0.0;
    double err_sum_c = 0.0;
    float abs_err_max_c = 0.0f;
    std::vector<float> per_note_ms;   ///< confirmation latency per suite note (-1 = miss)
    std::vector<float> per_note_cents;///< signed error per suite note (0 = miss)
};

void accumulate(Stats& st, float latency_ms, float err_cents) {
    ++st.confirmed;
    st.latency_sum_ms += latency_ms;
    st.latency_max_ms = std::max(st.latency_max_ms, latency_ms);
    st.abs_err_sum_c += std::fabs(err_cents);
    st.err_sum_c += err_cents;
    st.abs_err_max_c = std::max(st.abs_err_max_c, std::fabs(err_cents));
    if (std::fabs(err_cents) < 30.0f) ++st.within_30c;
}

// --- YIN runner: rolling window + profile blanking, like kan_cli ----------
Stats run_yin(const std::vector<std::vector<float>>& signals, double& ns_per_block) {
    Stats st;
    st.per_note_ms.assign(signals.size(), -1.0f);
    st.per_note_cents.assign(signals.size(), 0.0f);
    kan::HybridDetector det(SR);
    det.set_profile(kan::InstrumentProfile::plucked());
    const auto t0 = std::chrono::steady_clock::now();
    size_t blocks_total = 0;

    for (size_t n = 0; n < signals.size(); ++n) {
        const auto& sig = signals[n];
        std::vector<float> rolling(WINDOW, 0.0f);
        // Score-informed, as during real play: the chart says which note is
        // expected, so the search is bounded to ±2 semitones around it.
        det.set_expected_note(SUITE[n].freq);
        bool done = false;
        size_t off = 0;
        for (; off + BLOCK <= sig.size() && !done; off += BLOCK, ++blocks_total) {
            std::move(rolling.begin() + BLOCK, rolling.end(), rolling.begin());
            std::copy(sig.begin() + off, sig.begin() + off + BLOCK, rolling.end() - BLOCK);
            const float ms = 1000.0f * float(off + BLOCK) / SR;
            if (ms < det.profile().blanking_ms) continue; // stage-2 blanking
            auto p = det.detect_pitch(rolling);
            if (p.valid) {
                const float err = 1200.0f * std::log2(p.frequency_hz / true_freq(SUITE[n]));
                accumulate(st, ms, err);
                st.per_note_ms[n] = ms;
                st.per_note_cents[n] = err;
                done = true;
            }
        }
        if (!done) ++blocks_total;
    }
    const auto t1 = std::chrono::steady_clock::now();
    ns_per_block = double(std::chrono::duration_cast<std::chrono::nanoseconds>(t1 - t0).count())
                 / double(blocks_total ? blocks_total : 1);
    return st;
}

// --- YIN runner with adaptive window: when the score gives the expected
// note, shrink the analysis window to ~2.5 periods of that note (clamped to
// [512, 2048], multiple of the block size). This is the configuration that
// can physically approach the ROADMAP confirmation gates (<12 ms >261 Hz,
// <22 ms >82 Hz) — confirmation cannot beat ~2 periods of the fundamental.
Stats run_yin_adaptive(const std::vector<std::vector<float>>& signals, double& ns_per_block) {
    Stats st;
    st.per_note_ms.assign(signals.size(), -1.0f);
    st.per_note_cents.assign(signals.size(), 0.0f);
    kan::HybridDetector det(SR);
    det.set_profile(kan::InstrumentProfile::plucked());
    const auto t0 = std::chrono::steady_clock::now();
    size_t blocks_total = 0;

    for (size_t n = 0; n < signals.size(); ++n) {
        const auto& sig = signals[n];
        const float period = SR / SUITE[n].freq;
        size_t win = size_t(std::ceil(2.5f * period / float(BLOCK))) * BLOCK;
        win = std::clamp(win, size_t(512), size_t(2048));
        std::vector<float> rolling(win, 0.0f);
        det.set_expected_note(SUITE[n].freq);
        bool done = false;
        size_t off = 0;
        for (; off + BLOCK <= sig.size() && !done; off += BLOCK, ++blocks_total) {
            std::move(rolling.begin() + BLOCK, rolling.end(), rolling.begin());
            std::copy(sig.begin() + off, sig.begin() + off + BLOCK, rolling.end() - BLOCK);
            const float ms = 1000.0f * float(off + BLOCK) / SR;
            if (ms < det.profile().blanking_ms) continue; // stage-2 blanking
            auto p = det.detect_pitch(rolling);
            if (p.valid) {
                const float err = 1200.0f * std::log2(p.frequency_hz / true_freq(SUITE[n]));
                accumulate(st, ms, err);
                st.per_note_ms[n] = ms;
                st.per_note_cents[n] = err;
                done = true;
            }
        }
        if (!done) ++blocks_total;
    }
    const auto t1 = std::chrono::steady_clock::now();
    ns_per_block = double(std::chrono::duration_cast<std::chrono::nanoseconds>(t1 - t0).count())
                 / double(blocks_total ? blocks_total : 1);
    return st;
}

// --- BACF runner (score-informed or blind) --------------------------------
Stats run_bacf(const std::vector<std::vector<float>>& signals, bool score_informed,
               double& ns_per_block) {
    Stats st;
    st.per_note_ms.assign(signals.size(), -1.0f);
    st.per_note_cents.assign(signals.size(), 0.0f);
    kan::BacfDetector det(SR);
    const auto t0 = std::chrono::steady_clock::now();
    size_t blocks_total = 0;

    for (size_t n = 0; n < signals.size(); ++n) {
        const auto& sig = signals[n];
        det.reset();
        if (score_informed) det.set_expected_note(SUITE[n].freq);
        else det.clear_expected_note();
        size_t off = 0;
        for (; off + BLOCK <= sig.size(); off += BLOCK, ++blocks_total) {
            auto p = det.process_block(sig.data() + off, BLOCK);
            if (p.valid) {
                const float err = 1200.0f * std::log2(p.frequency_hz / true_freq(SUITE[n]));
                const float ms = 1000.0f * float(off + BLOCK) / SR;
                accumulate(st, ms, err);
                st.per_note_ms[n] = ms;
                st.per_note_cents[n] = err;
                break;
            }
        }
    }
    const auto t1 = std::chrono::steady_clock::now();
    ns_per_block = double(std::chrono::duration_cast<std::chrono::nanoseconds>(t1 - t0).count())
                 / double(blocks_total ? blocks_total : 1);
    return st;
}

// --- Bounded matched-filter runner: fast score-informed confirmation on
// the CLEAN (post-blanking) window only, growing until the matcher confirms,
// at the gate's 64-sample block granularity.
Stats run_bounded(const std::vector<std::vector<float>>& signals, double& ns_per_block) {
    Stats st;
    st.per_note_ms.assign(signals.size(), -1.0f);
    st.per_note_cents.assign(signals.size(), 0.0f);
    kan::BoundedPitchMatcher matcher(SR);
    const auto profile = kan::InstrumentProfile::plucked();
    const size_t blank_samples = size_t(profile.blanking_ms / 1000.0f * SR);
    const size_t B = 64; // gate configuration: 48 kHz / 64 samples
    const auto t0 = std::chrono::steady_clock::now();
    size_t blocks_total = 0;

    for (size_t n = 0; n < signals.size(); ++n) {
        const auto& sig = signals[n];
        matcher.set_expected_note(SUITE[n].freq);
        const size_t need = matcher.window_samples();
        bool done = false;
        size_t off = 0;
        for (; off + B <= sig.size() && !done; off += B, ++blocks_total) {
            const size_t t = off + B; // samples consumed
            if (t <= blank_samples) continue; // stage-2 blanking
            const size_t count = t - blank_samples; // clean samples available
            if (count < need) continue;
            auto p = matcher.match(&sig[blank_samples], count);
            if (p.valid) {
                const float err = 1200.0f * std::log2(p.frequency_hz / true_freq(SUITE[n]));
                const float ms = 1000.0f * float(t) / SR;
                accumulate(st, ms, err);
                st.per_note_ms[n] = ms;
                st.per_note_cents[n] = err;
                done = true;
            }
        }
        if (!done) blocks_total += (sig.size() - off) / B;
    }
    const auto t1 = std::chrono::steady_clock::now();
    ns_per_block = double(std::chrono::duration_cast<std::chrono::nanoseconds>(t1 - t0).count())
                 / double(blocks_total ? blocks_total : 1);
    return st;
}

void report(const char* label, const Stats& st, size_t total, double ns_per_block) {
    const int n = st.confirmed ? st.confirmed : 1;
    printf("| %-22s | %2d/%-2zu | %6.1f | %6.1f | %15.1f | %15.1f | %8.0f |\n",
           label, st.confirmed, total,
           st.confirmed ? st.latency_sum_ms / n : 0.0,
           st.latency_max_ms,
           st.confirmed ? st.abs_err_sum_c / n : 0.0,
           st.abs_err_max_c,
           ns_per_block);
}

} // namespace

int main() {
    const size_t total = sizeof(SUITE) / sizeof(SUITE[0]);
    std::vector<std::vector<float>> signals;
    signals.reserve(total);
    for (const auto& n : SUITE) signals.push_back(synth_note(n));

    double ns_yin = 0, ns_yin_ad = 0, ns_bacf_si = 0, ns_bacf_blind = 0, ns_bounded = 0;
    const Stats yin = run_yin(signals, ns_yin);
    const Stats yin_ad = run_yin_adaptive(signals, ns_yin_ad);
    const Stats bacf_si = run_bacf(signals, true, ns_bacf_si);
    const Stats bacf_blind = run_bacf(signals, false, ns_bacf_blind);
    const Stats bounded = run_bounded(signals, ns_bounded);

    printf("BACF (Cycfi Q) vs YIN — synthesized suite, %zu notes, SR=%.0f, block=%zu\n\n",
           total, SR, BLOCK);
    printf("| detector               | conf. | avg ms | max ms | avg abs err (c) | max abs err (c) | ns/block |\n");
    printf("|------------------------|-------|--------|--------|-----------------|-----------------|----------|\n");
    report("YIN score-informed", yin, total, ns_yin);
    report("YIN SI adapt window", yin_ad, total, ns_yin_ad);
    report("BACF score-informed", bacf_si, total, ns_bacf_si);
    report("BACF blind", bacf_blind, total, ns_bacf_blind);
    report("Bounded matched (SI)", bounded, total, ns_bounded);

    // Per-note latency breakdown: the ROADMAP gates are frequency-dependent
    // (<22 ms above 82 Hz, <12 ms above 261 Hz), so the detail matters.
    printf("\n| note | freq Hz | YIN 2048 ms | YIN adapt ms | BACF SI ms | BACF blind ms | bounded ms | bounded err (c) | BACF SI err (c) |\n");
    printf("|------|---------|-------------|--------------|------------|---------------|------------|-----------------|------------------|\n");
    for (size_t n = 0; n < total; ++n) {
        printf("| %-4s | %7.2f | %11.1f | %12.1f | %10.1f | %13.1f | %10.1f | %+15.1f | %+16.1f |\n",
               SUITE[n].name, SUITE[n].freq,
               yin.per_note_ms[n], yin_ad.per_note_ms[n],
               bacf_si.per_note_ms[n], bacf_blind.per_note_ms[n],
               bounded.per_note_ms[n], bounded.per_note_cents[n],
               bacf_si.per_note_cents[n]);
    }

    // Gate cross-checks (docs/ROADMAP.md Milestone 1):
    // - confirmation must succeed for every suite note (>=80% tolerated here;
    //   the F1 gate is measured by the chord test, this is the mono path)
    // - avg |err| < 30 cents for a usable tuner-grade readout
    bool pass = true;
    pass &= yin.confirmed >= int(total * 0.8);
    pass &= yin_ad.confirmed >= int(total * 0.8);
    pass &= bacf_si.confirmed >= int(total * 0.8);
    pass &= bacf_blind.confirmed >= int(total * 0.8);
    if (yin.confirmed) pass &= yin.abs_err_sum_c / yin.confirmed < 30.0;
    if (yin_ad.confirmed) pass &= yin_ad.abs_err_sum_c / yin_ad.confirmed < 30.0;
    if (bacf_si.confirmed) pass &= bacf_si.abs_err_sum_c / bacf_si.confirmed < 30.0;
    if (bacf_blind.confirmed) pass &= bacf_blind.abs_err_sum_c / bacf_blind.confirmed < 30.0;
    // Score-informed BACF must not be slower than fixed-window YIN on average.
    if (yin.confirmed && bacf_si.confirmed) {
        pass &= (bacf_si.latency_sum_ms / bacf_si.confirmed)
                <= (yin.latency_sum_ms / yin.confirmed) + 5.0;
    }
    // Bounded matched-filter: the fast confirmation stage, run at the gate's
    // 64-sample block granularity on the clean post-blanking window. The
    // physical floor is blanking (8 ms) + longest candidate lag + 64
    // correlation pairs + block quantization; regression bounds below
    // (full gate analysis in docs/dsp/latency-measurements.md):
    // - every suite note must confirm
    // - every note <= 24.5 ms; notes above 261 Hz < 16 ms (E2's floor is
    //   8 ms blanking + 12.9 ms longest lag + 1.3 ms pairs + quantization)
    // - avg abs err < 25 cents (confirmation stage; BACF/YIN refine cents)
    pass &= bounded.confirmed == int(total);
    if (bounded.confirmed) pass &= bounded.abs_err_sum_c / bounded.confirmed < 25.0;
    for (size_t n = 0; n < total; ++n) {
        if (bounded.per_note_ms[n] >= 0.0f) {
            pass &= bounded.per_note_ms[n] <= 24.5f;
            if (SUITE[n].freq > 261.0f) pass &= bounded.per_note_ms[n] < 16.0f;
        }
    }

    printf(pass ? "\nTEST PASS\n" : "\nTEST FAIL\n");
    return pass ? 0 : 1;
}
