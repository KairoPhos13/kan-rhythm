#pragma once
// Neural transcription: Basic Pitch (Spotify, Apache-2.0) run in plain C++.
//
// Owner decision 2026-10-15: a trained transcriber gives the editor a
// better starting score than more hand-made rules, and its posteriorgrams
// (how likely each note sounds / starts, every ~11.6 ms) are what a judge
// can ask "is the expected note sounding now?". The network is the
// ICASSP 2022 model (Bittner et al., "A Lightweight Instrument-Agnostic
// Model for Polyphonic Note Transcription and Multipitch Estimation"):
// ~17k weights, compiled into libkan (src/basic_pitch_weights.inc, made by
// tools/basic_pitch_export.py). No ONNX, TensorFlow or Python at runtime.
//
// The port follows the reference (basic_pitch/inference.py and
// note_creation.py) step by step, so the reference numbers carry over:
// 2 s windows at 22050 Hz overlapping by 30 frames, the CQT2010v2 front
// end, harmonic stacking, the six convolutions, and the note decoding
// with inferred onsets and the "melodia trick". libkan_basic_pitch_test
// checks the posteriorgrams against values from the reference onnxruntime
// run and the notes on synthetic audio with exact truth.
//
// Offline (NOT real-time): it allocates and uses worker threads.
#include <cstddef>
#include <vector>

namespace kan::basic_pitch {

inline constexpr int kSampleRate = 22050;
inline constexpr int kHop = 256;               // samples per frame (~11.6 ms)
inline constexpr int kNotes = 88;              // A0 (MIDI 21) .. C8
inline constexpr int kContourBins = 264;       // 3 bins per semitone from A0
inline constexpr int kMidiOffset = 21;

/// The network's outputs for a whole recording, frame-major.
struct Posteriorgrams {
    int frames = 0;
    std::vector<float> note;      // frames x 88: the note sounds
    std::vector<float> onset;     // frames x 88: the note starts
    std::vector<float> contour;   // frames x 264: pitch, 1/3 semitone
    /// Time (s) of frame `i`, as the reference computes it.
    double frame_time(int i) const;
};

/// One note decoded from the posteriorgrams.
struct Note {
    double start_s = 0.0;
    double end_s = 0.0;
    int midi = 0;
    float amplitude = 0.0f;  // mean note activation, 0..1
    float cents = 0.0f;      // median offset from the contour, +/-50
};

struct NoteOptions {
    float onset_threshold = 0.5f;     // reference defaults
    float frame_threshold = 0.3f;
    float min_note_ms = 127.7f;
    bool infer_onsets = true;
    bool melodia_trick = true;
    int min_midi = 0;                 // 0 = no limit
    int max_midi = 0;
    /// One note at a time (wind, voice): the model is polyphonic and also
    /// reports a strong harmonic (12, 19 or 24 semitones up) as a note.
    /// A note mostly covered by one a harmonic below it is dropped; of two
    /// notes still mostly overlapping, the weaker goes; a remaining overlap
    /// ends the earlier note where the later starts. Not in the reference.
    bool monophonic = false;
};

/// Runs the network over mono audio at any sample rate (resampled to
/// 22050 Hz). `threads` 0 = hardware concurrency.
Posteriorgrams infer(const float* audio, size_t n, double sample_rate, int threads = 0);

/// Runs the network over one window of exactly 43844 samples at 22050 Hz
/// (the reference model's input), writing 172 frames of each output.
/// For tests against the reference.
void infer_window(const float* window, float* note, float* onset, float* contour);

/// Decodes notes, as basic_pitch.note_creation.model_output_to_notes.
std::vector<Note> notes(const Posteriorgrams& p, const NoteOptions& options = {});

/// Resamples mono audio (windowed sinc). Exposed for tests and tools.
std::vector<float> resample(const float* audio, size_t n, double from_rate, double to_rate);

}  // namespace kan::basic_pitch
