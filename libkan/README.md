# libkan

Header-only real-time DSP library at the core of **kan!**: the hybrid
detection pipeline that decouples rhythm from pitch.

- **Stage 0 preconditioning:** zero-latency 4th-order 35 Hz high-pass +
  adaptive noise gate calibrated to room silence (kills stand thumps and
  ghost onsets from ambient noise)
- **Fast path (~2.6 ms):** onset detection (HFC energy derivative) for
  instant rhythmic hit judgment
- **Harmonic path (~10–30 ms):** score-informed pitch confirmation
  (bounded NSDF matcher ±2 semitones), monophonic pitch (lightweight YIN,
  or Cycfi Q BACF via `libkan_bacf`) and note/cents reporting vs A4 = 440 Hz
- **Lock-free SPSC ring buffer** for audio-thread → consumer-thread transport
  (zero allocations, zero locks)

```
libkan/
├── include/libkan/
│   ├── ring_buffer.hpp         Lock-free SPSC queue
│   ├── precondition.hpp        Stage 0: 35 Hz high-pass + adaptive noise gate
│   ├── instrument_profile.hpp  Per-instrument profile presets (blanking, range)
│   ├── calibration.hpp         Tuning compensation: Δc calibrator, Δc1..Δc6 matrix, EMA tonal center
│   ├── technique.hpp           Technique validators: vibrato/bend/slide curves, envelope shapes, spectral signatures
│   ├── dsp_detector.hpp        HybridDetector: onset + stage-2 blanking + YIN pitch
│   ├── bacf_detector.hpp       Score-informed Cycfi Q BACF detector (needs libkan_bacf)
│   └── bounded_pitch.hpp       Bounded-lag NSDF matcher: fast ±2-semitone confirmation
├── tests/
│   ├── dsp_test.cpp            Offline test (synthesized detuned A4)
│   ├── precondition_test.cpp   Offline test (synthetic noise / thumps)
│   ├── blanking_test.cpp       Offline test (transient blanking / profiles)
│   ├── test_signals.hpp        Shared deterministic synthesized-signal helpers
│   ├── bacf_test.cpp           Offline test (BACF: detuned notes, band rejection)
│   └── bacf_bench.cpp          A/B bench: BACF vs YIN vs bounded matcher (ctest)
└── TODO.md                     Milestone 1 task list
```

Detector selection and measured latency/accuracy: [docs/dsp/bacf-vs-yin.md](../docs/dsp/bacf-vs-yin.md) ·
gate tracking: [docs/dsp/latency-measurements.md](../docs/dsp/latency-measurements.md).

## Usage

```cmake
target_link_libraries(your_target PRIVATE libkan)        # dependency-free core
target_link_libraries(your_target PRIVATE libkan_bacf)   # + Cycfi Q BACF backend
```

```cpp
#include "libkan/ring_buffer.hpp"
#include "libkan/dsp_detector.hpp"
#include "libkan/precondition.hpp"
#include "libkan/instrument_profile.hpp"
#include "libkan/bounded_pitch.hpp"
#include "libkan/bacf_detector.hpp"   // requires libkan_bacf (Cycfi Q, MIT)
```

`libkan_bacf` exists only when `KAN_ENABLE_BACF=ON` (default), which fetches
Cycfi Q v1.5.0 (MIT) via FetchContent. The core `libkan` target stays
dependency-free.

## Constraints

- Real-time safety: code on the audio callback path must not allocate, block,
  lock, or do I/O (root `CONTRIBUTING.md` §3)
- Dependency-free core: `libkan` must not depend on anything else in the
  monorepo; future third-party deps must be MIT/BSD/Apache only (no GPL)
- Designed to be extracted into its own repository (`libkan` / `kan-dsp`) later

Spec: [docs/SPECIFICATION.md](../docs/SPECIFICATION.md) §2 ·
Gates: [docs/ROADMAP.md](../docs/ROADMAP.md) Milestone 1
