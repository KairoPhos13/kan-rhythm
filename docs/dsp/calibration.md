# Tuning compensation: calibration layer

Status: Milestone 1 item 5 implemented (SPECIFICATION §2 "Dynamic consonance
module", detector side). Reproduce: `ctest --test-dir build -R calibration`.
API: `libkan/include/libkan/calibration.hpp` (dependency-free).

## What shipped

| piece | class | spec role |
|---|---|---|
| 2 s reference-note calibrator | `DetuneCalibrator` | "play any long comfortable note" → Δc = 1200·log2(f_measured / f_standard) (guided or nearest equal-tempered note; median + 1.4826·MAD robustness) |
| Compensation matrix | `TuningCompensation` | global Δc + per-string Δc1..Δc6 (3 s guided strum); `target_frequency()` / `target_midi()` shift detector targets, `residual_cents()` for judgment |
| Relative tuning | `RelativeTuningTracker` | fretless/bowed/voice: intervals judged against an EMA tonal center; only harmony-breaking deviations are punished (wrong notes cannot drag the center) |

## Measured (offline, `libkan/tests/calibration_test.cpp`)

- A4 +12 c reference note (synthesized, YIN readings): **Δc = +12.05 c**
  (error 0.05 c), unguided flow infers the same note.
- Detector-target shift: `target_frequency(440, +12 c) = 443.06 Hz`; the
  bounded matcher confirms the detuned note with **+0.01 c residual** (vs
  +12.0 c raw against fixed A440, the exact penalty the compensation
  removes while scoring stays strict).
- Per-string matrix: Δc1..Δc6 recovered exactly, `residual_cents()` ≈ 0 on
  compensated targets.
- Relative tuning: +10 c consistent player → center converges to +10.0 c,
  all intervals pass; a +60 c-above-center note is flagged and does not
  move the center; interval steps reported (ΔΔ = +15 c measured).

## Consumers

- `apps/kan_demo --calibrate`: prints Δc and **applies** it; the tuner then
  shows residual vs the instrument's own tuning; `--expect` targets shift by
  the same Δc.
- `game/` judgment (M2): `residual_cents()` is the intonation axis input.
- `kanfx/` backing-track pitch shifter (item: Signalsmith Stretch): must
  apply the same `correction_cents()` so instrument and song lock together.
  Not wired here; libkan stays dependency-light; the shifter lives in
  kanfx's input/backing chain.

## Notes

- Strings beyond the matrix size fall back to global; `string_count = 0`
  means global-only compensation.
- The calibrator collects into fixed storage (kMaxReadings = 512) and never
  allocates; safe on the consumer thread between notes.
