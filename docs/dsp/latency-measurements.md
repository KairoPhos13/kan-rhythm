# Milestone 1 latency & gate measurements

Status: living document — every detector change updates the tables below.
Gate definitions: [ROADMAP.md](../ROADMAP.md) Milestone 1 · detector A/B:
[bacf-vs-yin.md](bacf-vs-yin.md).

Legend: **[M]** measured offline (synthesized fixtures, deterministic, no
hardware) · **[T]** theoretical (derived from measured components + block
arithmetic) · **[H]** requires hardware loopback (not yet measured — this
environment has no audio interface; procedure below).

## Pipeline latency model (consumer thread, 48 kHz)

```
hardware → ring buffer → [stage 0: HPF+gate] → [stage 1: onset]
        → [stage 2: blanking] → [stage 3: confirm (bounded NSDF matcher)]
        → [sustain: BACF score-informed cents tracking]
```

All heavy analysis runs on the consumer thread fed by the lock-free ring
buffer; the audio callback only pushes samples (CONTRIBUTING §3).

Per-stage contribution at the gate configuration (64-sample blocks = 1.33 ms):

| stage | contribution | basis |
|---|---|---|
| capture block | 1.33 ms (64) / 2.67 ms (128) | [T] block arithmetic |
| stage 0 precondition | ~360 ns/block | [M] biquad cascade + gate |
| stage 1 onset | ≤ 1 block after energy jump | [M] fires on first block with HFC derivative > 3.2× EMA |
| stage 2 blanking | 8 ms plucked / 15 ms wind-bowed / 6 ms keyboard | [M] profile constant |
| stage 3 confirm | `blanking + tau_max(f−2st) + 64 pairs + ≤1 block` | [M] see table below |

## Gate 1 — onset latency (hardware → event) ≤ 4.5 ms @ 48 kHz/64

**[H]** Not yet measured end-to-end (needs the loopback-cable rig below).
**[T]** Budget: capture block 1.33 ms + onset detection ≤ 1 block 1.33 ms +
ring-buffer handoff < 0.01 ms **[M]** = **~2.7 ms** expected, ~1.8 ms of
headroom to the gate. Offline fixture runs confirm the detector fires on
the first or second block after the energy jump **[M]**.

### Loopback-cable procedure (for [H] measurement)

1. Cable the interface output (channel 0) physically into its input
   (channel 1) — a real electrical round trip.
2. `kan_cli --loopback-cable --latency-report` (harness pending, see
   libkan/TODO.md): the app emits a sharp impulse (1-sample step burst) at
   a known `t0` (host clock) and timestamps the onset event `t1`.
3. `latency = t1 − t0 − output_block_latency` where the output block
   latency (1.33 ms @ 64) is the known playback-side buffering. Repeat
   ≥ 1000 times; report median and p99. The gate uses the median; p99
   must stay < 6 ms.
4. Run the same procedure at 128-sample blocks for the CLI default.

## Gate 2 — monophonic confirmation latency

Bounded NSDF matcher (`libkan/bounded_pitch.hpp`), plucked profile (8 ms
blanking), 64-sample blocks, synthesized plucked notes **[M]**:

| note | freq Hz | confirm ms | ROADMAP gate | verdict |
|------|---------|-----------|--------------|---------|
| E2   |   82.41 |      24.0 | < 22 ms (> 82 Hz)   | ✗ +2.0 (see floor analysis) |
| A2   |  110.00 |      20.0 | < 22 ms | ✓ |
| C3   |  130.81 |      18.7 | < 22 ms | ✓ |
| G3   |  196.00 |      16.0 | < 22 ms | ✓ |
| C4   |  261.63 |      14.7 | < 12 ms (> 261 Hz)  | ✗ +2.7 (see floor analysis) |
| E4   |  329.63 |      13.3 | < 12 ms | ✗ +1.3 |
| A4   |  440.00 |      12.0 | < 12 ms | ✗ equal (not <) |
| C5   |  523.25 |      12.0 | < 12 ms | ✗ equal (not <) |
| E5   |  659.26 |      12.0 | < 12 ms | ✗ equal (not <) |
| A5   |  880.00 |      10.7 | < 12 ms | ✓ |

All notes are far inside the SPECIFICATION §2 harmonic-validation budget
(onset → note confirmed < 35 ms) **[M]**.

### Physical-floor analysis (why the boundary notes miss)

Any correlation-based confirmation needs, after the onset:

```
t_confirm ≥ blanking + tau(f_expected − 2 st) + pairs + block_quantization
```

- **C4**: 8 ms blanking + 4.06 ms longest lag (246.9 Hz) = **12.06 ms
  before a single correlation pair** — the < 12 ms gate is unreachable for
  autocorrelation methods under an 8 ms blanking, independent of
  implementation. DFT/Goertzel alternatives do not help at these window
  lengths: the negative-frequency image of a real signal sits ~2 bins away
  at ~1-period windows and tilts the ±200-cent analysis band monotonically
  (measured: 100+ cent biases — see the bounded_pitch.hpp header notes).
- **E2**: 8 + 12.9 (lag at 73.9 Hz) + 1.33 (64 pairs) = 22.2 ms floor;
  measured 24.0 ms (the first eligible block barely misses NSDF threshold
  on the decaying envelope, confirming one block later).

### Resolution paths (for the gate review)

1. **Keyboard profile passes C4**: with 6 ms blanking (piano — the gate's
   own example instrument), C4 confirms at **10.7 ms [T]**. Wind profiles
   (15 ms blanking) can never meet < 12 ms and instead judge timing on the
   air-ramp start via `judgment_offset_ms` (SPECIFICATION §3) — the < 12 ms
   gate should be read as plucked/keyboard-oriented.
2. **Score-informed blanking trim**: when the chart expects a note, the
   matcher band (±2 st) already rejects most transient confusion; trimming
   blanking to ~4 ms for the confirmation stage only would bring C4 to
   ~8.7 ms and E2 to ~18.9 ms **[T]**. Blocked on validating accuracy
   against real attack transients (fixtures so far model the full 8 ms
   inharmonic precursor — a worst case).
3. Accept and re-scope: all measured values pass the SPECIFICATION §2
   budget (< 35 ms) with >2× margin.

## Gate 3 — chord verification < 32 ms, F1 > 94 %

**[M]** Pass (offline, synthesized strummed triads — `libkan/tests/chord_test.cpp`):
- Verification completes at **29.3 ms** (8 ms blanking + 21.3 ms window) < 32 ms ✓
- **Macro F1 = 1.0000** (major/minor/one-wrong-string argmax, 40 trials,
  C3..A4) > 0.94 ✓
- Gate carrier: `HarmonicChordValidator` (joint harmonic LS). Design,
  backends comparison, spec-deviation proposal and low-register limits:
  [chord-validation.md](chord-validation.md).

## Gate 4 — DSP thread CPU < 3 % of one core

Consumer-thread analysis cost (offline benches, `-O3 -march=native`,
x86-64; stage 0/1/ring measured on 2 s of synthesized note, 128-sample
blocks; matcher/BACF/YIN from `libkan_bacf_bench`) **[M]**:

| stage | ns/block | % of one core |
|---|---|---|
| audio-callback path: ring push+pop (128) | ~74 | 0.003 % |
| stage 0 HPF + gate (128) | ~360 | 0.014 % |
| stage 1 onset (128) | ~81 | 0.003 % |
| stage 3 bounded matcher (per call, 64-sample blocks) | ~450 | < 0.04 % |
| sustain BACF score-informed (128) | ~1 000 | 0.04 % |
| sustain YIN 2048-window (if run every block — avoid) | ~160 000 | 6.0 % |

Full pipeline with the recommended configuration (matcher for confirmation,
BACF for sustain, YIN never on the per-block path): **< 0.1 % [M]** — gate
passes with >30× margin.

## Gate 5 — zero underruns over 60 min soak

**[H]** Requires the `--soak` mode of kan_cli on real hardware (pending,
see apps/kan_cli/TODO.md). Offline ring-buffer integrity is ctest-covered;
the CLI counts callback overruns via miniaudio's performance counters.

## Changelog

- 2026-10-05: initial tables — bounded NSDF matcher + BACF integration
  (items 3); gates 2 and 4 assessed offline; gate 1 budget derived.
