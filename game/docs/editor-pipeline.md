# KAN Editor Pipeline — spec

This file is the game-side specification for the level-editor pipeline
built on libkan. The implementation agent prompt lives at
`docs/implementation/kan-editor-agent-prompt.md`.

## §7b. Self-contained separation (DONE, 2026-10-13)

The official separation flow must not require an installed separator:

1. **Runtime compiled into libkan.** `libkan_roformer` (CMake option
   `KAN_ENABLE_ROFORMER`, default ON) compiles the GGUF RoFormer runtime
   (BSRoformer.cpp + GGML, both MIT) into the library. `kan::separate_stems`
   works in-process with only the auto-downloaded GGUF model.
2. **`find_separator()` probes the RUNTIME**, not just the model: it
   returns `runtime:<model>` when the compiled runtime is present and the
   model is available (auto-downloaded). The external `bs_roformer-cli`
   remains as DEVELOPER TOOLING fallback only (provisional).
3. **Progress honesty:** a failed run never reports 100% (neither the
   in-process path nor the process path).
4. **Input conditioning in the runtime:** any WAV sample rate is
   linearly resampled to the model rate (44.1 kHz); mono is duplicated
   to stereo.

Status: verified end-to-end (clip separation in-process, 6 stems in
official order; failure semantics covered by code + review).

Until §7b landed, demux was developer tooling and the game-side
pre-check was provisional — both are now superseded by the in-process
runtime.
