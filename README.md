# kan-rhythm

**You kan!** Hybrid DSP engine in C++20 for real-time rhythm detection and
pitch/tuning analysis, with a buffer latency of **~2.66 ms** (128 samples @ 48 kHz).

No bureaucracy: CMake downloads [miniaudio](https://github.com/mackron/miniaudio)
automatically, opens your microphone in ultra-low-latency mode, moves the data
through a **lock-free (SPSC) ring buffer** without ever blocking the audio
thread, and detects in real time:

- 🥁 **Onset** — the instant rhythmic hit (~3 ms)
- 🎵 **Musical note** — monophonic pitch via YIN autocorrelation + deviation
  in **cents** relative to A4 = 440 Hz (~21 ms post-attack)

> **Collaboration language: English.** All code, comments, commits, docs and
> discussions in this repository are in English. See [CONTRIBUTING.md](CONTRIBUTING.md).

## Architecture

```
Microphone (RT callback, zero blocking/allocs)
        │
        ▼
LockFreeRingBuffer<float, 16384>   ← atomic SPSC queue (libkan/include/libkan/ring_buffer.hpp)
        │
        ▼
HybridDetector                     ← hybrid pipeline (libkan/include/libkan/dsp_detector.hpp)
  ├── process_onset_block()        ← FAST PATH: energy/HFC-derivative jump (~2.6 ms/block)
  └── detect_pitch()               ← HARMONIC PATH: lightweight YIN + parabolic interpolation (~20 ms)
        │
        ▼
Real-time CLI (apps/kan_cli)       ← ⚡ ONSET! → ✅ HIT CONFIRMED (note, Hz, cents)
```

## Repository layout (monorepo)

Each top-level component is designed to be splittable into its own repository later.

```
kan-rhythm/
├── CMakeLists.txt      Superbuild: fetches miniaudio, adds all components
├── CONTRIBUTING.md     Ground rules (English-only policy, RT-audio rules)
├── AGENTS.md           Short rules digest for AI coding agents
├── libkan/             Header-only real-time DSP library (the engine core)
│   ├── include/libkan/
│   └── tests/          Offline DSP tests with synthesized signals
├── apps/
│   └── kan_cli/        Real-time microphone benchmark CLI
├── game/               Game client (future)
├── charts/             Song/chart data + format spec (future)
├── tools/              CLI utilities: validators, analysis, calibration (future)
├── donations/          Donation engine — financial support for the project (future)
├── assets/             Graphics, sounds, fonts (future)
└── docs/               Architecture docs and design decisions (future)
```

## Requirements

- CMake ≥ 3.20
- C++20 compiler (GCC/Clang/MSVC)
- Internet connection the first time (FetchContent downloads miniaudio, MIT-0)
- Linux: ALSA (`libasound2-dev` on Debian/Ubuntu)

## Build & run

```bash
cmake -B build -DCMAKE_BUILD_TYPE=Release
cmake --build build -j
./build/apps/kan_cli/kan_cli
```

Play a guitar string, a piano key, or blow a flute in front of the microphone:

```
⚡ [ONSET! 300/PERFECT] Hit detected (<3ms) -> Evaluating pitch...
✅ [HIT CONFIRMED at +22.3 ms] Note:  A4 | Freq:  441.2 Hz | Offset: +4.7 cents
🥁 [PERCUSSIVE / MUTE] Rhythmic attack without sustained tone
```

## Tests

```bash
ctest --test-dir build --output-on-failure
```

The offline DSP test feeds a synthesized, slightly detuned A4 (+10 cents)
through the full pipeline and asserts onset detection plus pitch accuracy
within ±3 Hz.

## License

MIT — see [LICENSE](LICENSE). miniaudio is MIT-0 / public domain.
