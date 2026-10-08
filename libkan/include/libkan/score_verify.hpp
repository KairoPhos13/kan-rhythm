#pragma once
// Score verification core: measure a written score against audio (test-side
// shared code — the probe prints it, score_error_test asserts on it).
// Measurement only, never judgment: the score is a hypothesis set and a time
// anchor; the tool reports what the audio contains.
#include "libkan/harmonic_chord.hpp"
#include "libkan/instrument_profile.hpp"
#include "libkan/precondition.hpp"
#include "libkan/dsp_detector.hpp"

#include <algorithm>
#include <cmath>
#include <utility>
#include <cstdio>
#include <cstring>
#include <vector>

namespace kan {
namespace score_verify {

constexpr size_t kMaxScoreNotes = 10;

struct ScoreNote {
    float onset_ms = 0.0f, dur_ms = 0.0f;
    int midi = 0;
    float presence = 0.0f, cents = 0.0f, amp_peak = 0.0f;
    float measured_onset_ms = 0.0f, measured_end_ms = 0.0f;
    float peak_ms = 0.0f; ///< when the note's fitted level peaked
    float cents_center = 0.0f;    ///< amplitude-weighted pitch estimate
    bool saturated = false;       ///< estimate hit its register's search cap
    int velocity = 0;             ///< ground-truth strike velocity (MIDI)
    bool end_faded = false;       ///< end inferred from slow decay, not an
                                  ///< observed release (long decaying notes
                                  ///< cross the end threshold while still
                                  ///< held: duration unmeasurable -- flag it,
                                  ///< never report a confident wrong end)
    std::vector<std::pair<float, float>> levels;      ///< (t_ms, fitted level)
    std::vector<std::pair<float, float>> cents_traj;  ///< (t_ms, cents) when
                                      ///< want_traj: execution-shape facts
                                      ///< (vibrato, bends) need the path, not
                                      ///< just its center (I3)
    float onset_err_ms = 1e9f;
    float sim_w = 0.0f;      ///< amp-weighted accumulators for center/sim
    float sim_wsum = 0.0f, cents_wsum = 0.0f;
};

/// Score verification core (shared by the probe and score_error_test).
/// Measurement only: the written score is a hypothesis set + a time anchor;
/// the tool never judges, it reports what the audio contains.
/// Score line: `onset_ms  duration  note`, where duration is a rhythm word
/// (16th, 8th, quarter, dotted-quarter, half, dotted-half, whole — also
/// Spanish names) or a millisecond number, and the note is a name (G3,
/// F#4) or a midi number. Header lines set the grid: `# quarter_ms 656`
/// or `# bpm 91.5`.
int note_name_to_midi(const char* tok) {
    static const char* NAMES[12] = {"C", "C#", "D", "D#", "E", "F", "F#", "G",
                                    "G#", "A", "A#", "B"};
    const int len = (tok[1] == '#') ? 2 : 1;
    if (tok[len] < '0' || tok[len] > '9') return -1; // octave required
    for (int i = 0; i < 12; ++i) {
        if (std::strncmp(tok, NAMES[i], size_t(len)) == 0 && NAMES[i][len] == '\0') {
            return (std::atoi(tok + len) + 1) * 12 + i;
        }
    }
    return -1;
}

float beats_to_ms(const char* tok, float quarter_ms) {
    // "1/2", "3/4" -> fractions of a beat; "1", "4" -> whole beats.
    const char* slash = std::strchr(tok, '/');
    double beats = 0.0;
    if (slash != nullptr) {
        const double num = std::atof(tok);
        const double den = std::atof(slash + 1);
        if (den > 0.0) beats = num / den;
    } else {
        beats = std::atof(tok);
    }
    return (beats > 0.0) ? float(beats) * quarter_ms : 0.0f;
}

float rhythm_to_ms(const char* tok, float quarter_ms) {
    auto eq = [](const char* a, const char* b) { return std::strcmp(a, b) == 0; };
    if (eq(tok, "16th") || eq(tok, "sixteenth") || eq(tok, "semicorchea")) return quarter_ms * 0.25f;
    if (eq(tok, "8th") || eq(tok, "eighth") || eq(tok, "corchea")) return quarter_ms * 0.5f;
    if (eq(tok, "quarter") || eq(tok, "negra")) return quarter_ms;
    if (eq(tok, "dotted-quarter") || eq(tok, "negra-puntillo")) return quarter_ms * 1.5f;
    if (eq(tok, "half") || eq(tok, "blanca")) return quarter_ms * 2.0f;
    if (eq(tok, "dotted-half") || eq(tok, "blanca-puntillo")) return quarter_ms * 3.0f;
    if (eq(tok, "whole") || eq(tok, "redonda")) return quarter_ms * 4.0f;
    return 0.0f; // not a rhythm word: caller falls back to milliseconds
}

bool parse_score(const char* path, std::vector<ScoreNote>& notes) {
    FILE* f = std::fopen(path, "r");
    if (f == nullptr) return false;
    float quarter_ms = 500.0f;
    bool tempo_known = false;
    char line[256];
    while (std::fgets(line, sizeof(line), f) != nullptr) {
        const char* p = line;
        while (*p == ' ' || *p == '\t') ++p;
        if (*p == '\0' || *p == '\n') continue;
        if (*p == '#') {
            if (std::strstr(p, "quarter_ms") != nullptr) {
                quarter_ms = std::strtof(std::strstr(p, "quarter_ms") + 10, nullptr);
                tempo_known = true;
            } else if (std::strstr(p, "bpm") != nullptr) {
                const float bpm = std::strtof(std::strstr(p, "bpm") + 3, nullptr);
                if (bpm > 1.0f) { quarter_ms = 60000.0f / bpm; tempo_known = true; }
            }
            continue;
        }
        char tok1[32] = {0}, tok2[32] = {0}, tok3[32] = {0};
        if (std::sscanf(p, "%31s %31s %31s", tok1, tok2, tok3) != 3) continue;
        ScoreNote n;
        n.onset_ms = std::strtof(tok1, nullptr);
        n.dur_ms = rhythm_to_ms(tok2, quarter_ms);
        if (n.dur_ms <= 0.0f) n.dur_ms = beats_to_ms(tok2, quarter_ms);
        if (n.dur_ms <= 0.0f && !tempo_known) n.dur_ms = std::strtof(tok2, nullptr);
        const int by_name = note_name_to_midi(tok3);
        n.midi = (by_name >= 0) ? by_name : int(std::lround(std::strtof(tok3, nullptr)));
        if (n.dur_ms <= 0.0f || n.midi <= 0) continue;
        notes.push_back(n);
    }
    std::fclose(f);
    std::sort(notes.begin(), notes.end(),
              [](const ScoreNote& a, const ScoreNote& b) { return a.onset_ms < b.onset_ms; });
    return !notes.empty();
}

/// Measure the written notes against the audio (joint model: at each fit
/// time the candidates are the written notes alive there). `extra_strikes`,
/// Noise-floor estimate: 5th percentile of 128-sample frame RMS over the
/// take (robust to musical density: quietest content approximates the
/// floor). Used as the similarity reference.
inline float estimate_noise_floor(const std::vector<float>& sig) {
    if (sig.empty()) return 0.0f;
    std::vector<float> frames;
    frames.reserve(sig.size() / 128 + 1);
    for (size_t off = 0; off + 128 <= sig.size(); off += 128) {
        double acc = 0.0;
        for (size_t i = 0; i < 128; ++i) acc += double(sig[off + i]) * double(sig[off + i]);
        frames.push_back(float(std::sqrt(acc / 128.0)));
    }
    if (frames.empty()) return 0.0f;
    std::nth_element(frames.begin(), frames.begin() + frames.size() / 20, frames.end());
    return frames[frames.size() / 20];
}

/// when given, receives detected strikes that no written onset explains
/// (played but not written — energy the score cannot account for).
void measure_score(const std::vector<float>& sig, float sr,
                   std::vector<ScoreNote>& notes,
                   std::vector<float>* extra_strikes = nullptr,
                   float fit_step_ms = 75.0f,
                   const ::kan::InstrumentProfile* profile_in = nullptr,
                   bool want_traj = false,
                   float noise_floor_rms = -1.0f) {
    const ::kan::InstrumentProfile prof =
        (profile_in != nullptr) ? *profile_in : ::kan::InstrumentProfile::keyboard();

    // Detected attacks (for the onset-error column only; the score's own
    // times anchor the measurement, so a missed onset loses no note).
    ::kan::Preconditioner pre(sr);
    ::kan::HybridDetector det(sr);
    det.set_profile(prof);
    std::vector<float> cond(128);
    std::vector<float> detected_ms;
    for (size_t off = 0; off + 128 <= sig.size(); off += 128) {
        pre.process_block(&sig[off], cond.data(), 128);
        if (det.process_onset_block(cond.data(), 128)) {
            detected_ms.push_back(1000.0f * float(off + 128) / sr);
        }
    }

    // Joint measurement over the score span: at every fit time the
    // candidate set = the written notes ALIVE there (struck + sustaining).
    // With the full sounding set modeled, windows may span later attacks
    // without contamination — that is the register-by-note model.
    float t_lo = notes.front().onset_ms, t_hi = 0.0f;
    for (const auto& n : notes) t_hi = std::max(t_hi, n.onset_ms + n.dur_ms);
    // Heavy cavalry: capacity for every alive note at once (analysis-side;
    // the live path keeps its 10-note capacity). All scratch is sized at
    // construction — never on the analysis hot path.
    ::kan::HarmonicChordValidator validator(sr, 8192,
        ::kan::HarmonicChordValidator::kHardCapNotes);
    // Noise floor (similarity reference): explicit override or estimated.
    const float floor_rms = (noise_floor_rms >= 0.0f) ? noise_floor_rms
                                                      : estimate_noise_floor(sig);
    validator.set_noise_floor(floor_rms);
    // Wider of the profile's ranges: saturation is flagged per note after
    // the fit (ISS-1), so exploration may exceed a register's trust band.
    validator.set_search_range_cents(
        std::max(prof.pitch_search_cents, prof.pitch_search_cents_low));
    constexpr size_t kFit = 2048;
    float window[kFit];
    for (float t = t_lo + 200.0f; t < t_hi + 300.0f; t += fit_step_ms) {
        // Adaptive escalation: the fit brings the effort the density
        // demands — all alive notes modeled jointly (no truncation, the
        // ISS-3 cap artifact), extra passes and refinement only when dense,
        // and group-fitting beyond capacity so no note goes unmeasured.
        std::vector<size_t> alive;
        for (size_t i = 0; i < notes.size(); ++i) {
            if (notes[i].onset_ms <= t && t < notes[i].onset_ms + notes[i].dur_ms + 250.0f) {
                alive.push_back(i);
            }
        }
        if (alive.empty()) continue;
        const size_t wend = size_t(t / 1000.0f * sr);
        if (wend < kFit || wend > sig.size()) continue;  // window must fit
        std::memcpy(window, &sig[wend - kFit], kFit * sizeof(float));

        const bool dense = alive.size() > 10;
        validator.set_passes(dense ? 2 : 1);
        validator.set_refine_rounds(dense ? 4 : 2);
        const size_t cap = validator.max_notes();
        for (size_t g0 = 0; g0 < alive.size(); g0 += cap) {
            const size_t g1 = std::min(alive.size(), g0 + cap);
            float midis[::kan::HarmonicChordValidator::kHardCapNotes];
            size_t idxs[::kan::HarmonicChordValidator::kHardCapNotes];
            size_t n = 0;
            for (size_t i = g0; i < g1; ++i) {  // onset order: chord voices
                midis[n] = float(notes[alive[i]].midi);  // stay together
                idxs[n] = alive[i];
                ++n;
            }
            if (!validator.set_expected_chord(midis, n)) continue;
            const ::kan::ChordResult r = validator.validate(window, kFit);
            for (size_t k = 0; k < n; ++k) {
                ScoreNote& sn = notes[idxs[k]];
                const float amp = validator.note_rms(k);
                if (amp > sn.amp_peak) { sn.amp_peak = amp; sn.peak_ms = t; }
                sn.cents_wsum += validator.refined_cents(k) * amp;
                sn.sim_wsum += r.similarity * amp;
                sn.sim_w += amp;
                sn.presence = std::max(sn.presence, r.note_presence[k]);
                sn.levels.push_back({t, amp});
                if (want_traj) {
                    sn.cents_traj.push_back({t, validator.refined_cents(k)});
                }
            }
        }
    }

    // Post-processing per note:
    // (1) saturation (ISS-1): an estimate at its register's cap ran out of
    //     range and is flagged, never used as a pitch fact (I2);
    // (2) release-based end (ISS-2): the note ends at its release -- the
    //     terminal drop below its own sustained (plateau) level. Peak-
    //     relative rules truncate decaying piano notes and misread swelling
    //     winds; this is envelope-shape agnostic (end_drop_db is pack data).
    for (auto& sn : notes) {
        sn.cents_center = sn.sim_w > 0.0f ? sn.cents_wsum / sn.sim_w : 0.0f;
        const float cap = prof.pitch_search_cents_at(sn.midi); // smooth band
        sn.saturated = sn.sim_w > 0.0f && std::fabs(sn.cents_center) >= cap - 1.0f;

        if (sn.levels.empty()) continue;
        const size_t i0 = sn.levels.size() / 4;
        const size_t i1 = (sn.levels.size() * 3) / 4;
        std::vector<float> mid;
        for (size_t i = i0; i <= i1 && i < sn.levels.size(); ++i) {
            mid.push_back(sn.levels[i].second);
        }
        float plateau = 0.0f;
        if (!mid.empty()) {
            std::nth_element(mid.begin(), mid.begin() + mid.size() / 2, mid.end());
            plateau = mid[mid.size() / 2];
        }
        const float thr = std::max(plateau * std::pow(10.0f, -prof.end_drop_db / 20.0f),
                                   sn.amp_peak * 0.01f);
        sn.measured_end_ms = 0.0f;
        for (const auto& lv : sn.levels) {
            if (lv.second >= thr) sn.measured_end_ms = lv.first;
        }

        // Release vs fade-out (batch 03 root cause: ALL 40 duration
        // outliers were negative, on notes with written duration > 3 s --
        // monotone decay crosses the threshold while the key is still held).
        // An observed release drops fast; a decay crossing does not. If the
        // drop around the crossing is gradual, the end was never observed:
        // flag `end_faded` instead of trusting the crossing time.
        if (sn.measured_end_ms > 0.0f && !sn.levels.empty()) {
            // Slope criterion (batch 05 found the fixed-window version
            // over-fired 71%: notes shorter than the 300 ms lookback had
            // no history and were flagged by default). A release falls
            // fast (~0.2 dB/ms, damper), a decay crossing is slow (~0.01
            // dB/ms) -- rate over the available lookback separates them
            // at any note length.
            // The release signature is a local CLIFF near the crossing
            // (damper: ~10+ dB within ~100 ms); a decay crossing sees only
            // 1-3 dB there. Windowed locally so note length cannot change
            // the verdict (the 300 ms fixed window mis-flagged short
            // notes; the long proportional slope mixed plateau and cliff).
            const float t_end = sn.measured_end_ms;
            const float t_first = sn.levels.front().first;
            const float lookback = std::min(100.0f, std::max(1.0f, t_end - t_first));
            float lvl_before = 0.0f, lvl_after = -1.0f;
            for (const auto& lv : sn.levels) {
                // Local window ONLY: reaching back to the attack peak made
                // slow decays look like cliffs (fixture matrix case B/D).
                if (lv.first >= t_end - lookback - 1e-3f && lv.first <= t_end + 1e-3f) {
                    lvl_before = std::max(lvl_before, lv.second);
                }
                if (lvl_after < 0.0f && lv.first > t_end + 1e-3f) lvl_after = lv.second;
            }
            // The cliff is the CONTINUED fall after the crossing sample:
            // on a release the level collapses in the next sample (24 dB
            // measured in fixture A); on a decay or pedal ring it keeps
            // drifting slowly. No sample after = the end was never
            // observed (faded, by definition).
            const float drop_db = (lvl_after > 0.0f && lvl_before > 0.0f)
                ? 20.0f * std::log10(lvl_before / lvl_after) : 0.0f;
            sn.end_faded = lvl_before <= 0.0f || lvl_after < 0.0f
                        || drop_db < 6.0f;
        }
    }

    // Detected strikes no written onset explains (played, not written).
    // Only within the score's span: beyond it the score claims nothing.
    float score_end = 0.0f;
    for (const auto& sn : notes) score_end = std::max(score_end, sn.onset_ms + sn.dur_ms);
    for (float t : detected_ms) {
        if (t > score_end + 500.0f) continue;
        bool explained = false;
        for (auto& sn : notes) {
            if (std::fabs(sn.onset_ms - t) <= 150.0f) { explained = true; break; }
        }
        if (!explained && extra_strikes != nullptr) extra_strikes->push_back(t);
    }
    // Onset-error matching (informational; the score's times anchor).
    for (auto& n : notes) {
        float best = 200.0f;
        for (float t : detected_ms) best = std::min(best, std::fabs(t - n.onset_ms));
        n.onset_err_ms = best;
    }

}

/// A pitch OUTSIDE the written set that explains significant residual
/// energy at a strike (the "mash"/cheat-superset case). Measurement only:
/// "these pitches sound at this strike and the score does not account for
/// them" — penalizing the precision is the game's call (G6).
struct ExtraPitch {
    float t_ms = 0.0f;
    int midi = 0;
    float sim_gain = 0.0f; ///< similarity gain when added to the written set
    float amp = 0.0f;      ///< its fitted level
};

/// Per-strike purity: how much of the strike's energy the written set does
/// NOT explain. The robust primary fact for the game's optional purity mod
/// (game-judging-guide G6): measured 0.1% on a clean strike vs 6.1% on a
/// mash of correct chord + 3 random keys. Candidate lists are noisy (recall
/// is total, neighbors leak in); this number is not.
struct StrikePurity {
    float t_ms = 0.0f;
    float sim0 = 0.0f;       ///< similarity of the written set alone
    float unexplained = 0.0f; ///< 1 - sim0: energy outside the score
    int alive = 0;            ///< candidates alive (keyed + pedal-extended)
    int keyed = 0;            ///< notes actually keyed (key-hold count: the
                              ///< right density context for thresholds)
};

/// Set-extension search per strike (heavy cavalry: one extra fit per
/// chromatic candidate). Detects extra sounding pitches that the written
/// set cannot explain — including keys mashed alongside correct ones,
/// whose energy otherwise gets absorbed into the written notes' fits
/// (near-miss absorption: amps inflate, cents bend; sim alone does NOT
/// drop reliably).
inline void search_extra_pitches(const std::vector<float>& sig, float sr,
                                 const std::vector<ScoreNote>& notes,
                                 std::vector<ExtraPitch>* out = nullptr,
                                 std::vector<StrikePurity>* purity = nullptr,
                                 size_t strike_stride = 1,
                                 float t_ref_ms = 200.0f,
                                 // Pedal-aware candidate set: on piano the
                                 // sounding set outlives the key holds
                                 // (sustain pedal). Released notes stay
                                 // candidates for pedal_ms after note-off,
                                 // else their ringing is miscounted as
                                 // "unexplained" (the clean baseline was
                                 // polluted exactly there).
                                 float pedal_ms = 0.0f,
                                 // Calibrated on the mash counterexample
                                 // (score_error_test case 9): true extra keys
                                 // show gains 0.034-0.044 with amps >= 0.10;
                                 // a clean strike has sim0 ~0.999 (no room).
                                 float sim_gain_min = 0.03f,
                                 float amp_min = 0.05f) {
    ::kan::HarmonicChordValidator validator(sr, 8192,
        ::kan::HarmonicChordValidator::kHardCapNotes);
    // Same noise-floor semantics as measure_score (the purity baseline was
    // calibrated on it and must be recalibrated with it -- checklist).
    validator.set_noise_floor(estimate_noise_floor(sig));
    validator.set_search_range_cents(60.0f);
    validator.set_passes(1);
    validator.set_refine_rounds(2);
    constexpr size_t kFit = 2048;
    float window[kFit];

    // Strikes = distinct written onsets (30 ms clustering).
    std::vector<float> strikes;
    for (const auto& n : notes) {
        if (strikes.empty() || n.onset_ms - strikes.back() > 30.0f) {
            strikes.push_back(n.onset_ms);
        }
    }
    size_t strike_idx = 0;
    for (float onset : strikes) {
        if ((strike_idx++ % std::max<size_t>(1, strike_stride)) != 0) continue;
        const float t = onset + t_ref_ms;
        const size_t wend = size_t(t / 1000.0f * sr);
        if (wend < kFit || wend > sig.size()) continue;
        std::memcpy(window, &sig[wend - kFit], kFit * sizeof(float));

        std::vector<size_t> alive;
        for (size_t i = 0; i < notes.size(); ++i) {
            const float hold = notes[i].dur_ms + 250.0f + pedal_ms;
            if (notes[i].onset_ms <= t && t < notes[i].onset_ms + hold) {
                alive.push_back(i);
            }
        }
        if (alive.empty()) continue;

        int lo = 108, hi = 21;
        std::vector<float> base;
        for (size_t i : alive) {
            base.push_back(float(notes[i].midi));
            lo = std::min(lo, notes[i].midi);
            hi = std::max(hi, notes[i].midi);
        }
        if (!validator.set_expected_chord(base.data(), base.size())) continue;
        const float sim0 = validator.validate(window, kFit).similarity;
        if (purity != nullptr) {
            int keyed = 0;
            for (size_t i = 0; i < notes.size(); ++i) {
                if (notes[i].onset_ms <= t &&
                    t < notes[i].onset_ms + notes[i].dur_ms + 250.0f) ++keyed;
            }
            purity->push_back({t, sim0, 1.0f - sim0, int(alive.size()), keyed});
        }
        if (out == nullptr) continue;   // purity-only mode: one fit per strike
        if (sim0 >= 0.995f) continue;   // nothing left unexplained

        for (int cand = std::max(21, lo - 12); cand <= std::min(108, hi + 12); ++cand) {
            bool taken = false;
            for (float m : base) {
                if (int(m) == cand) { taken = true; break; }
            }
            if (taken) continue;
            std::vector<float> ext = base;
            ext.push_back(float(cand));
            if (!validator.set_expected_chord(ext.data(), ext.size())) continue;
            const ::kan::ChordResult r = validator.validate(window, kFit);
            const float amp = validator.note_rms(ext.size() - 1);
            const float gain = r.similarity - sim0;
            if (gain >= sim_gain_min && amp >= amp_min) {
                out->push_back({t, cand, gain, amp});
            }

        }
    }
}

} // namespace score_verify
} // namespace kan
