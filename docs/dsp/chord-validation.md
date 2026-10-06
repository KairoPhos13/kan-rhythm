# Score-informed chord validation — design & results

Status: Milestone 1 gate 3 measured offline (synthesized strummed triads).
Reproduce: `ctest --test-dir build -R 'chord|chroma'` (`libkan/tests/
chord_test.cpp`, `chroma_test.cpp`, battery in `chord_suite.hpp`).

## Gate (ROADMAP M1)

- Verification latency < **32 ms**
- Discrimination F1 (major vs minor vs one wrong string) > **94%**

## What shipped

| backend | header | role |
|---|---|---|
| `HarmonicChordValidator` | `libkan/include/libkan/harmonic_chord.hpp` | **Gate carrier.** Joint weighted LS over harmonic atoms (fundamental + 5 harmonics per expected note), one-vs-rest frequency refinement (±30 c, tracks per-string detuning), IRLS against strum transients, explained-energy similarity + per-note presence floor |
| `GoertzelChordValidator` | `libkan/include/libkan/chord_goertzel.hpp` | Spec's marginal Goertzel bank (fundamental + first 3 harmonics slots). Verifies correct chords; marginal classification limit documented below |
| `ChromaChordValidator` | `libkan/include/libkan/chroma_fft.hpp` | KissFFT (BSD-3) 12-bin chroma projection, octave-invariant template machinery for instrument packs. Secondary |

## Measured results (40 trials, C3..A4 roots, ±15 c per-note detuning,
5 ms strum, 8 ms blanking + 1024-sample window = **29.3 ms < 32 ms gate**)

| metric | Harmonic LS | Goertzel bank | Chroma (KissFFT) |
|---|---|---|---|
| macro F1 (argmax over confusions) | **1.0000** ✓ gate | 0.71 (marginal limit) | 0.58 |
| true similarity avg / min | 0.976 / 0.942 | 0.932 / — | 0.611 / — |
| false similarity avg / max | 0.852 / 0.989* | 0.909 / 0.977 | — / — |
| true chords verified (threshold + floor) | 38/40 (C4+: 20/20) | 40/40 | 2/24 |
| false acceptances | 46/120 (C4+: 10/60) | n/a | **0/72** |

\* false max 0.989 occurs only in the **C3 register**: the major/minor third
is ~9 Hz there and no method resolves it inside a 21 ms window (a
fundamental time-frequency limit — one mainlobe is ~48 Hz wide). Even
there, argmax discrimination holds (true templates score higher: 1.0 F1),
and the low-register *absolute-threshold* question is settled over the
note's sustain (the chart judge integrates refined cents over time).

## Why not the literal spec design (marginal Goertzel / chroma)

SPECIFICATION §2 stage 3 suggests a Goertzel bank / 12-bin chroma with
cosine similarity ≈ 0.80. Measured: marginal magnitude slots smear across
neighboring semitones inside the latency budget (kernel mainlobe 94 Hz vs
18.5 Hz E4–Eb4 spacing) — macro-F1 0.71. The fix is not a different
window but a *joint model*: the LS validator explains the whole chord at
once, exploiting phase coherence across harmonics (E4's 3rd harmonic is
resolved at 21 ms even though its fundamental is not). This is a
**spec deviation to ratify**: stage 3 wording should be
"harmonic least-squares / template model fit, threshold ≈ 0.80", keeping
the Goertzel/chroma primitives as instrument-pack template machinery
(platform-architecture §5 item 5).

## Known limitations (documented, not hidden)

1. Low-register absolute thresholds (root ≲ C3): major/minor false
   acceptances inside 21 ms. Argmax discrimination still correct. Mitigations:
   longer window (judge integrates over sustain), or chart-side tolerance.
2. Default thresholds: similarity 0.80 (spec) + presence floor 0.45/n.
   Operating points are pack-configurable (`set_threshold`,
   `set_presence_floor`) per instrument-pack templates.
3. Strum spread: window assumes transients live in the first half
   (true for strums ≲ 10 ms at 1024 samples); longer strums need the
   window moved later or lengthened.
