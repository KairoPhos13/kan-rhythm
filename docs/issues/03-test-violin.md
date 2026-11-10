# Play-test kan! on violin (bowed-string family)

Labels: `instrument-testing`, `help wanted`
Size: M

## Context

kan! has been played mostly on an alto ocarina
([game/README.md: Status](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/README.md)).
Bowed strings are a whole family kan! has never been tried on: attack is
a bow-stick friction ramp rather than an air or pluck transient, pitch
slides into place (portamento), intonation is continuous with no frets,
bow direction changes and string crossings create non-note events, and
the player's own tuning drifts. The pipeline has pieces aimed at exactly
this (fretless relative-tuning mode, transient blanking profiles for
wind/bowed attacks, technique validators), but none of it is validated on
a real violin.

Report from real play matters more than numbers here: kan!'s promise is
that practice is fun, and nothing kills fun faster than a judge that
punishes a violinist for playing like a violinist.

## What to do

1. Build and run the game (`make run` in `game/`) and `apps/kan_demo`.
2. On a violin (viola/cello also welcome, say which):
   - play one easy level end to end;
   - stress cases: slow bow attacks, fast detaché, slurred legato
     (one bow, several notes), string crossings, double stops (are they
     handled gracefully?), vibrato, slides into notes, soft pp entries.
3. Check calibration: the instrument's own detuning should be learnt and
   taken off, and fretless drift should follow the relative-tuning mode
   rather than being judged as wrong notes.
4. Report findings as separate issues (instrument, bowing, passage,
   what happened). Check that the notes on screen make sense for strings
   (fingerings/range hints are future work, but nothing should assume
   keys or holes).

## Acceptance criteria

- [ ] At least one full level played end to end on a bowed instrument
- [ ] Attack/onset behaviour for slow vs fast bow strokes documented
- [ ] Legato (slurred) passages and string crossings documented
- [ ] Detune calibration and relative-tuning behaviour reported
- [ ] Double stops / chords handled without nonsense output (documented)
- [ ] Findings filed as separate issues with reproductions

## Pointers

- `apps/kan_demo`: `--profile` presets, free-tuner readout
- `libkan/include/libkan/instrument_profile.hpp`: 15 ms blanking for
  wind/bowed attacks
- `libkan` `RelativeTuningTracker`: interval evaluation vs a moving
  tonal centre (fretless instruments)
- [docs/dsp/technique-validation.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/docs/dsp/technique-validation.md):
  bend/vibrato/slide trajectory checks
- [game/docs/instrument-modes.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/docs/instrument-modes.md):
  where instruments live in the client

## Notes

- Fixes must be general (profile data / shared code), never
  violin-specific branches; the project is instrument-agnostic.
- "Feels unfair" is a valid bug report even without measurements.
