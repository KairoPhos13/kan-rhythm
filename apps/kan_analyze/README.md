# kan_analyze — song performance descriptor

Describes **how a piece is played**: per-note events (onset, pitch, cents,
duration, envelope, vibrato) + song-level features (tempo, intonation,
articulation mix, range). These are the same features a learner is evaluated
on — a reference descriptor is both the **level seed** and the **rubric**.

```bash
kan_analyze my_recording.mp3 --instrument ocarina [--json]
kan_analyze --synth <ocarina|piano|duet|fx>   # built-in offline scenarios
```

- Sources: WAV/MP3/FLAC (miniaudio decoder) or synthesized scenarios.
- Instruments: `ocarina` (default), `flute`, `recorder`, `guitar`, `piano`.
- Output: text report or `--json` (machine-readable events + features).
- Reliability flags: `MIX` (low pitch validity = polyphonic/overlapped) and
  `HOP` (tracker hopping between sources) are listed but **excluded** from
  statistics — blind mono analysis cannot separate polyphony; chords and
  companions belong to the score-informed path (`HarmonicChordValidator`).

Findings, measured outputs and corner cases: `docs/dsp/performance-analysis.md`.
Offline self-test: `ctest -R kan_analyze_selftest`.
