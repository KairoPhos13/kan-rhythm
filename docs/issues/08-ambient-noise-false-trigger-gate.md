# Ambient-noise false-trigger gate: measure and close the < 1% gate

Labels: `libkan`, `audio`, `instrument-testing`
Size: M

## Context

kan! listens through a plain microphone in ordinary rooms. The release
gate in [game/TODO.md](https://github.com/KairoPhos/kan-rhythm/blob/main/game/TODO.md)
reads: *ambient-noise false-trigger test — < 1% false onsets at 45 dB SPL
from 50 cm*. A game that fires notes at you because of a fan or a passing
car is neither fun nor calm; the adaptive noise gate and high-pass added
in stage 0 (`precondition.hpp`) were built for this and are tested
offline, but the *gate number* has never been measured.

Per the repository rules, any detection change needs an offline test with
known input and expected output
([AGENTS.md](https://github.com/KairoPhos/kan-rhythm/blob/main/AGENTS.md)),
and the real-time path must stay allocation- and lock-free
([CONTRIBUTING.md §3](https://github.com/KairoPhos/kan-rhythm/blob/main/CONTRIBUTING.md)).

## What to do

1. Build a measurement procedure (document it in
   `docs/dsp/latency-measurements.md` or a new `docs/dsp/` note):
   - a controlled ambient source at ~45 dB SPL at 50 cm (room noise
     recording played back at a measured level is acceptable — say how
     you measured);
   - an instrument at the same distance for the positive control.
2. Record ambient-only WAVs from at least two rooms (quiet room, room
   with fan/PC noise) and run them through
   `apps/kan_cli --loopback <wav>` / `libkan`'s loopback test bench:
   count false onsets per minute and as a percentage of windows.
3. Tune the noise gate / onset thresholds **generally** (no per-room
   magic numbers; calibration must stay the mechanism) until the gate
   holds: **< 1% false triggers**, without losing real soft notes.
4. Add a regression fixture: a checked-in generated ambient scenario
   (deterministic `fixture_gen` style, like `libkan_loopback_test`) with
   an asserted false-onset count.

## Acceptance criteria

- [ ] Written procedure for producing the 45 dB SPL / 50 cm scenario
- [ ] False-trigger rate measured on ambient-only recordings from at
      least two rooms, numbers published in `docs/dsp/`
- [ ] Gate holds: < 1% false triggers in the tested scenarios
- [ ] Positive control: real soft (pp) notes still trigger (no
      over-gating)
- [ ] Offline regression test with known input and expected output added
      to the CTest suite

## Pointers

- `libkan/include/libkan/precondition.hpp` — 35 Hz HPF + adaptive noise
  gate (room-silence calibration)
- `libkan/tests/` — `libkan_loopback_test`, deterministic `fixture_gen`
- `apps/kan_cli` — `--loopback <wav>`, `--noise-cal <ms>`, `--no-gate`
- [docs/architecture/measurement-invariants.md](https://github.com/KairoPhos/kan-rhythm/blob/main/docs/architecture/measurement-invariants.md) —
  invariants a threshold change must not break (I1 onset jump rule)
- [docs/dsp/latency-measurements.md](https://github.com/KairoPhos/kan-rhythm/blob/main/docs/dsp/latency-measurements.md) —
  existing measurement style to follow

## Notes

- Real-time safety is non-negotiable: gate changes must not add
  allocation, locking or I/O to the audio callback path.
- If the < 1% number cannot be met on some hardware class (e.g. AGC
  laptop mics), report the boundary honestly with measurements.
