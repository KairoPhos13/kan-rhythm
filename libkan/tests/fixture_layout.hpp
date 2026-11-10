#pragma once
// Shared fixture layout: the synthesized WAV set for the loopback /
// regression harness, with the ground-truth onset positions and pitches
// the tests assert against. Single source of truth for generator and test.
#include <cstddef>
#include <cstdint>

namespace kan_test {

constexpr uint32_t kFixtureRate = 48000;

// Fixture file names (written into libkan/tests/fixtures/ by fixture_gen).
constexpr const char* kFxSineSweep = "sine_sweep.wav";     // 80 -> 1200 Hz log sweep
constexpr const char* kFxDetuned = "detuned_notes.wav";    // plucked notes, known detunes
constexpr const char* kFxChords = "chords.wav";            // major / minor / wrong string
constexpr const char* kFxNoise = "noise.wav";              // room-level noise: ZERO onsets
constexpr const char* kFxPercussive = "percussive.wav";    // noise bursts: onsets, NO pitch
constexpr const char* kFxOnsetGrid = "onset_grid.wav";     // latency grid: burst every 1 s

// detuned_notes.wav: 3 plucked notes with a short pick transient each.
struct NoteEvent {
    float t_seconds;      ///< onset position (ground truth)
    float freq_hz;        ///< synthesized fundamental (detune included)
    const char* name;
};
inline constexpr NoteEvent kDetunedNotes[] = {
    {0.50f, 442.54f, "A4+10c"},
    {1.50f, 518.79f, "C5-15c"},
    {2.50f, 667.13f, "E5+21c"},
};
inline constexpr size_t kDetunedCount = sizeof(kDetunedNotes) / sizeof(kDetunedNotes[0]);

// chords.wav: strummed triads (5 ms per string), C major / C minor /
// C-major-with-wrong-fifth (G#), at these onset times.
inline constexpr float kChordTimes[3] = {0.50f, 2.00f, 3.50f};
inline constexpr size_t kChordCount = 3;

// percussive.wav: broadband noise bursts (no tonal content).
inline constexpr float kPercTimes[3] = {0.50f, 1.50f, 2.50f};
inline constexpr size_t kPercCount = 3;

// onset_grid.wav: tone bursts (100 ms, 440 Hz) every second, the latency
// grid. Detection latency = detected_onset_sample - t*rate, block-quantized.
inline constexpr float kGridPeriod = 1.0f;
inline constexpr float kGridFirst = 0.50f;
inline constexpr size_t kGridCount = 3;
inline constexpr float kGridDuration = 3.5f;

// noise.wav / sine_sweep.wav durations.
inline constexpr float kNoiseDuration = 3.0f;
inline constexpr float kSweepDuration = 3.0f;

} // namespace kan_test
