# Play-test kan! on guitar and other plucked strings (plucked family)

Labels: `instrument-testing`, `help wanted`
Size: M

## Context

kan! is built for "a $5 plastic recorder, an acoustic guitar, a violin …"
([README.md](https://github.com/KairoPhos/kan-rhythm/blob/main/README.md)),
yet nearly all play-testing so far is on one wind instrument. Plucked
strings stress different parts of the pipeline: very fast attack
transients (blanking window is 8 ms for plucked), dead strings and mutes
(percussive attacks without a sustained tone), chords and strums
(multi-note validation), per-string tuning offsets, and short decays that
can end a note earlier than the player intends.

The pipeline has per-string compensation and chord validation ready to be
exercised ([libkan/TODO.md](https://github.com/KairoPhos/kan-rhythm/blob/main/libkan/TODO.md))
— real play is what tells us whether they feel right.

## What to do

1. Build and run the game (`make run` in `game/`) and `apps/kan_demo`
   (`guitar` preset; `--profile plucked` in `kan_cli`).
2. On an acoustic or classical guitar (bass, ukulele, harp also welcome —
   say which):
   - play one easy level end to end (single-note lines);
   - stress cases: chords/strums where the level asks for a chord,
     palm mutes and percussive hits, fast repeated notes, soft plucks,
     open strings vs fretted notes, capo or alternate tunings.
3. Run the per-string compensation flow (guided strum input) and report
   whether each string is judged on its own tuning.
4. Check note endings: the player's release should decide, not the
   string's decay alone (duration is judged against the release bias —
   see [game/docs/gameplay.md](https://github.com/KairoPhos/kan-rhythm/blob/main/game/docs/gameplay.md)).
5. File findings as separate issues with reproductions.

## Acceptance criteria

- [ ] At least one full level played end to end on a plucked instrument
- [ ] Chord/strum judging behaviour documented (what the level asks vs
      what the judge accepts)
- [ ] Mutes / percussive attacks documented (they should count as
      rhythmic hits, not as pitch failures)
- [ ] Per-string compensation flow run and its result reported
- [ ] Note-release behaviour on short decays documented
- [ ] Findings filed as separate issues with reproductions

## Pointers

- `apps/kan_demo` — `guitar` preset; `apps/kan_cli --profile plucked`
- `libkan` — per-string compensation matrix API (`Δc1..Δc6`, guided
  strum input), chord validation
  (`chord_goertzel.hpp`, `chroma_fft.hpp`)
- `libkan/include/libkan/instrument_profile.hpp` — 8 ms plucked blanking
- [docs/dsp/chord-validation.md](https://github.com/KairoPhos/kan-rhythm/blob/main/docs/dsp/chord-validation.md) —
  measured chord-check behaviour
- [game/docs/gameplay.md](https://github.com/KairoPhos/kan-rhythm/blob/main/game/docs/gameplay.md) —
  judging rules, percussive/mute handling

## Notes

- Generalist rule: fixes live in shared detection code or instrument
  profile data (I10), never in guitar-only branches.
- If you also have a bass or a harp, those observations belong in the
  same report — say which instrument each note is about.
