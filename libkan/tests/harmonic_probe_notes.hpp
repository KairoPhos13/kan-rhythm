#pragma once
// Hand-written candidate annotation for the Phase-1 chord-validation
// experiment on real polyphonic piano material (left-hand chords under a
// right-hand melody — the Hallelujah piano tutorial take:
// test_results/audio/07-hallelujah-piano-tutorial_piano.wav, 254.3 s,
// 48 kHz stereo; kan_analyze reports 244 attacks).
//
// How the annotation was produced (reproducible):
//   libkan_harmonic_probe --dump <wav>
// prints, per attack below, the long-window spectral content at +200 ms and
// +800 ms after the onset (harmonic-sum fundamental scores + top spectral
// peaks, 341 ms Hann windows zero-padded to 131072). The `sounding` lists
// were hand-written from that output: a candidate is included when its f0
// carries a real spectral line (not just harmonic-sum aliasing — e.g. the
// 588 Hz line under G3 is G3's 3rd harmonic, not a D5 note), ordered loudest
// line first. They are HYPOTHESES ("these pitches are sounding here"), not
// ground truth — the probe measures each candidate's presence / refined
// cents and reports agreement with the independent f0-line evidence.
// `controls` are pitches believed ABSENT at that attack (no f0 line within
// +/-40 c, chosen as easy and as hard chromatic neighbors): their measured
// presence is the false-presence rate.
//
// Labels (used for the blind mono-vs-polyphonic experiment): mono = 1
// sounding candidate (M* — lone single-fundamental moments found by
// scanning all 244 onsets), poly = >= 3 (A*). Five A-attacks list 7-8
// sounding candidates: real pedal-rich piano texture exceeding
// HarmonicChordValidator::kMaxNotes (6) — the experiment for the
// "candidates beyond kMaxNotes" design question.
#include <cstddef>

namespace kan_probe {

/// One annotated attack: onset time + candidate pitches.
struct Attack {
    const char* id;        ///< stable id (report rows reference it)
    float t_ms;            ///< onset time in the recording
    int sounding[8];       ///< candidate midis believed sounding (loud first)
    int n_sounding;
    int controls[4];       ///< negative controls (believed absent)
    int n_controls;
};

} // namespace kan_probe

// clang-format off
static const kan_probe::Attack kAttacks[] = {
    // id     t_ms   sounding (loud first)                  n  controls        n
    {"A01",  2979.0f, {60, 48, 67, 55},                    4, {65, 57},       2},
    {"A02",  3320.0f, {48, 55, 60, 67},                    4, {69, 53},       2},
    {"M05",  8643.0f, {48},                               1, {50, 47},       2},
    {"A03", 16667.0f, {67, 48, 52, 55, 60},                5, {65, 59},       2},
    {"A04", 26971.0f, {72, 67, 55, 53, 62},                5, {69, 66},       2},
    {"M01", 34309.0f, {64},                               1, {65, 62},       2},
    {"A05", 34976.0f, {65, 53, 67, 69, 72, 41, 64},        7, {62, 59},       2},
    {"A06", 49301.0f, {43, 62, 55, 58, 47, 67},            6, {57, 65},       2},
    {"A07", 59304.0f, {67, 48, 55, 60},                    4, {65, 57},       2},
    {"A08", 70979.0f, {67, 60, 64, 55, 48, 43},            6, {65, 59},       2},
    {"A09", 80304.0f, {74, 55, 67, 62},                    4, {59, 65},       2},
    {"M02", 86309.0f, {64},                               1, {65, 62},       2},
    {"A10", 96307.0f, {69, 45, 53, 57, 41, 65, 60, 48},    8, {62, 59},       2},
    {"A11",108309.0f, {67, 48, 79, 55, 60},                5, {65, 57},       2},
    {"A12",119309.0f, {64, 48, 55, 60, 67},                5, {59, 69},       2},
    {"A13",130971.0f, {74, 72, 67, 62, 55},                5, {59, 60},       2},
    {"M03",138309.0f, {64},                               1, {65, 62},       2},
    {"A14",139301.0f, {45, 69, 57, 65, 72, 53, 41, 60},    8, {62, 59},       2},
    {"A15",152973.0f, {62, 55, 74, 43, 48, 67},            6, {59, 65},       2},
    {"A16",161304.0f, {52, 57, 69, 45, 71, 64},            6, {60, 65},       2},
    {"M06",164643.0f, {48},                               1, {50, 47},       2},
    {"A17",174016.0f, {67, 55, 43, 74, 62},                5, {59, 65},       2},
    {"A18",182976.0f, {74, 67, 72, 62, 55},                5, {59, 60},       2},
    {"M04",190309.0f, {64},                               1, {65, 62},       2},
    {"A19",191301.0f, {45, 69, 65, 57, 53, 72, 60, 41},    8, {62, 59},       2},
    {"A20",205301.0f, {62, 43, 55, 74, 67},                5, {59, 65},       2},
    {"A21",220976.0f, {48, 50, 43, 55, 72, 62, 74, 67},    8, {59, 64},       2},
    {"A22",237301.0f, {43, 55, 62, 59, 47, 67},            6, {65, 57},       2},
};
// clang-format on

static constexpr size_t kAttackCount = sizeof(kAttacks) / sizeof(kAttacks[0]);
