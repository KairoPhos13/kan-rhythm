# Third-party notices: libkan

Third-party code and data that libkan compiles in or links. Anything that
ships libkan (the kan! client included) must carry these notices.

## Compiled into libkan

### Basic Pitch (neural transcription)

- **What:** the ICASSP 2022 model's weights and its CQT constants
  (`src/basic_pitch_weights.inc`, exported from `nmp.onnx` by
  `tools/basic_pitch_export.py`), and a C++ port of its inference and note
  decoding (`src/basic_pitch.cpp`).
- **From:** https://github.com/spotify/basic-pitch at `fa5997a`
  (`basic_pitch/saved_models/icassp_2022/nmp.onnx`, sha256
  `2c3c1d14…2c059a0ec`).
- **Licence:** Apache License 2.0, Copyright 2022 Spotify AB. Full text:
  [`licenses/basic-pitch-LICENSE`](licenses/basic-pitch-LICENSE). Its
  NOTICE file: [`licenses/basic-pitch-NOTICE`](licenses/basic-pitch-NOTICE).
- **Changes:** the network runs in plain C++ instead of
  TensorFlow/ONNX, and there is an optional one-note-at-a-time
  decoding (`NoteOptions::monophonic`) that the reference does not have.
- **Paper:** Bittner, Bosch, Rubinstein, Meseguer-Brocal, Ewert, "A
  Lightweight Instrument-Agnostic Model for Polyphonic Note Transcription
  and Multipitch Estimation", ICASSP 2022.

### GGML and BSRoformer.cpp (source separation)

Fetched at build time and compiled into `libkan_roformer` (pins in
`CMakeLists.txt`): ggml (MIT, Copyright (c) 2023-2026 The ggml authors)
and BSRoformer.cpp (MIT, Copyright (c) 2026 chenmozhijin <cmzj@cmzj.org>).

## Compiled into consumers of libkan

- **miniaudio** (MIT-0 / public domain dedication): single-header audio
  device I/O and WAV decoding, fetched at build time (0.11.25, root
  `CMakeLists.txt` and `game/forge/native`) and compiled into the
  consumers that ship: the kan! client's native bridge
  (`game/forge/native/kan_forge_api.cpp`) and the `tools/`/`apps/`
  utilities. Not part of libkan itself; the upstream copyright header
  stays in the fetched `miniaudio.h`.

## Optional backends (fetched by the root CMakeLists)

- Cycfi Q (MIT): `libkan_bacf`.
- KissFFT (BSD-3-Clause): `libkan_fft`.
- Signalsmith Stretch (MIT): `libkan_stretch`.
