# apps/kan_analyze: TODO

Song performance descriptor: per-note events + song features = level-seed
material and learner-evaluation rubric. See `docs/dsp/performance-analysis.md`.

## Done

- [x] WAV/MP3/FLAC input (miniaudio decoder) + `--synth` offline scenarios
- [x] Full pipeline analysis: stage 0 → onset → blanking → blind pitch →
      per-note records (time, pitch, cents, envelope, vibrato)
- [x] Song features: tempo (median IOI), intonation stats, vibrato usage,
      staccato count, range
- [x] Reliability flags: MIX (polyphony/overlap) + HOP (tracker hopping),
      excluded from statistics, honestly reported
- [x] `--json` machine-readable output (level seeding)
- [x] Corner-case scenarios validated: mono wind, polyphonic piano, duet
      with companion, FX tails (zero ghost onsets)
- [x] **One consistent estimate per row**: note/Hz/cents come from a single
      (midi, cents) estimate (Hz derived, never mixed with a last reading);
      MIX/HOP rows print `-` / `null` instead of contradicting numbers.
      Covered by `--selftest` / ctest `kan_analyze_hop_test` on a
      synthesized hopping note (scenario `--synth hop`)

## Next

- [x] Per-note description stream output: `--events <f.jsonl>` emitting the
      `NoteEvent` stream (one schema shared with kan_demo and the live
      path; live/offline byte-identical)
- [ ] Decay-relative note-end rule (FX tails currently inflate durations;
      envelope decay metrics already reported; part of the stream's CLOSE
      fact)
- [ ] Score-informed mode: accept a chart template (expected notes) and run
      the bounded matcher + chord validator per event, the designed
      evaluation path for polyphony/companions
- [ ] Reference-vs-learner comparison mode (output a per-feature diff),
      currently done by diffing two `--json` descriptors externally
