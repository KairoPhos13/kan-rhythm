# BACF (Cycfi Q) vs YIN: offline A/B benchmark

Status: measured offline on synthesized signals (no microphone), Milestone 1.
Reproduce: `ctest --test-dir build -R bacf_bench` (or run
`./build/libkan/libkan_bacf_bench`), sources in `libkan/tests/bacf_bench.cpp`.
Gate definitions: [ROADMAP.md](../ROADMAP.md) Milestone 1 · latency analysis:
[latency-measurements.md](latency-measurements.md).

## Setup

- Suite: 10 notes, E2 (82.41 Hz) … A5 (880 Hz), plucked-string model
  (8 ms pick transient + 4 decaying harmonics), detunings −20…+15 cents,
  48 kHz, 128-sample blocks (the bounded matcher runs at the gate's
  64-sample granularity).
- Detectors compared:
  - **YIN score-informed**: `HybridDetector::detect_pitch` on a rolling
    2048-sample window, search bounded to ±2 semitones of the expected note.
  - **YIN SI adaptive window**: same, window shrunk to ~2.5 periods of the
    expected note.
  - **BACF score-informed**: `BacfDetector` (Cycfi Q v1.5.0 `pitch_detector`,
    MIT) constructed with the ±2-semitone band as its lowest/highest
    frequency; 2-frame confirmation.
  - **BACF blind**: same over the full 60-1200 Hz range (no chart input).
  - **Bounded matched filter**, `BoundedPitchMatcher` (`bounded_pitch.hpp`):
    NSDF over the bounded lag band on the clean post-blanking window,
    parabolic sub-sample interpolation. This is libkan's own fast
    confirmation stage; included because it beats both libraries on
    confirmation latency.
- Metrics: confirmation latency (note start → first valid result), error in
  cents vs the synthesized ground truth, and consumer-thread CPU cost per
  128-sample block (2.67 ms of audio).

## Results (GCC 16, `-O3 -march=native`, x86-64)

| detector               | conf. | avg ms | max ms | avg abs err (c) | max abs err (c) | ns/block |
|------------------------|-------|--------|--------|-----------------|-----------------|----------|
| YIN score-informed     | 10/10 |   42.1 |   48.0 |             6.9 |            14.9 |   ~160000 |
| YIN SI adapt window    | 10/10 |   22.7 |   34.7 |             5.0 |            37.0 |    ~92000 |
| BACF score-informed    | 10/10 |   28.0 |   56.0 |             1.7 |            17.0 |     ~1200 |
| BACF blind             | 10/10 |   59.5 |   69.3 |             1.7 |            17.0 |     ~1070 |
| Bounded matched (SI)   | 10/10 |   15.3 |   24.0 |             1.8 |            15.1 |      ~450 |

Per-note confirmation latency (ms) and bounded-matcher error (cents):

| note | freq Hz | YIN 2048 | YIN adapt | BACF SI | BACF blind | bounded | bounded err (c) |
|------|---------|----------|-----------|---------|------------|---------|-----------------|
| E2   |   82.41 |     45.3 |      34.7 |    56.0 |       66.7 |    24.0 |           −15.1 |
| A2   |  110.00 |     45.3 |      29.3 |    42.7 |       69.3 |    20.0 |            +0.5 |
| C3   |  130.81 |     45.3 |      29.3 |    34.7 |       66.7 |    18.7 |            +0.4 |
| G3   |  196.00 |     48.0 |      21.3 |    32.0 |       69.3 |    16.0 |            +0.1 |
| C4   |  261.63 |     42.7 |      18.7 |    26.7 |       66.7 |    14.7 |            +0.8 |
| E4   |  329.63 |     42.7 |      18.7 |    21.3 |       50.7 |    13.3 |            −0.1 |
| A4   |  440.00 |     40.0 |      18.7 |    18.7 |       53.3 |    12.0 |            −0.2 |
| C5   |  523.25 |     37.3 |      18.7 |    18.7 |       50.7 |    12.0 |            +0.2 |
| E5   |  659.26 |     37.3 |      18.7 |    16.0 |       50.7 |    12.0 |            −0.1 |
| A5   |  880.00 |     37.3 |      18.7 |    13.3 |       50.7 |    10.7 |            −0.8 |

## Analysis

- **Accuracy:** BACF is tuner-grade (≈0 cents on 9/10 notes; −17 cents on
  A5 where its internal median filter straddles the short decay tail).
  Score-informed YIN averages ~7 cents. The bounded matcher is sub-cent
  from A2 up; its only weak spot is E2 (−15 cents), where the correlation
  overlap is a small fraction of the long period.
- **Latency:** YIN with a fixed 2048-window is window-bound (~37-48 ms
  regardless of note). BACF's internal analysis window scales with the
  lowest frequency of its search range, so score-informed BACF confirms
  1.5-2× faster than blind BACF but still needs ~2 periods plus its
  median-filter settling. The bounded matcher confirms from
  `blanking + longest lag + 64 pairs` of clean audio, the fastest of the
  three on every note.
- **CPU:** BACF and the bounded matcher cost ~1 µs per 2.67 ms block
  (<0.05 % of a core each). YIN on a 2048 window costs ~90-160 µs per call
  (up to ~6 % of a core if called every block; the ROADMAP's <3 % DSP CPU
  gate requires keeping full-window YIN off the per-block path).

## Decision (Milestone 1)

- **Fast confirmation stage:** `BoundedPitchMatcher`, lowest latency,
  lowest CPU, confirmation-grade cents.
- **Intonation/sustain tracking:** **BACF score-informed**, best cents
  accuracy at negligible CPU; `set_expected_note()` per chart event.
- **Blind fallback (no chart context, calibration flows):** YIN on the
  rolling window (`HybridDetector::detect_pitch`), which is also the
  dependency-free reference implementation.
- Cycfi Q stays a vendored dependency (`libkan_bacf` CMake target,
  `KAN_ENABLE_BACF=ON` by default); core `libkan` headers remain
  dependency-free.

## Caveats

- Synthesized signals only; real instrument attacks (longer inharmonic
  precursors, room reverb) will push confirmation latencies up; track via
  the loopback harness (`--latency-report`) and re-run this bench whenever
  the detector changes.
- BACF numbers use Cycfi Q v1.5.0 with −45 dB hysteresis, 2-frame
  confirmation, and q's internal median filter.
