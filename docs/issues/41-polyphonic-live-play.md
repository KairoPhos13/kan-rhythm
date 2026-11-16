# Polyphonic live play: piano measured on the game's own path

Labels: `libkan`, `game`, `judging`, `polyphony`
Size: L

## Context

Owner, 2026-11-15: advanced levels must be able to ask for more, up to
scores played with ten fingers. MAESTRO's professional repertoire has
held up to 15 keys at once and up to 36 notes alive in the judge's
window. Not every machine, the Steam Deck included, has to run that, but
the algorithm has to adapt to it.

libkan was measured on MAESTRO before (`tools/dataset_bench`, 98.6 % of
notes found), but through the **score-anchored offline core**
(`score_verify`), not through what the game runs while a player plays.
This issue is the first measurement of the game's own path, the "test of
truth".

## What was measured (2026-11-15)

The game's live describer (`kan_forge_live_process`, `keyboard`
profile, 10 ms blocks as the game feeds it) on MAESTRO recordings,
compared with their MIDI (same pitch, onset within 70 ms):

| Recording | Notes | Attacks heard | Final pitches | Found |
|---|---|---|---|---|
| Chopin Etude Op. 25 (2004), first 30 s | 446 | 36 | 0 | 0 % |
| Chopin Nocturne Op. 9 No. 2 (2011), first 60 s | 347 | 71 | 0 (62 unresolved) | 0 % |
| Same, with every chord expected (`kan_forge_live_expect`) | 347 | 133 | 14 | 3.5 % |

libkan's own tool on the same minute (`kan_demo --instrument piano
--input`) gives the same picture: 67 onsets, 1 final pitch, 102
unresolved. **The game's wiring is not the cause: the blind live
describer does not resolve real piano.** It was built and tuned on one
voice at a time (ocarina, flute), where it works.

Expectations do not rescue it as they are: the API holds one set for
"the next attack" (146 of 209 sets were refused because the previous one
was still waiting), a set holds at most 10 notes, and the game never
calls it.

CPU on piano: 25 to 37 % of one core in real time (the ocarina melody
takes 3 %), worst 10 ms block 13 to 31 ms.

The game around the describer was also tested with three local MAESTRO
levels in autoplay (score = every key, reference = the recording, no
companion). Four costs that grew with density were found and fixed for
every instrument (issue 40); the judge itself handles chords and dense
passages: 1048/1048, 1051/1051 and 580/580 perfect.

## Direction

The game knows the score ahead of time, so the measurement can be
score-anchored, as `score_verify` already is offline:

1. The game hands libkan the chart's notes ahead of time (all of them, not one set per attack).
2. The describer measures every note alive in its window jointly, in
   groups of at most 10 per fit when more are alive (the plan noted in
   `tools/dataset_bench/TEST_LOG.md`, ISS-3), and reports each note's
   presence, pitch and level as facts. The game judges.
3. Effort follows density, scheduled ahead of dense passages
   (`docs/architecture/game-judging-guide.md` §2), within the live budget
   of two analysis threads. Past what a machine sustains, notes resolve as
   "not measured", never "wrong".
4. The one-voice path (ocarina, flute) keeps its results byte for byte.

## Acceptance

- [ ] A bench of the game's path on MAESTRO (the scratch harness used
      here, made a tool): recall by notes sounding at once, CPU per block.
- [ ] Piano on the game's path finds at least 95 % of notes where up to
      10 sound at once, and reports the rest as not measured, not wrong.
- [ ] Ocarina and flute: identical events on the starter references.
- [ ] Runs in real time on the owner's desktop; the Deck may resolve
      fewer notes in the densest passages, never misjudge them.

## Notes

- MAESTRO is CC BY-NC-SA 4.0: the test levels and recordings stay on the
  developer's machine, never in the repository or on the server.
