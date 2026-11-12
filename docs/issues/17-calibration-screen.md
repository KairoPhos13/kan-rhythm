# 3-step calibration screen (latency, noise floor, instrument detune)

Labels: `game`, `audio`
Size: M

## Context

kan! judges timing in milliseconds and pitch in cents, and it promises
that "a slightly flat or sharp instrument is not punished": the
instrument's own tuning is learnt and taken off
([game/README.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/README.md)).
All of that rests on calibration, and the release checklist in
[game/TODO.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/TODO.md)
specifies the screen: *"3-step calibration screen (acoustic latency,
noise floor, fixed-instrument detune → Δc applied to kanfx backing-track
pitch shift)"*, with the gate *"−35-cent detuned instrument matched in
< 2 s calibration, no artifacts, no score loss"*.

The measurement machinery already exists: `DetuneCalibrator` /
`TuningCompensation` in `libkan`'s `calibration.hpp`, room-silence noise
gate calibration, and measured numbers in
[docs/dsp/calibration.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/docs/dsp/calibration.md).
What is missing is the friendly in-game screen that runs the three steps
and gets a player through them without a manual.

## What to do

1. Implement a calibration screen (reachable from the first-run flow
   (`docs/issues/16`) and from settings) with three steps:
   1. **Acoustic latency**: play-along or clap/attack prompt; measure
      the input→system offset (use the existing harness semantics from
      [docs/dsp/latency-measurements.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/docs/dsp/latency-measurements.md);
      `tools/` `kancalibrate` may grow the reusable core);
   2. **Noise floor**: listen to room silence (with the mic live, no
      playing), drive the adaptive noise gate; show what it heard in
      plain words;
   3. **Instrument detune**: a 2-second reference note (A4 or a
      comfortable range note per instrument), compute Δc, and apply it:
      detector target offsets and the backing-track consonance pitch
      shift (kanfx, ±cents, BPM preserved).
2. Show results in player words ("your instrument plays about 12 cents
   flat; that is fine, kan! will follow you"), with Re-run / Skip.
3. Keep it forgiving: calibration is a service to the player, never a
   test they can fail. Absurd values should be questioned gently, not
   rejected.

## Acceptance criteria

- [x] All three steps runnable in-game with clear prompts and results
- [ ] Detune calibration meets the gate: a deliberately −35-cent
      instrument is matched in < 2 s, backing track follows with no
      audible artifacts and no score loss
- [ ] Noise-floor step visibly calibrates the gate (subsequent false
      triggers drop, see `docs/issues/08` for the formal gate)
- [x] Latency result is applied to judging offsets (and is re-runnable)
- [x] Every step skippable; results persisted and shown in settings
- [x] Screen reachable from first run and from settings

## Implementation (2026-11-12)

The screen is one step of the first-run guide and a screen of its own
(both `game/ruleset/FirstRun/`), from Settings → Rulesets → kan!
("Calibrate: microphone delay, room, tuning…"). No fork change.

1. **Acoustic latency**: reuses the editor's measurement unchanged
   (`MicCalibrator` + `MicCalibration.Estimate` behind
   `MicTimingOverlay`): four count-in clicks, eight notes, "Use it" saves
   the delay to `kan-forge/timing.json` (`ForgeTiming.MIC_DELAY_MS`),
   which every play takes off what it hears (`KanPlayState` →
   `ListeningTimeline`): the judging offsets, re-runnable at any time.
2. **Noise floor**: listens to the room for 3 s with the game's own
   pipeline and measures its level (`Kan.Core.Play.NoiseFloorMeasure`,
   median with the loud moments reported kindly). The result calibrates
   the note detector (`kan_forge_live_set_noise_floor` → libkan
   `NoteDescriber::set_noise_floor`), so the room's hum stops starting
   notes. Persisted (`ForgeTiming.SetCalibratedNoiseFloor`).
3. **Instrument detune**: a held reference note (the middle of the
   instrument's range, or A4) measured by `Kan.Core.Play.DetuneMeasure`
   (libkan's `DetuneCalibrator` semantics: median, 1.4826·MAD, guided or
   nearest-note). Δc is applied to the detector's targets
   (`ForgeTiming.TUNING_CENTS`, taken off everything heard before
   judging) and to the companion, which shifts by the same Δc with the
   tempo put back (BPM preserved) so instrument and song lock together
   (`KanPlayfield.followTuning`).

Measured offline (`game/core/tests/DetuneMeasureTest.cs`,
`NoiseFloorMeasureTest.cs`, `game/forge/tests/MicCalibrationTest.cs`, and
the native self-test's new noise-floor check): a −35-cent instrument is
matched from 1.5 s of readings (inside the 2-second gate), a door slam
does not raise the room's floor, and a floor above the sound stops
onsets on known input. The latency measure keeps its own suite.

Not verified here: the audible-artifact and score-loss halves of the
detune gate, and the false-trigger rate in a real room (issue 08), all of
which need a play-test. Until kanfx's consonance pitch shifter is wired,
the companion shift goes through the level track's own pitch/tempo pair
(the same mechanism the editor uses for track pitch). Dev hooks:
`KAN_DEV_SCREEN=calibrate` with `KAN_DEV_STEP=latency|noise|detune`;
in a developer run every measurement hears `KAN_LIVE_INPUT`, never the
microphone.

## Pointers

- `libkan/include/libkan/calibration.hpp`: `DetuneCalibrator`,
  `TuningCompensation`
- [docs/dsp/calibration.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/docs/dsp/calibration.md):
  measured calibration behaviour
- `apps/kan_demo --calibrate`: the existing 2 s reference-note flow
- `libkan` `precondition.hpp`: the adaptive noise gate the noise-floor
  step calibrates
- [game/TODO.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/TODO.md):
  the exact screen spec and gates (Game feel & calibration)

## Notes

- The consonance pitch shift is kan!'s "sound good first" feature
  (root README): the backing track locks to the player's tuning, not the
  other way round. Calibration is where that promise starts.
