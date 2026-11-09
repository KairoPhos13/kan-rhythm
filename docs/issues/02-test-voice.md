# Play-test kan! with the singing voice (voice family)

Labels: `instrument-testing`, `help wanted`
Size: M

## Context

kan! judges notes from a plain microphone and promises "any instrument
that plays notes" ([README.md](https://github.com/KairoPhos/kan-rhythm/blob/main/README.md),
[game/README.md](https://github.com/KairoPhos/kan-rhythm/blob/main/game/README.md)).
The voice is the instrument everyone already owns, and it is also the
hardest case for the pipeline: vibrato that a pitch tracker may follow or
smear, consonants that look like onsets but carry no pitch, breath and
room noise, glides between notes, and pitch that drifts with effort and
volume. kan! has never been play-tested with singing.

The description stream already models some of this — vibrato, expression
facts, "unresolved" pitch states
([docs/architecture/note-description-stream.md](https://github.com/KairoPhos/kan-rhythm/blob/main/docs/architecture/note-description-stream.md))
— but only play-testing shows whether it feels fair.

## What to do

1. Build and run the game (`make run` in `game/`) and the raw tools
   (`apps/kan_demo`, `apps/kan_analyze`).
2. Sing at least one easy level end to end (humming or syllables — try
   both), and check in `kan_demo`'s free-tuner mode:
   - onset detection on consonant vs hummed attacks;
   - pitch stability with and without vibrato;
   - legato lines vs separated notes;
   - soft singing (pp) and loud singing (ff).
3. Note how the judge treats glides, scoops and natural voice pitch
   drift; the game should not punish the voice's physics. Tuning
   calibration (the instrument's own offset is learnt and taken off)
   should work for voice too.
4. File each finding as its own issue with a reproduction (voice type,
   passage, what happened).

## Acceptance criteria

- [ ] At least one full level sung end to end (hummed and/or with
      syllables)
- [ ] Onset behaviour on consonants vs hums documented
- [ ] Pitch judgement with vibrato and with glides documented
- [ ] Calibration flow used with the voice, result reported
- [ ] Findings filed as separate issues with reproductions

## Pointers

- `apps/kan_demo` — free-tuner mode (onset flash, note, Hz, cents,
  stability bar)
- `libkan/include/libkan/technique.hpp` — vibrato/bend/expression
  validators (plain-data specs)
- `libkan` relative-tuning mode (`RelativeTuningTracker`) — intended for
  instruments without fixed pitch
- [docs/dsp/technique-validation.md](https://github.com/KairoPhos/kan-rhythm/blob/main/docs/dsp/technique-validation.md) —
  what the validators measure today
- [game/docs/gameplay.md](https://github.com/KairoPhos/kan-rhythm/blob/main/game/docs/gameplay.md) —
  judging rules (timing windows follow the music's pace)

## Notes

- Generalist rule: a fix that helps the voice must not break winds,
  strings or keys. Instrument behaviour is data, not code.
- If the voice simply cannot work for some class of notes, documenting
  the boundary honestly is a valid and valuable result.
