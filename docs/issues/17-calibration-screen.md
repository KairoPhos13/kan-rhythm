# 3-step calibration screen (latency, noise floor, instrument detune)

Labels: `game`, `audio`
Size: M

## Context

kan! judges timing in milliseconds and pitch in cents, and it promises
that "a slightly flat or sharp instrument is not punished" — the
instrument's own tuning is learnt and taken off
([game/README.md](https://github.com/KairoPhos/kan-rhythm/blob/main/game/README.md)).
All of that rests on calibration, and the release checklist in
[game/TODO.md](https://github.com/KairoPhos/kan-rhythm/blob/main/game/TODO.md)
specifies the screen: *"3-step calibration screen (acoustic latency,
noise floor, fixed-instrument detune → Δc applied to kanfx backing-track
pitch shift)"*, with the gate *"−35-cent detuned instrument matched in
< 2 s calibration, no artifacts, no score loss"*.

The measurement machinery already exists — `DetuneCalibrator` /
`TuningCompensation` in `libkan`'s `calibration.hpp`, room-silence noise
gate calibration, and measured numbers in
[docs/dsp/calibration.md](https://github.com/KairoPhos/kan-rhythm/blob/main/docs/dsp/calibration.md).
What is missing is the friendly in-game screen that runs the three steps
and gets a player through them without a manual.

## What to do

1. Implement a calibration screen (reachable from the first-run flow —
   `docs/issues/16` — and from settings) with three steps:
   1. **Acoustic latency** — play-along or clap/attack prompt; measure
      the input→system offset (use the existing harness semantics from
      [docs/dsp/latency-measurements.md](https://github.com/KairoPhos/kan-rhythm/blob/main/docs/dsp/latency-measurements.md);
      `tools/` `kancalibrate` may grow the reusable core);
   2. **Noise floor** — listen to room silence (with the mic live, no
      playing), drive the adaptive noise gate; show what it heard in
      plain words;
   3. **Instrument detune** — a 2-second reference note (A4 or a
      comfortable range note per instrument), compute Δc, and apply it:
      detector target offsets and the backing-track consonance pitch
      shift (kanfx, ±cents, BPM preserved).
2. Show results in player words ("your instrument plays about 12 cents
   flat — that is fine, kan! will follow you"), with Re-run / Skip.
3. Keep it forgiving: calibration is a service to the player, never a
   test they can fail. Absurd values should be questioned gently, not
   rejected.

## Acceptance criteria

- [ ] All three steps runnable in-game with clear prompts and results
- [ ] Detune calibration meets the gate: a deliberately −35-cent
      instrument is matched in < 2 s, backing track follows with no
      audible artifacts and no score loss
- [ ] Noise-floor step visibly calibrates the gate (subsequent false
      triggers drop — see `docs/issues/08` for the formal gate)
- [ ] Latency result is applied to judging offsets (and is re-runnable)
- [ ] Every step skippable; results persisted and shown in settings
- [ ] Screen reachable from first run and from settings

## Pointers

- `libkan/include/libkan/calibration.hpp` — `DetuneCalibrator`,
  `TuningCompensation`
- [docs/dsp/calibration.md](https://github.com/KairoPhos/kan-rhythm/blob/main/docs/dsp/calibration.md) —
  measured calibration behaviour
- `apps/kan_demo --calibrate` — the existing 2 s reference-note flow
- `libkan` `precondition.hpp` — the adaptive noise gate the noise-floor
  step calibrates
- [game/TODO.md](https://github.com/KairoPhos/kan-rhythm/blob/main/game/TODO.md) —
  the exact screen spec and gates (Game feel & calibration)

## Notes

- The consonance pitch shift is kan!'s "sound good first" feature
  (root README): the backing track locks to the player's tuning, not the
  other way round. Calibration is where that promise starts.
