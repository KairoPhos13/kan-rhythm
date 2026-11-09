# Play-test kan! on harmonica (free-reed family)

Labels: `instrument-testing`, `help wanted`
Size: M

## Context

kan!'s instrument coverage is a stated release goal ("tried on more
instruments … a recorder or flute, voice, a bowed and a plucked one" —
[game/TODO.md](https://github.com/KairoPhos/kan-rhythm/blob/main/game/TODO.md))
and the free-reed family is the one no pipeline test has seen. Harmonica
is a genuinely different physical case: pitch is fixed by hole/reed but
**bent** continuously by the player, chords sound on adjacent holes
whether you want them or not, tremolo and hand-wah modulate the tone, and
breath direction (blow/draw) is part of the attack.

This matters for the generalist promise: bend and vibrato validators and
the relative-tuning tracker are meant for exactly this kind of instrument,
and only a player can say whether judging a bend feels fair.

## What to do

1. Build and run the game (`make run` in `game/`) and `apps/kan_demo`
   (free-tuner mode; use the `wind` profile).
2. On a diatonic or chromatic harmonica (say which):
   - play one easy level end to end;
   - stress cases: draw bends and blow bends into a note, tremolo,
     hand-wah, blow/draw alternation on repeated notes, accidental
     neighbouring-hole sound (is it judged gently or as a failure?),
     soft breath notes.
3. Check that the pitch judgement follows a *deliberate* bend (it should
   arrive at the target and be accepted) and does not punish the
   transition into it.
4. Report findings as separate issues with reproductions (harmonica
   type, key, hole numbers, what happened).

## Acceptance criteria

- [ ] At least one full level played end to end on a harmonica
- [ ] Bend behaviour documented (deliberate bends vs judged pitch)
- [ ] Blow/draw attack behaviour documented
- [ ] Neighbouring-hole sound / chord bleed behaviour documented
- [ ] Findings filed as separate issues with reproductions

## Pointers

- `apps/kan_demo` — free-tuner mode: pitch readout + stability bar while
  bending
- `libkan/include/libkan/technique.hpp` — bend/vibrato trajectory
  validators (specs the game consumes)
- `libkan` `RelativeTuningTracker` — moving tonal centre for instruments
  whose pitch is not fixed
- [docs/dsp/technique-validation.md](https://github.com/KairoPhos/kan-rhythm/blob/main/docs/dsp/technique-validation.md) —
  measured behaviour of the validators
- [game/docs/gameplay.md](https://github.com/KairoPhos/kan-rhythm/blob/main/game/docs/gameplay.md) —
  how bends and pitch transitions are judged today

## Notes

- Generalist rule: never a "harmonica mode" hidden in code — instrument
  behaviour is data (profiles, technique specs).
- All findings welcome, including "this cannot work on harmonica unless
  X" — honest boundaries are useful results.
