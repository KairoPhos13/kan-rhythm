# libkan

Header-only real-time DSP library at the core of **kan!**: the hybrid
detection pipeline that decouples rhythm from pitch.

- **Stage 0 preconditioning:** zero-latency 4th-order 35 Hz high-pass +
  adaptive noise gate calibrated to room silence (kills stand thumps and
  ghost onsets from ambient noise)
- **Fast path (~2.6 ms):** onset detection (HFC energy derivative) for
  instant rhythmic hit judgment
- **Harmonic path (~20–30 ms):** monophonic pitch (YIN today, BACF planned)
  and score-informed chord validation, reported in cents vs A4 = 440 Hz
- **Lock-free SPSC ring buffer** for audio-thread → consumer-thread transport
  (zero allocations, zero locks)

```
libkan/
├── include/libkan/
│   ├── ring_buffer.hpp         Lock-free SPSC queue
│   ├── precondition.hpp        Stage 0: 35 Hz high-pass + adaptive noise gate
│   ├── instrument_profile.hpp  Per-instrument profile presets (blanking, range)
│   └── dsp_detector.hpp        HybridDetector: onset + stage-2 blanking + pitch
├── tests/
│   ├── dsp_test.cpp            Offline test (synthesized detuned A4)
│   ├── precondition_test.cpp   Offline test (synthetic noise / thumps)
│   └── blanking_test.cpp       Offline test (transient blanking / profiles)
└── TODO.md                     Milestone 1 task list
```

## Usage

```cmake
target_link_libraries(your_target PRIVATE libkan)
```

```cpp
#include "libkan/ring_buffer.hpp"
#include "libkan/dsp_detector.hpp"
```

## Constraints

- Real-time safety: code on the audio callback path must not allocate, block,
  lock, or do I/O (root `CONTRIBUTING.md` §3)
- Dependency-free core: `libkan` must not depend on anything else in the
  monorepo; future third-party deps must be MIT/BSD/Apache only (no GPL)
- Designed to be extracted into its own repository (`libkan` / `kan-dsp`) later

Spec: [docs/SPECIFICATION.md](../docs/SPECIFICATION.md) §2 ·
Gates: [docs/ROADMAP.md](../docs/ROADMAP.md) Milestone 1
