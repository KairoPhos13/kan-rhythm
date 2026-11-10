# kan_analyze: song performance descriptor

Describes **how a piece is played**: per-note events (onset, pitch, cents,
duration, envelope, vibrato) + song-level features (tempo, intonation,
articulation mix, range). These are the same features a learner is evaluated
on: a reference descriptor is both the **level seed** and the **rubric**.

```bash
kan_analyze my_recording.mp3 --instrument ocarina [--json]
kan_analyze --synth <ocarina|piano|duet|fx|hop>   # built-in offline scenarios
kan_analyze my_recording.wav --events notes.jsonl # per-note description stream
```

- Sources: WAV/MP3/FLAC (miniaudio decoder) or synthesized scenarios.
- Instruments: `ocarina` (default), `flute`, `recorder`, `guitar`, `piano`.
- Output: text report or `--json` (machine-readable events + features);
  `--events <f.jsonl>` writes the per-note description stream (HIT /
  provisional / CONTENT / final / EXPRESSION / CLOSE facts,
  `libkan/note_stream.hpp`), the same schema `kan_demo --events` logs
  live, byte-identical on the same audio.
- One estimate per event: `midi`/`hz`/`cents` describe the same reading of
  the note (Hz derived from the note + cents pair). Rows the tool cannot
  estimate honestly print `-` (text) / `null` (JSON) in those columns.
- Reliability flags: `MIX` (low pitch validity = polyphonic/overlapped) and
  `HOP` (tracker hopping between sources) are listed but **excluded** from
  statistics. Blind mono analysis cannot separate polyphony; chords and
  companions belong to the score-informed path (`HarmonicChordValidator`).

Findings, measured outputs and corner cases: `docs/dsp/performance-analysis.md`.
Offline self-tests: `ctest -R kan_analyze` (`kan_analyze_selftest` pipeline
run + `kan_analyze_hop_test`, `kan_analyze --selftest`: report-consistency
checks on the synthesized hopping-note fixture).
