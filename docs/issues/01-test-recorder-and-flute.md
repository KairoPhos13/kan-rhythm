# Play-test kan! on recorder and flute (wind family)

Labels: `instrument-testing`, `help wanted`
Size: M

## Context

kan! is instrument-agnostic by design
([README.md — Core Philosophy](https://github.com/KairoPhos/kan-rhythm/blob/main/README.md)),
but so far it has been played mostly on one instrument (an alto ocarina):
see [game/README.md — Status](https://github.com/KairoPhos/kan-rhythm/blob/main/game/README.md).
Recorders and transverse flutes are the closest relatives that still differ
in ways that matter to detection: different breath-noise profiles, no
ocarina-style voicing, wide dynamic range, legato passages, and a high
register where onsets get soft. What feels wrong on them is exactly the
feedback the project needs before a public release.

Fixes must be **general**: instrument behaviour is data (attack ramp,
blanking window, range — measurement invariant I10 in
[docs/architecture/measurement-invariants.md](https://github.com/KairoPhos/kan-rhythm/blob/main/docs/architecture/measurement-invariants.md)),
never a recorder-only or ocarina-only special case in code.

## What to do

1. Build and run the game and the CLI tools:
   `make` / `make run` in `game/`
   ([game/README.md — Build, test, run](https://github.com/KairoPhos/kan-rhythm/blob/main/game/README.md)),
   and `apps/kan_demo` / `apps/kan_cli` for raw detection readouts.
2. On a **soprano recorder** and a **transverse flute** (any model):
   - free-tuner warm-up in `kan_demo` (presets `recorder`, `flute`);
   - play one easy level end to end in the game;
   - stress cases: legato lines, staccato, the high register, soft (pp)
     notes, breath attacks between notes.
3. Record what feels wrong: missed onsets, octave errors, pitch judged
   late, breath noise counted as notes, timing windows that feel unfair
   (defaults must stay forgiving — timing windows follow the music's
   pace, not its tempo), anything in the UI that assumes another
   instrument.
4. File each finding as its own issue (English, with a reproduction:
   instrument, note range, level, and what happened). If you can fix it,
   the fix belongs in instrument-profile data or shared detection code.

## Acceptance criteria

- [ ] At least one full level played on a recorder, and one on a flute
- [ ] Onset detection, pitch judgement and timing feel reported for both,
      including the high register and soft notes
- [ ] Breath noise / wind-attack behaviour documented (does it false-
      trigger? where?)
- [ ] Every finding filed as a separate issue with a reproduction
- [ ] Any proposed fix is general (profile data or shared code) — no
      instrument-name conditionals

## Pointers

- `apps/kan_demo` — live pipeline with instrument presets
  (`ocarina`, `flute`, `recorder`, `guitar`, `piano`)
- `apps/kan_cli` — `--profile plucked|wind|keyboard`, `--detector yin|bacf`
- `libkan/include/libkan/instrument_profile.hpp` — attack ramp, blanking
  window (8 ms plucked / 15 ms wind-bowed), expected frequency range
- [docs/dsp/range-coverage.md](https://github.com/KairoPhos/kan-rhythm/blob/main/docs/dsp/range-coverage.md) —
  measured coverage across the full range
- [game/docs/gameplay.md](https://github.com/KairoPhos/kan-rhythm/blob/main/game/docs/gameplay.md) —
  how a play is judged (what "feels wrong" maps to)

## Notes

- Report in English ([CONTRIBUTING.md](https://github.com/KairoPhos/kan-rhythm/blob/main/CONTRIBUTING.md)),
  but write your best effort — reviewers help with wording, never penalize.
- kan! is calm by design: when judging feedback feels punishing on a real
  instrument, that is a bug worth reporting even without numbers.
