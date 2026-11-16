# Polyphonic live measurement: design note

**Date:** 2026-11-15. Issue: `docs/issues/41-polyphonic-live-play.md`.
Status: design, nothing built yet. Every number this work produces comes
from the truth bench described in section 2.

## 1. The problem in one paragraph

The game judges from facts about played notes. The live describer
(`NoteDescriber`) produces those facts for one voice. On real piano it
hears 71 attacks for 347 notes and settles no pitch. The offline core
(`score_verify`) is given each note's time and pitch and asks only if
energy is there, so it accepts wrong notes and measures no time. Piano
needs a third thing: a measurement that is anchored on the chart, tests
the written note against the notes it could be confused with, and
measures when the note was struck.

## 2. The yardstick comes first: the truth bench

A tool in `game/forge/native/` (next to `kan_forge_bench`), because it
drives the C ABI the game calls.

**Input.** A MAESTRO recording and its MIDI (the truth), an excerpt, and
a seed. The audio goes through `kan_forge_live_process` in 10 ms blocks,
as the game feeds it.

**Cases.** The bench builds every score itself from the truth MIDI:

| Case | Score given | What it proves |
|---|---|---|
| right | the truth | right notes are accepted, time is measured |
| semitone, tone, octave | 30 % of notes moved by 1, 2 or 12 semitones | a wrong pitch is rejected |
| added | notes the pianist never played, 30 % more | a note that is not there is rejected |
| left out | 30 % of notes removed | played notes outside the score do not disturb the rest |
| shifted | the whole score 120 and 250 ms late, 120 ms early | time is measured, not assumed |
| jitter | each note moved by up to 60 ms | the same, note by note |

A changed or added note counts as wrong only if the truth has no attack
of that pitch inside the note's window.

**Outcome of each score note**, one of four:

- *accepted*: the path reports the written pitch, with an attack inside
  the judge's widest window around the written time;
- *misreported*: the path reports another pitch for it;
- *absent*: the path reports nothing for it;
- *not measured*: the path says it could not measure it.

The windows are the judge's own (`LiveJudge.WindowMs` and `PaceOf`,
ported as constants with a reference to the C# source).

**Report**, by notes sounding at the attack (1, 2 to 4, 5 to 10, 11+):

- right notes: accepted, not measured, misreported or absent;
- wrong notes: rejected (anything but accepted);
- attack time error against the truth: median, p95, share inside the
  perfect window;
- CPU per 10 ms block: median, p99, worst, share of real time. Runs are
  pinned with `taskset`, repeated, and reported with their spread.

**Paths under test** (adapters to the same outcome):

- `blind`: today's game path, played notes paired to score notes;
- `expect`: the same with `kan_forge_live_expect`;
- `core`: today's offline core (`score_verify::measure_score`), as the
  baseline of the old claim;
- `chart`: the new path of section 3, when it exists;
- `oracle` and `yes`: a perfect path and one that accepts everything.
  They test the bench itself.

**Corpus.** Two fixed lists of excerpts: a development list from
MAESTRO's train split, for tuning, and a held-out list from its
validation and test splits, for the numbers we publish. Basic Pitch was
trained on MAESTRO's train split, so only the held-out list can speak
for it.

**Selftest (ctest, no dataset).** A synthetic piano-like piece with known
truth. It checks that the oracle scores 100 % on both sides, that the
yes path rejects 0 % of wrong notes, and that the counts of every case
add up. Later it also holds the floor the new path must keep.

## 3. The method: chart-anchored measurement with rivals

A new class in libkan, apart from `NoteDescriber`, so the one-voice path
does not change by a byte. Working name `ChartMeasurer`
(`libkan/include/libkan/chart_measure.hpp`).

**The game hands over the chart once.** Every note: id, start, end,
pitch and its search window. The game also tells libkan how stream time
maps to chart time, and again whenever that changes (rate, waiting).

**Per written note, libkan measures:**

1. **When.** An attack evidence curve for the written pitch over the
   search window. Its peak is the measured attack time. A peak serves
   one written note only, so repeated notes pair in order.
2. **Which pitch.** The same curve for the rivals: a semitone, a tone
   and an octave either side, leaving out pitches the chart itself
   expects there. The fact names the best supported pitch and its margin
   over the runner-up.
3. **How sure.** When the margin is too small, or the budget of the
   moment is spent, the fact is "not measured". When no pitch in the set
   shows an attack, the fact is "no attack found". libkan reports
   measurements. The game decides what they are worth.

**Two kinds of evidence, each measured alone, then together:**

- *Harmonic.* The joint least-squares fit libkan already has
  (`HarmonicChordValidator`), with the written notes alive and the
  rivals fitted together, in groups of at most 10. The attack curve is
  the rise of a note's fitted level. The octave case needs the partials
  that tell the two notes apart, which a joint fit keeps.
- *Learned.* Basic Pitch's onset and note posteriors, run as a stream in
  short windows. Its cost does not grow with density. Its open questions
  are latency (the low notes' filters look far ahead) and how well it
  rejects rivals. Phase 2 measures both before anything is built on it.

**Facts to the game.** A new live event, one per written note: chart
note id, measured attack time, best supported pitch, cents, level,
support, margin, and the not-measured flag. The event struct keeps its
layout. The game turns each into what its judge already takes (an attack
with a pitch), so the judge and its forgiving defaults stay as they are.

**One lane per expected key (owner, 2026-11-15).** The game should show
piano keys lighting as they are played, as it shows one voice today. So
each written note lives as a record of its own, many at once, with the
event shapes the game already takes:

1. a provisional attack, fast, so the key lights (the aim is under
   about 100 ms; harmonic evidence in short windows is the candidate);
2. the final fact: measured attack time and the pitch confirmed against
   its rivals (what the judge uses);
3. the level while it sounds, so the key stays lit;
4. the close, so the key goes dark and the duration is known.

A wrong key near the written one (a rival) can be shown as such, because
it is a hypothesis already measured. A key far from anything written is
blind transcription, the problem this design chooses not to take on: at
most a weaker fact, "something sounded that the chart does not explain".
The principle stands for the whole path: the chart makes every question
a closed one. The truth bench also reports the provisional fact's
latency and how often it differs from the final one.

**Time from the attack, pitch from the tail (owner, 2026-11-15).** The
two are measured at different moments with different windows, following
the physics of the instrument family. The family's shape is profile data
(`InstrumentProfile`), never code for one instrument.

- *Struck and plucked (piano, guitar): a jump, then a decay.* The attack
  time comes from short windows on the note's upper partials, which
  answer fast even for a low note. The pitch is confirmed later, with a
  long window on the ringing tail: a low piano note sounds on after a
  short key press, and 170 ms of tail separates partials that 43 ms
  cannot. An attack is a jump in level, so a rival that already rings
  under the pedal, and only decays, is not an attack. The end of the key
  is often not observable (the note fades), and is flagged so.
- *Blown and bowed (ocarina, flute, bowed strings): a held level, then a
  cut.* The pitch is confirmed during the hold. The close is a clear
  cut. The attack may carry no transient, as the one-voice path already
  handles.

So a low note's final fact arrives later than a high note's. Its attack
time does not move. The game's wait follows the fact's deadline.

**Effort.** All heavy work runs as scheduled units on the describer's
worker thread. The chart gives the density ahead of time, so the
schedule is known before the passage arrives. Past the budget a note
resolves as not measured, never as wrong.

**One voice.** A mono profile ignores the chart call. The bench can dump
every live event of a recording as text. The dumps of the starter
references are taken before any change and compared after every step.
The dump detects change, it does not forbid it (owner, 2026-11-15): if
this work finds a bug in the one-voice path, the bug is fixed, and the
change is explained and measured on the ocarina's own references.

## 4. Order of work

1. Truth bench, selftest, baseline of `blind`, `expect` and `core`.
2. Streamed Basic Pitch: latency, cost, and its rival rejection, in the
   bench alone (no API yet).
3. `ChartMeasurer` with harmonic evidence. Then the two together.
4. C API and the game: chart feed, clock map, facts to the judge.
5. Scheduling by density. Deck budget.
6. Documents: issue 41, validation status, libkan README.

A step is kept only when the bench shows it better on the held-out list
and no measure worse.

## 5. Risks, said early

- **Bass semitones.** Two low notes a semitone apart differ by a few Hz.
  A 43 ms window cannot separate their fundamentals, only their upper
  partials. Expect the low register to hold the last misses.
- **Octaves.** A note an octave up shares every partial with the note
  below. Rejecting it rests on the odd partials of the lower note.
- **Pedal.** Sounding notes outlive their keys. A rival may already ring
  when the written note is struck. The measure is the rise, not the
  level, for that reason.
- **Latency.** The judge waits about 300 ms for a pitch today. Learned
  evidence for low notes may need more. If so, the game's wait has to
  follow the fact's deadline.
- **The targets may not all be reached.** If so, the report says how far,
  where (register, density) and what would close it. A larger trained
  model is the known next step, with its own licence question, since
  MAESTRO is non-commercial.
