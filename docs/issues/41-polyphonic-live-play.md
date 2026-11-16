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

## Why: not the processor, not physics; the method and its validation (2026-11-15)

**The earlier 98.5 % did not measure judging.** Negative controls on the
Nocturne with the same offline core (details in
`docs/architecture/validation-status.md`, Correction): a score a semitone
off is "found" 92.9 % of the time (the right one 94.5 %), about 40 % of
wrong notes keep a pitch within 30 c, and a score 250 ms late is still
"found" 87.7 %. The core assumes the time and the pitch and asks only
whether energy is there; a piano always has energy near any hypothesis
(neighbouring harmonics, pedal resonance, the notes around it).

**Not the processor.** The offline core ran this Nocturne at 5.7x real
time on one core (about 18 % of it); the live describer takes 25 to 37 %.

**Not physics.** The information is in the signal: Basic Pitch, the small
network already in libkan (~17k weights, offline today), transcribes the
first 120 s of the same Nocturne with no score at all: recall 83.7 %,
precision 68.8 % (same pitch, onset within 50 ms), at 14.5 % of one
core. Larger published piano transcribers trained on MAESTRO report
higher. Piano is hard (shared harmonics, inharmonic strings, pedal), not
out of reach.

**The method.** Three gaps:

1. The live path follows one voice: a blind record's pitch is a single
   reading, so a chord or a pedalled passage reads as hops and resolves
   as "unresolved"; it is gated on detected onsets (71 of 347 notes).
2. The polyphonic fit tests only the hypothesis it is given. With no
   competing hypotheses (the note a semitone, a tone, an octave away) a
   wrong note absorbs the energy of the right one and passes.
3. Time is assumed, never measured: the score's time anchors the window.
   A judge needs the time the note was played.

And the validation had no negative controls, so it could not show any of
this.

## Direction

First the yardstick, then the method: nothing below is accepted until
the truth bench (acceptance, first item) shows it.

The game knows the score ahead of time, so the measurement can be
score-anchored, as `score_verify` already is offline, with what that
core lacks:

- Competing hypotheses: each written note is measured against its
   neighbours (a semitone, a tone, an octave), and the time of its attack
   is measured from its own level and onset evidence near the written
   time, not assumed.
- A learned front end as evidence: Basic Pitch's note and onset
   posteriors (already in libkan) beside the harmonic fit; streamed in
   short windows for live use.

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

- [ ] A truth bench of the game's path on MAESTRO, with negative
      controls built from the same recordings: the right score; notes
      changed by a semitone, a tone, an octave; notes left out; notes
      added; the score shifted in time. It reports, by notes sounding at
      once: right notes accepted, wrong notes rejected, the attack time's
      error, and CPU per block.
- [ ] Wrong notes (a semitone or more off) rejected at least 95 % of the
      time where up to 10 notes sound at once; the attack time within
      the perfect window for 95 % of right notes.
- [ ] Piano on the game's path finds at least 95 % of notes where up to
      10 sound at once, and reports the rest as not measured, not wrong.
- [ ] Ocarina and flute: identical events on the starter references.
- [ ] Runs in real time on the owner's desktop; the Deck may resolve
      fewer notes in the densest passages, never misjudge them.

## Notes

- MAESTRO is CC BY-NC-SA 4.0: the test levels and recordings stay on the
  developer's machine, never in the repository or on the server.
