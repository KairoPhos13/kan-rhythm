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
#include <cstdio>
#include <cstring>
#include <vector>

namespace score_verify {

constexpr size_t kMaxScoreNotes = 10;

struct ScoreNote {
    float onset_ms = 0.0f, dur_ms = 0.0f;
    int midi = 0;
    float presence = 0.0f, cents = 0.0f, amp_peak = 0.0f;
    float measured_onset_ms = 0.0f, measured_end_ms = 0.0f;
    float peak_ms = 0.0f; ///< when the note's fitted level peaked
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
/// when given, receives detected strikes that no written onset explains
/// (played but not written — energy the score cannot account for).
void measure_score(const std::vector<float>& sig, float sr,
                   std::vector<ScoreNote>& notes,
                   std::vector<float>* extra_strikes = nullptr) {

    // Detected attacks (for the onset-error column only; the score's own
    // times anchor the measurement, so a missed onset loses no note).
    kan::Preconditioner pre(sr);
    kan::HybridDetector det(sr);
    det.set_profile(kan::InstrumentProfile::keyboard());
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
    kan::HarmonicChordValidator validator(sr, 8192);
    validator.set_search_range_cents(60.0f); // permissive band (field detune)
    validator.set_passes(1);
    validator.set_refine_rounds(2);
    constexpr size_t kFit = 2048;
    float window[kFit];
    for (float t = t_lo + 200.0f; t < t_hi + 300.0f; t += 75.0f) {
        float midis[kMaxScoreNotes];
        size_t idxs[kMaxScoreNotes];
        size_t n_live = 0;
        for (size_t i = 0; i < notes.size() && n_live < kMaxScoreNotes; ++i) {
            if (notes[i].onset_ms <= t && t < notes[i].onset_ms + notes[i].dur_ms + 250.0f) {
                midis[n_live] = float(notes[i].midi);
                idxs[n_live] = i;
                ++n_live;
            }
        }
        if (n_live == 0) continue;
        const size_t wend = size_t(t / 1000.0f * sr);
        if (wend < kFit || wend > sig.size()) continue;  // window must fit
        std::memcpy(window, &sig[wend - kFit], kFit * sizeof(float));
        if (!validator.set_expected_chord(midis, n_live)) continue;
        const kan::ChordResult r = validator.validate(window, kFit);
        for (size_t k = 0; k < n_live; ++k) {
            ScoreNote& sn = notes[idxs[k]];
            const float amp = validator.note_rms(k);
            if (amp > sn.amp_peak) { sn.amp_peak = amp; sn.peak_ms = t; }
            sn.cents_wsum += validator.refined_cents(k) * amp;
            sn.sim_wsum += r.similarity * amp;
            sn.sim_w += amp;
            sn.presence = std::max(sn.presence, r.note_presence[k]);
            if (amp >= sn.amp_peak * 0.10f) {
                sn.measured_end_ms = t; // extended while audible
            }
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

} // namespace score_verify
