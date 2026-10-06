# Technique validation — how the game knows a trick was done right

Status: Milestone 1 item 6 implemented (platform-architecture.md §5).
Reproduce: `ctest --test-dir build -R technique`. API:
`libkan/include/libkan/technique.hpp` (dependency-free).

## The contract

libkan **measures**, the instrument pack **supplies expectations**, the game
**judges**. Expectations are plain data (`*Spec` structs — same rule as
`InstrumentProfile`: no format coupling; charts/spec serializes them later).

| family | examples | measurement | spec |
|---|---|---|---|
| Pitch gestures | bend, vibrato (yuri…), slide | `VibratoAnalyzer`, `BendAnalyzer` on a `TrajectoryBuffer` of the per-block cents stream | `VibratoSpec`, `BendSpec` |
| Articulation | staccato, legato, accent, mute | `EnvelopeAnalyzer` on the RMS stream | `EnvelopeSpec` |
| Extended | string harmonics, flutter-tongue, pedal | `SpectralSignatureValidator` (slot amplitudes vs template) | `SpectralSignature` |

Every check returns `TechniqueResult {ok, score, error}` → feeds the
300/100/50 style judgment + "technique correct/incorrect" feedback.

## The user-facing example: Japanese vibrato (*yuri*-style)

A chart says "wide slow vibrato here". The pack encodes e.g.
`VibratoSpec {rate 2–6 Hz, depth ≥ 25 cents, duration ≥ 150 ms}`; a
classical passage instead asks `5.5–7.5 Hz, 15–45 cents`; a straight-tone
passage sets `expect_straight = true`. Measured (offline, `technique_test.cpp`):

| execution | readout | yuri spec | classical spec | straight spec |
|---|---|---|---|---|
| 4 Hz, ±40 c, 800 ms | rate 4.0 Hz, depth 39.6 c | **ok** (score 0.84) | rejected | rejected |
| straight tone | not periodic | — | — | **ok** |
| 200 c oscillation | depth 200 c | rejected (trill, not vibrato) | — | — |

Integration: a true FM vibrato read through the YIN pipeline gives
rate **4.99 Hz** (true 5.0) — the game can tell "vibrato performed at the
right speed" from the live stream.

## Bend / slide

`BendAnalyzer` measures endpoints, duration, monotonicity and shape-fit
error against Linear (slide) or EaseOut (string bend) curves. Measured:
linear 0→200 c/250 ms fits its spec at score 1.00; an ease-out bend passes
the ease-out spec and fails a strict linear spec; a wobbling "bend" fails
monotonicity.

## Articulation

`EnvelopeAnalyzer` measures duration, attack, release, sustain ratio and
the peak→−20 dB decay. Measured: legato (sustain 0.94) passes only legato;
staccato (sustain 0.16, release 45 ms) passes only staccato; mute (−20 dB in
70 ms) passes the mute spec.

## Dynamics tiers (pp..ff)

`DynamicsClassifier` turns one attack peak per note into a tier:
**session-relative** by default (the tier ends track the observed level
range and converge as notes accumulate; the first `min_notes` notes report
`warming-up`, never a guess) or **calibrated** (the player marks one soft
and one loud note, fixing the range ends). Measured
(`libkan_technique_test`, soft vs loud synthesized passage): calibration →
soft = `pp`, geometric mid = `mf`, loud = `ff`; session mode → first 8
notes `warming-up`, then every soft note ranks below every loud note with
the extremes landing on `pp` / `ff`. The tier is emitted as the description
stream's EXPRESSION fact (`dyn_tier`) alongside the raw level in dBFS.

## Notes & limits

- Vibrato rate needs **≥3 oscillation cycles** (rate physics): a 2 Hz
  vibrato needs 1.5 s before the game can confirm it. Faster vibratos
  confirm sooner; judgment windows should account for this.
- Trajectory feeds come from the harmonic path (YIN window ~21 ms attenuates
  measured depth somewhat — rate, the primary discriminator, is robust;
  BACF per-sample readings will sharpen depth when the confirmation path
  exposes its stream).
- Spectral signatures are slot templates (Goertzel-style magnitudes):
  string-harmonic template similarity 1.000 vs 0.558 for a normal pluck —
  clean separation at threshold 0.80.
