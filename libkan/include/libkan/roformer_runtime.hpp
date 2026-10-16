#pragma once
// In-process GGUF RoFormer separation runtime (single stack: GGML).
// The official separation flow is SELF-CONTAINED (editor-pipeline s7b):
// no installed separator binary is required — only the auto-downloaded
// GGUF model. Compiled when KAN_ENABLE_ROFORMER is ON (KAN_HAVE_ROFORMER_RUNTIME).
//
// Thread-safety: one Inference instance per call (not pooled).
#include <functional>
#include <string>
#include <vector>

namespace kan {

/// True when the runtime is compiled in and can run (device init works).
bool roformer_runtime_available();

/// SNR in dB between reference and test signals (120 = identical).
double roformer_snr_db(const std::vector<float>& ref,
                       const std::vector<float>& test);

/// One-time CPU/GPU parity gate (device correctness guard): separates a
/// built-in probe on the default device and on CPU; content stems must
/// match at >= 30 dB SNR. Cached per (model, ggml and BSRoformer pins, backend);
/// a pin update re-probes, a plain rebuild does not.
/// A failure means the caller must fall back to CPU.
bool roformer_device_parity_ok(const std::string& model_path,
                               std::string* detail = nullptr);

/// Model requirements exposed to the acquisition layer (44.1 kHz models).
int roformer_sample_rate();

/// Number of stems the current model family separates (6).
int roformer_stem_count();

/// Separate a WAV file into stems written as <out_dir>/<name>.wav in
/// official order: bass, drums, other, vocals, guitar, piano.
/// `progress` receives 0..1 fractions; on FAILURE the progress is left
/// wherever it stopped — never falsely completed (a failed run must not
/// report 100%).
/// Handles: any input sample rate (linear resample to model rate),
/// mono (duplicated) and stereo input.
bool roformer_separate_wav(const std::string& input_wav,
                           const std::string& out_dir,
                           const std::string& model_path,
                           const std::function<void(float)>& progress = nullptr);

} // namespace kan
