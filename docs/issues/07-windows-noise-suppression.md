# Windows noise suppression and audio enhancements vs kan!'s input

Labels: `audio`, `platform:windows`, `instrument-testing`
Size: S

## Context

Windows applies optional "audio enhancements" / noise suppression to
microphone input (device settings, and per-app processing in Windows 10
and 11). Those DSPs are tuned for speech: they suppress breath and
background hiss, which also removes the air of a flute, the bow noise of
a violin and the soft attack of a recorder — precisely the evidence
kan!'s onset and pitch detection depend on. kan!'s capture path is
miniaudio through [libkan](https://github.com/KairoPhos/kan-rhythm/blob/main/libkan/README.md)
and is unaffected by the BASS question; what is affected is whatever the
OS does to the signal before it reaches us.

Before the release we need to know the actual failure mode on Windows, so
the game can tell players what to switch off instead of silently judging
a processed signal.

## What to do

1. On a Windows 10 or 11 machine with a real instrument:
   - play with the microphone's "audio enhancements"/noise suppression
     **on** (the OS default on many devices), then **off**;
   - for each state: free-tuner readouts in `apps/kan_demo`, then one
     easy level in the game;
   - also try Windows' "voice activity"/exclusive-mode options if the
     device driver offers them.
2. Compare: onset misses, pitch wobble, soft-note detection, latency
   perception. If you can, capture short WAVs of the same passage in both
   states and run them through `kan_cli --loopback <wav>` for an offline
   comparison.
3. Write up the findings (device, driver, exact Windows setting path) as
   an issue. If the fix is guidance (e.g. a first-run hint "turn
   enhancements off for your instrument mic"), propose the wording —
   calm and factual, no blame.

## Acceptance criteria

- [ ] Same passage played with Windows enhancements on and off, results
      compared
- [ ] Behaviour differences documented (onsets, pitch, soft notes)
- [ ] The exact Windows setting path documented for each device tested
- [ ] A concrete proposal for how kan! should surface this to players
      (hint, calibration warning, or documentation entry)

## Pointers

- `apps/kan_demo`, `apps/kan_cli --loopback <wav>` — offline comparison
  of captured WAVs
- [docs/dsp/latency-measurements.md](https://github.com/KairoPhos/kan-rhythm/blob/main/docs/dsp/latency-measurements.md) —
  measurement procedure
- [game/docs/environment.md](https://github.com/KairoPhos/kan-rhythm/blob/main/game/docs/environment.md) —
  platform notes (record results here or link the issue from there)
- [game/TODO.md](https://github.com/KairoPhos/kan-rhythm/blob/main/game/TODO.md) —
  "other microphones … Windows' noise suppression"

## Notes

- Keep the fix general: guidance or preprocessing behaviour that helps
  every instrument, not an ocarina or Windows-brand special case.
- Windows-only driver quirks are fine to document in
  `game/docs/environment.md`.
