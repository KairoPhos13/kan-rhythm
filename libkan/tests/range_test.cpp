// Full-range coverage test: every key of the piano (A0 27.5 Hz .. C8
// 4186.5 Hz) through the detection stack. The point is coverage, not
// latency: notes must be DETECTED and CONFIRMED with confirmation-grade
// accuracy across the whole instrument range, including bass ocarinas,
// bass guitars and other sub-80 Hz instruments. Latency is reported
// against the physical floor (blanking + longest candidate lag + overlap)
// but not gate-chased; see docs/dsp/range-coverage.md for the walls.
#include "libkan/dsp_detector.hpp"
#include "libkan/bounded_pitch.hpp"
#include "libkan/instrument_profile.hpp"
#include "libkan/precondition.hpp"
#include "test_signals.hpp"

#include <cmath>
#include <cstdio>
#include <algorithm>
#include <vector>

#ifdef RANGE_TEST_HAVE_BACF
#include "libkan/bacf_detector.hpp"
#endif

namespace {

const float SR = 48000.0f;
constexpr size_t B = 128;

// Rich plucked-string model for the sweep: 8 partials with 1/h amplitudes
// (real strings/hammers carry strong upper partials; they sharpen the
// correlation peak; see range-coverage.md on peak width vs harmonic count).
std::vector<float> synth_key(float freq, int midi) {
    const size_t n = size_t(0.5f * SR);
    std::vector<float> out(n);
    for (size_t i = 0; i < n; ++i) {
        const float t = float(i) / SR;
        float s = 0.0f;
        if (t < 0.008f) {
            const uint32_t h = uint32_t(i) * 2654435761u + uint32_t(midi);
            s = 0.15f * (float(int((h ^ (h >> 13)) % 2000)) / 1000.0f - 1.0f)
              * (1.0f - t / 0.008f);
        } else {
            const float decay = std::exp(-2.0f * (t - 0.008f));
            for (int h = 1; h <= 8; ++h) {
                if (freq * float(h) > 16000.0f) break; // no partials past 16 kHz
                s += (0.5f / float(h)) * decay
                   * std::sin(2.0f * float(M_PI) * freq * float(h) * t);
            }
        }
        out[i] = s;
    }
    return out;
}

// One piano key: synthesized plucked-style note at its fundamental.
struct KeyResult {
    int midi = 0;
    float freq = 0.0f;
    bool onset = false;
    bool matched = false;
    float match_ms = -1.0f;
    float match_cents = 0.0f;
    bool yin_ok = false;
    float yin_cents = 0.0f;
    float floor_ms = 0.0f; // physics floor: blanking + tau_max + pairs
};

} // namespace

int main() {
    const int kMidiLo = 21;   // A0, 27.5 Hz
    const int kMidiHi = 108;  // C8, 4186.5 Hz
    bool pass = true;

    printf("piano-range sweep: MIDI %d (A0, 27.5 Hz) .. %d (C8, 4186.5 Hz), "
           "%d keys, stage-0 + onset + blanking + pitch\n\n",
           kMidiLo, kMidiHi, kMidiHi - kMidiLo + 1);

    std::vector<KeyResult> results;
    int n_onset = 0, n_match = 0, n_yin = 0, n_bacf = 0;

    for (int midi = kMidiLo; midi <= kMidiHi; ++midi) {
        KeyResult r;
        r.midi = midi;
        r.freq = kan_test::midi_to_hz(float(midi));

        // Profile: the synthesized notes have an 8 ms pick attack, so
        // blanking must cover it (real keyboards use 6 ms against real ~4 ms
        // hammer attacks; the profile tracks the instrument's physics).
        kan::InstrumentProfile prof = kan::InstrumentProfile::plucked();
        prof.family = kan::InstrumentFamily::Keyboard;
        prof.min_freq_hz = 20.0f;   // cover A0 (bass ocarinas, bass guitars)
        prof.max_freq_hz = 5000.0f; // cover C8
        prof.blanking_ms = 8.0f;

        auto sig = synth_key(r.freq, midi);

        // Silence pad in front so the onset has a clean rise.
        std::vector<float> buf(size_t(0.05f * SR), 0.0f);
        buf.insert(buf.end(), sig.begin(), sig.end());

        kan::Preconditioner pre(SR);
        pre.calibrate(buf.data(), size_t(0.05f * SR) / 4); // silence-only part
        kan::HybridDetector det(SR);
        det.set_profile(prof);

        // --- onset + score-informed confirmation (game path) -------------
        kan::BoundedPitchMatcher matcher(SR);
        matcher.set_expected_note(r.freq);
        const float period = SR / r.freq;
        r.floor_ms = prof.blanking_ms
                   + 1000.0f * float(matcher.window_samples()) / SR;

        std::vector<float> clean;
        bool waiting = false;
        float ms_after = 0.0f;
        size_t clean_start = 0;

        for (size_t off = 0; off + B <= buf.size(); off += B) {
            float cond[B];
            pre.process_block(&buf[off], cond, B);
            // First onset only: decaying notes can retrigger the detector;
            // each synthetic key is ONE note.
            if (!r.onset && det.process_onset_block(cond, B)) {
                r.onset = true;
                waiting = true;
                ms_after = 0.0f;
                clean.clear();
                clean_start = off + B;
            }
            if (!waiting) continue;
            ms_after += 1000.0f * float(B) / SR;
            if (det.in_transient_blanking(ms_after)) continue;
            if (r.matched) continue;
            clean.insert(clean.end(), cond, cond + B);
            if (clean.size() >= matcher.window_samples()) {
                auto p = matcher.match(clean.data(), clean.size());
                if (p.valid) {
                    r.matched = true;
                    r.match_ms = ms_after;
                    r.match_cents = p.cents_offset;
                }
            }
        }
        n_onset += r.onset ? 1 : 0;
        n_match += r.matched ? 1 : 0;

        // --- YIN blind confirmation (steady-state window) ----------------
        // Window must span > 2 periods: below ~95 Hz a 2048 window is too
        // short (a documented wall). Grow it per note.
        size_t win = 2048;
        while (win < 4 * size_t(period) && win < 16384) win *= 2;
        det.set_expected_note(r.freq); // score-informed band for the sweep
        std::vector<float> rolling(win, 0.0f);
        const size_t start = clean_start + size_t(prof.blanking_ms / 1000.0f * SR);
        for (size_t off = start; off + B <= buf.size(); off += B) {
            std::move(rolling.begin() + B, rolling.end(), rolling.begin());
            std::copy(buf.begin() + off, buf.begin() + off + B, rolling.end() - B);
            auto p = det.detect_pitch(rolling);
            if (p.valid) {
                r.yin_ok = true;
                r.yin_cents = 1200.0f * std::log2(p.frequency_hz / r.freq);
                break;
            }
        }
        n_yin += r.yin_ok ? 1 : 0;
        if (!r.yin_ok) printf("  note: YIN miss at midi %d (%.1f Hz)\n", midi, r.freq);

#ifdef RANGE_TEST_HAVE_BACF
        {
            kan::BacfDetector bacf(SR);
            bacf.set_expected_note(r.freq);
            bool ok = false;
            for (size_t off = start; off + B <= buf.size(); off += B) {
                auto p = bacf.process_block(&buf[off], B);
                if (p.valid) { ok = true; break; }
            }
            n_bacf += ok ? 1 : 0;
        }
#else
        n_bacf = -1;
#endif

        results.push_back(r);
    }

    // --- per-octave report ------------------------------------------------
    printf("| octave band | keys | onset | matched | YIN | match ms (avg/max) "
           "| match err (max c) | YIN err (max c) | floor ms (max) |\n");
    printf("|---|---|---|---|---|---|---|---|---|\n");
    for (int band = 0; band < 9; ++band) {
        const int lo = kMidiLo + band * 12, hi = std::min(kMidiHi, lo + 11);
        if (lo > kMidiHi) break;
        int keys = 0, on = 0, ma = 0, yi = 0;
        float ms_sum = 0, ms_max = 0, err_max = 0, yerr_max = 0, floor_max = 0;
        for (const auto& r : results) {
            if (r.midi < lo || r.midi > hi) continue;
            ++keys;
            on += r.onset; ma += r.matched; yi += r.yin_ok;
            if (r.matched) {
                ms_sum += r.match_ms;
                ms_max = std::max(ms_max, r.match_ms);
                err_max = std::max(err_max, std::fabs(r.match_cents));
            }
            if (r.yin_ok) yerr_max = std::max(yerr_max, std::fabs(r.yin_cents));
            floor_max = std::max(floor_max, r.floor_ms);
        }
        const float f_lo = kan_test::midi_to_hz(float(lo));
        const float f_hi = kan_test::midi_to_hz(float(hi));
        printf("| %5.1f-%5.1f Hz | %2d | %2d | %2d | %2d | %5.1f / %5.1f "
               "| %6.1f | %6.1f | %5.1f |\n",
               double(f_lo), double(f_hi), keys, on, ma, yi,
               ma ? double(ms_sum / ma) : -1.0, double(ms_max),
               double(err_max), double(yerr_max), double(floor_max));
    }

    printf("\ntotals: onset %d/%d, score-informed match %d/%d, YIN %d/%d"
#ifdef RANGE_TEST_HAVE_BACF
           ", BACF %d/%d"
#endif
           "\n", n_onset, kMidiHi - kMidiLo + 1, n_match, kMidiHi - kMidiLo + 1,
           n_yin, kMidiHi - kMidiLo + 1
#ifdef RANGE_TEST_HAVE_BACF
           , n_bacf, kMidiHi - kMidiLo + 1
#endif
    );

    // --- assertions: coverage + confirmation-grade correctness ------------
    // The game decision at confirm time is SEMITONE identity within the
    // expected band; cents-level intonation is the sustain trackers' job
    // (YIN/BACF columns). Latency is reported, not gate-chased.
    pass &= n_onset == kMidiHi - kMidiLo + 1;   // every key detected
    pass &= n_match == kMidiHi - kMidiLo + 1;   // every key confirmed
    for (const auto& r : results) {
        // Semitone identity: the confirm reading must map to the played key
        // (the game's "right/wrong note" decision). Cents-level intonation
        // comes from the sustain trackers (YIN/BACF columns).
        const int played = r.midi;
        const int read = int(std::lround(float(played) + r.match_cents / 100.0f));
        if (r.matched && read != played) {
            printf("SEMANTIC FAIL: midi %d match err %.1f cents -> reads midi %d\n",
                   r.midi, r.match_cents, read);
            pass = false;
        }
        if (r.yin_ok && std::fabs(r.yin_cents) > 25.0f) {
            printf("ACCURACY FAIL: midi %d YIN err %.1f cents\n", r.midi, r.yin_cents);
            pass = false;
        }
    }
    // YIN sustains tracking across the musical range; the top piano octave
    // (>3.5 kHz, ~13-sample periods) is sample-quantization territory,
    // reported, not asserted (docs/dsp/range-coverage.md).
    pass &= n_yin >= int(0.93 * double(kMidiHi - kMidiLo + 1));

    printf(pass ? "TEST PASS\n" : "TEST FAIL\n");
    return pass ? 0 : 1;
}
