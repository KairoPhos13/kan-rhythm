# Upstream issue — ggml-vulkan q8_0 integer dot product gives wrong
# results on RADV NAVI21 (Mesa)

**Status:** fixed downstream (hardened); ready to file upstream at
https://github.com/ggerganov/ggml/issues

## Title

q8_0 integer dot-product matmul produces wrong results with BS-RoFormer
on AMD RX 6800 (RADV NAVI21)

## Body

**Setup:** GGML commit 3612c0af93f2 (master, 2026-10-12) with Vulkan
backend, AMD Radeon RX 6800, RADV NAVI21 (Mesa). ggml reports:
`int dot: 1, fp16 dot2, no matrix cores`. Model: BS-RoFormer SW 6-stem
separation (BSRoformer.cpp a7b9625), q8_0 quantized GGUF, in-process
GGML inference.

**Symptom:** inference completes at normal speed but the OUTPUT IS WRONG:
stems leak into each other (instruments audible in stems that should be
silent; main parts attenuated). Same q8_0 model is correct on CPU, and
correct on the same GPU with `GGML_VK_DISABLE_INTEGER_DOT_PRODUCT=1`.
F16 on the same GPU is correct. CPU vs GPU similarity of content stems:
~0-3 dB SNR with int dot enabled vs 50-56 dB disabled.

**Ruled out:** model file (correct on CPU), quantization itself (q8 CPU
matches F16 within 0.5 dB), stem ordering, caller code (10-line C
driver reproduces), input conversion (44.1 kHz float input equally
wrong), build flags (no KissFFT; only USE_OPENMP).

**Reproduce:** BS-RoFormer SW 6-stem (62 bands, dim 256, depth 12,
chunk 588800, overlap 2), q8_0 GGUF, separate any music excerpt; compare
stems against the CPU run. Small clip sufficient (30 s).

**Impact:** silent audio corruption (no error, normal runtime) — very
dangerous for any inference consumer shipping q8_0 + Vulkan.

## Downstream hardening (this repository)

- `GGML_VK_DISABLE_INTEGER_DOT_PRODUCT=1` set in the process
  environment before ggml initialises Vulkan (user override respected).
- One-time **CPU/GPU parity gate** (`kan::roformer_device_parity_ok`):
  built-in probe separated on the default device and on CPU; content
  stems must match at >= 30 dB SNR or the session falls back to CPU.
  Marker cached per (model, ggml and BSRoformer pins, backend tag); a pin
  update re-probes, a plain rebuild does not (the CPU pass takes minutes).
  A broken GPU path can never silently produce a level's stems.
- ggml and BSRoformer.cpp are **pinned** to tested commits.
- Verification on the affected hardware class: `Vulkan0` vs `CPU`
  probe = 59.5 dB SNR (2026-10-14).
