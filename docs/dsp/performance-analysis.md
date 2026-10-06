# Performance analysis — describing how a song is played

Status: `apps/kan_analyze` implemented and run on this machine over four
synthesized scenarios covering the corner cases of real performances
(monophonic wind, polyphonic piano, duet with companion instrument, FX
tails). The tool accepts real audio (WAV/MP3/FLAC) — feed it exported
recordings and it produces the same descriptor.

## What the descriptor is

Per-note events + song-level features — **the same features a learner is
evaluated on**, so a reference descriptor is both the level seed and the
rubric:

- events: onset ms, midi/hz/cents, duration, attack/release, sustain ratio,
  vibrato (rate/depth), reliability flag
- song: tempo (median IOI), intonation mean/max + in-tune count, vibrato
  usage, staccato count, range
- `--json` for machine consumption (level seeding, evaluation diffs)

```bash
kan_analyze my_recording.mp3 --instrument ocarina [--json]
kan_analyze --synth <ocarina|piano|duet|fx>   # offline scenarios
```

## Measured outputs (this machine)

| scenario | evaluated | intonation mean/max (c) | vibrato | verdict |
|---|---|---|---|---|
| ocarina (mono melody, 5.2 Hz/±38 c vibrato + staccato) | 15/15 | 2.3 / 3.6 | 9 notes @ **5.1 Hz, 33 c** (truth 5.2/38) | correct description |
| piano (melody + block chords + pedal) | 2/16 | 0.6 / 0.6 | — | polyphony honestly excluded |
| duet (wind melody + piano companion stabs) | 1/24 | 2.1 / 2.1 | — | competing fundamentals excluded |
| fx (delay 230/460 ms + noise floor) | 10/10 | 0.1 / 0.3 | — | **zero ghost onsets** from tails |

## Corner cases found (and what the tool does about them)

1. **Polyphony is a physics boundary for blind mono analysis.** Piano
   chords give the tracker multiple simultaneous fundamentals: it either
   locks onto one partial (often an octave grab) or hops. The descriptor
   flags these notes `MIX`/`HOP` and **excludes them from statistics**
   rather than reporting garbage (before the fix the piano scenario claimed
   "intonation 1461 c, vibrato 66 Hz" — tracker noise, not music).
   Resolution for level material: chords are handled by the **score-informed**
   path (chart template + `HarmonicChordValidator`, F1 1.0) — blind
   polyphonic transcription is explicitly out of scope (SPEC §2: no blind AMT).
2. **Companion instruments (duet) steal the tracker.** A piano stab during a
   wind sustain flips the pitch estimate; hopping detection (`HOP`) catches
   it. For evaluation against a reference, the score-informed matcher's
   ±2-semitone bounding rejects the companion entirely — that is the
   designed game path.
3. **FX tails inflate note durations.** Delay/reverb keeps energy above the
   silence threshold, so notes read 900 ms instead of 400 ms. Ghost onsets
   did **not** occur (the HFC-jump onset test ignores smooth tails —
   validated). Mitigation for duration-based articulation features: the
   decay-relative envelope metrics (attack/release/sustain ratio) are
   already reported; a decay-based note-end rule is a listed follow-up.
4. **The analyzer found a bug in its own scenario generator**: synthesizing
   vibrato as `sin(2π·f·t·vib(t))` mixes in a huge spurious FM term (fake
   20–46 Hz "vibrato", octave errors). Correct synthesis integrates the
   modulation into phase. A caution for any future fixture generation.
5. **Cents trajectories must not wrap.** Nearest-note cents jumps ±100 at
   the semitone boundary and destroys vibrato analysis; the descriptor
   tracks continuous deviation from a fixed per-note reference.
6. **Short notes under-count vibrato cycles.** libkan's `periodic` needs
   ≥3 oscillation cycles (rate physics); the descriptor accepts clear
   2+-cycle oscillations at app level while keeping libkan strict.

## Using it for levels / learning evaluation

1. Run `kan_analyze` on a reference performance → descriptor (+ `--json`).
   `apps/kan_analyze/run_reference_set.sh <audio_dir>` runs a whole song
   folder and collects per-song `.log` + `.json` (profile auto-selected
   from the filename: `*piano*`, `*ocarina*`, `*xun*`, ...); `--demo`
   reproduces the four built-in scenarios.
2. The events are the level seed (onset times, pitches, techniques); the
   song features are the quality bar.
3. A learner's run produces the same descriptor (`kan_demo --csv` for live
   sessions, `kan_analyze` for recordings); compare per-feature
   (timing = onset deltas, intonation = cents, technique = vibrato/
   articulation match). The comparison layer lives in `game/` (M2) —
   libkan provides the measurements.

## Not doable blind (documented, per project policy)

- Extracting separate melody+accompaniment lines from a mixed recording
  (source separation is M3 editor-time AI work — HTDemucs — never real-time).
- Per-note pitch evaluation inside sustained polyphony without a chart
  template.
