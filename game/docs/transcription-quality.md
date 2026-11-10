# Detection quality: how well kan! hears a level

Owner request (2026-10-15): the transcription quality is a likely source of
errors, both for the editor's starting score and for gameplay judging.
Measure it, keep the numbers, and decide from them.

## The tool: `kan_forge_bench`

Built with the bridge (`game/forge/native`, `make` builds it at
`forge/native/build/kan_forge_bench`). It calls the same C ABI the game
calls, so it measures exactly the detector the game runs.

```sh
E=~/.local/share/kan/kan-forge/<level>/exports/<export folder>
forge/native/build/kan_forge_bench --audio "$E/reference.wav" \
    --truth "$E/score (corrected).mid" --profile ocarina [--csv out.csv]
```

The ground truth is a **validation export** from the compose tab (the
"export MIDI" button): the reference audio and the score as corrected by
hand. Any audio + MIDI pair works.

It reports three things:

1. **Blind transcription** (what the editor and the game use today):
   - precision, recall and F1 of onset + right pitch (±50 / ±100 ms) and
     of onsets alone;
   - the kind of pitch error on onset-matched notes (semitones, octave,
     other, unresolved);
   - repeats within 30 ms;
   - the global offset to the truth, which is then aligned before the
     other metrics.
2. **Expected mode streamed:** libkan's candidate mode
   (`kan_forge_live_expect`) armed note after note, as a game would use
   it.
3. **Expected mode per note:** a fresh describer around each heard attack,
   with the expectation armed 60 ms before it. It is armed with the right
   pitch and with *wrong* ones (±1, ±2, ±12 semitones), to measure
   confirmation and false acceptance separately from the streaming limits.

"Confirmed" uses libkan's own bar for claiming a pitch: similarity ≥ 0.40
and the refined pitch inside the search band.

## Results

### 2026-10-15: "Itsumo Nando Demo", 12-hole alto C ocarina (220 s)

Ground truth: the owner's corrected score, 325 notes. **Caveat:** it was
corrected by ear against the song, not verified note by note. Some notes
were moved, deleted or re-pitched, so it is an imperfect truth. Blind
transcription below uses the `ocarina` profile with the open range
(A0-C8).

**1. Blind transcription**

| | value |
|---|---|
| notes found | 617 (truth: 325) |
| without a pitch | 63 (10.2 %) |
| repeated within 30 ms | 126 |
| offset to the truth | **+102 ms** median (246 pairs): the transcriber reports onsets later than the hand-placed notes |
| onset ±50 ms + right pitch | precision 28.7 %, recall 54.5 %, **F1 37.6 %** |
| onset ±100 ms + right pitch | precision 33.2 %, recall 63.1 %, F1 43.5 % |
| onset ±50 ms, any pitch | precision 40.0 %, recall 76.0 %, F1 52.4 % |
| pitch of onset-matched notes | right 70.4 %, ±1-2 semitones 12.6 %, octave 0 %, other 2 %, unresolved 15 % |
| onset error after alignment | median +16.8 ms, MAD 17.2 ms |

**2. Expected mode, streamed:** of the 293 heard notes, 10.6 % confirmed,
56 % not confirmed and 33 % never measured. Median similarity 0.01. Not
usable as is (see the libkan limits below).

**3. Expected mode, per note** (293 notes with a heard attack; blind mode
named the right pitch for 65.5 % of them):

| expectation | confirmed | not confirmed | not measured | similarity (median) |
|---|---|---|---|---|
| right note | **72.7 %** | 23.9 % | 3.4 % | 0.99 |
| wrong by +1 | 2.4 % | 94.2 % | 3.4 % | 0.17 |
| wrong by −1 | 3.1 % | 93.5 % | 3.4 % | 0.06 |
| wrong by +2 | 4.8 % | 91.8 % | 3.4 % | 0.01 |
| wrong by −2 | 7.8 % | 88.7 % | 3.4 % | 0.01 |
| wrong by +12 | 0.0 % | 96.6 % | 3.4 % | 0.00 |
| wrong by **−12** | **74.1 %** | 22.5 % | 3.4 % | **0.99** |

### 2026-10-15 (later): same take, after the libkan fixes (`main` 2b114bc, 3e6ca04)

Two libkan changes:
- one record at a time on mono profiles: no twins and no endless notes;
- articulation by level for winds: tongued notes, slurs read every 20 ms
  with an 80 ms hold, breaths.

Truth and caveats are as above.

| blind transcription | before | after |
|---|---|---|
| notes found | 617 | 542 |
| repeated within 30 ms | 126 | **0** |
| onset ±50 ms + right pitch | F1 37.6 % (P 28.7, R 54.5) | **F1 57.0 %** (P 45.6, R 76.0) |
| onset ±100 ms + right pitch | F1 43.5 % | **F1 64.6 %** |
| onset ±50 ms, any pitch | F1 52.4 % (R 76.0) | F1 66.2 % (**R 88.3**) |
| pitch of matched notes: right / ±1-2 st / unresolved | 70.4 / 12.6 / 15.0 % | **82.9 / 2.8 / 14.3 %** |
| onset error after alignment (median, MAD) | +16.8 ms, 17.2 | +1.1 ms, 21.0 |
| cost for 220 s | 25.2 s | 19.4 s |

The offset to the hand-placed score is still **+111 ms**. On synthetic wind
phrases with exact truth (libkan fixture 14) onsets are now +3 ms median,
so this offset is **not** detector latency. The ocarina recording itself
sits behind the piano: the owner moved the notes onto the piano's timing.

Expected mode, after the fixes:
- **Per note:** the right expectation is confirmed for 67.1 % of 313
  heard notes; wrong ones by ±1-2 semitones 2-10 %, +12 never, **−12
  68.7 %**. Bound attacks 7.8 → 45.4 %, because re-articulated notes no
  longer go through the deferral.
- **Streamed:** 5.8 % confirmed.
- **Blind now beats expected per note** (83.7 % right pitch on the same
  notes). The game keeps judging blind.

Piano regression check (MAESTRO validation, two pieces, keyboard profile,
blind): before and after give identical output. Separately, blind piano
transcription is poor on MAESTRO: 39 notes found for a dense 167 s piece,
all without a pitch, and the same before the change. MAESTRO validated
the score-anchored path (`dataset_bench`), not blind transcription. To
look into before piano levels rely on the editor's transcription.

### 2026-10-15 (evening): Basic Pitch in libkan (`main` d4175c3, 37b1674)

Owner decision: a trained transcriber instead of more hand-made rules.
Basic Pitch (Spotify, Apache-2.0) now runs inside libkan in plain C++
(`libkan/basic_pitch.hpp`, `transcribe_audio_neural`); the bridge picks it
with `kan_forge_transcribe_file_with(..., "neural", ...)` and the bench
with `--engine neural` (blind only). The port matches the reference: 498
of 498 notes identical on this take.

Ocarina take, same truth and alignment as above:

| engine | notes | F1 ±50 ms + pitch (P / R) | F1 ±100 ms | right pitch / unresolved | time |
|---|---|---|---|---|---|
| describer (`ocarina`) | 542 | 57.0 % (45.6 / 76.0) | 64.6 % | 82.9 / 14.3 % | 20.4 s |
| **neural, one note at a time** (`ocarina`) | 462 | **70.1 %** (59.7 / 84.9) | **78.0 %** | **97.9 / 0 %** | **3.7 s** |
| neural, reference polyphonic decoding (`keyboard`) | 497 | 68.4 % (56.5 / 86.5) | 76.2 % | 97.2 / 0 % | 4.3 s |

Piano, MAESTRO v3 test split, exact truth (`--offset 0`, `keyboard`):

| piece | describer F1 | neural F1 (P / R) | describer / neural time |
|---|---|---|---|
| Scarlatti K. 525 (66 s) | 0.0 % | 64.6 % (79.8 / 54.2) | 15.9 / 1.2 s |
| Scarlatti K. 96 (139 s) | 0.0 % | 67.3 % (75.5 / 60.8) | 31.9 / 2.7 s |
| Liszt "Gnomenreigen" (168 s) | 0.2 % | 54.0 % (76.9 / 41.7) | 34.6 / 3.5 s |
| Scarlatti K. 239 (196 s) | 0.0 % | 67.3 % (70.4 / 64.6) | 47.7 / 4.0 s |

The describer finds piano attacks but leaves 95-100 % of them without a
pitch. Basic Pitch saw MAESTRO's training split; these pieces are held out.

The editor now transcribes with Basic Pitch (`ForgeJobs.TranscribeAsync`
defaults to the neural engine: track tab, compose section tools). Two
things keep the describer: the mic calibration (the delay it measures is
the live judge's) and the live judge itself (Basic Pitch reads 2 s
windows; its live delay is unmeasured). The instrument still matters:
one note at a time or several, the range, and the judge's profile.

## What it means

- **The main sources of error, in order:**
  1. **Extra notes.** Precision is 29-40 %: almost two found notes per
     real one. Repeats (126) and re-attacks inside held notes are a large
     part of it, and so are ornaments the owner did not keep.
  2. **Unresolved pitches** (15 % of matched notes) and **near misses of
     1-2 semitones** (12.6 %). No octave errors in blind mode here.
  3. **Missed attacks:** 24 % of truth notes have no onset within ±50 ms,
     mostly soft or slurred attacks.
  4. **A constant lateness of about 100 ms** against the hand-placed
     score. It is either latency in the transcriber's onset estimate or
     the owner's preference for placing notes. It is the same order as
     the transcriber-delay setting in the timing tab. The game's
     pitch-arrival credit and the mic calibration absorb it in play.
- **Expected mode helps when it is aimed right:** per note it confirms
  72.7 % against 65.5 % for blind, and rejects wrong notes 1-2 semitones
  off 92-97 % of the time. **But it cannot be trusted alone for octaves:**
  an expectation one octave *below* the played note is "confirmed" 74 % of
  the time with similarity 0.99. The note played contains the expected
  pitch's harmonics. An expectation one octave above is never confirmed.
  A game using expected mode must cross-check the octave, for example
  with the blind pitch reading (the trail's YIN frame) or a presence check
  on the octave above.
- **Expected mode does not work streamed today.** The game keeps judging
  blind (`LiveJudge`) until libkan lifts the limits below.

## Needs from libkan (also in editor-pipeline §7b.3)

Fixed on `main` 2026-10-15: duplicates and unterminated notes (one
record at a time), and slur/tonguing/breath articulation. Still open:

3. **Expectation not consumed by legato changes:** a transition record
   (pitch change without an attack) neither measures nor consumes the
   pending expectation, so on a slurred instrument the next attack takes
   the wrong expectation and the sequence shifts.
4. **One pending expectation that can't be replaced:** `set_candidates`
   refuses while one is pending. A game that missed an attack cannot
   re-aim and every later measurement is off by one. An API to replace or
   clear the pending set, or expectations keyed by time, would fix it.
5. **Monophonic deferral drops the binding:** when a note is still
   sounding at the next attack, the new record is created without the
   expectation (its HIT is blind and the CANDIDATE fact is filed under
   record 0; only 7.8 % of attacks are bound). The bridge links them by
   strike group as a workaround.
6. **Octave-below false acceptance** in candidate measurement (table
   above): a presence test against the expectation's octave above would
   separate them.

## Re-running

Run the bench after any change to the detector, the profiles or the
bridge, and after making a new validation export. Add each run's numbers
above, dated, with the level, instrument and truth caveats. Piano and
other instruments: export a level in the compose tab and run the same
command with `--profile keyboard` (etc.).
