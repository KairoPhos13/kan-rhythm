# apps/kan_analyze — TODO

Song performance descriptor: per-note events + song features = level-seed
material and learner-evaluation rubric. See `docs/dsp/performance-analysis.md`.

## Done

- [x] WAV/MP3/FLAC input (miniaudio decoder) + `--synth` offline scenarios
- [x] Full pipeline analysis: stage 0 → onset → blanking → blind pitch →
      per-note records (time, pitch, cents, envelope, vibrato)
- [x] Song features: tempo (median IOI), intonation stats, vibrato usage,
      staccato count, range
- [x] Reliability flags: MIX (polyphony/overlap) + HOP (tracker hopping) —
      excluded from statistics, honestly reported
- [x] `--json` machine-readable output (level seeding)
- [x] Corner-case scenarios validated: mono wind, polyphonic piano, duet
      with companion, FX tails (zero ghost onsets)

## Next

- [ ] Decay-relative note-end rule (FX tails currently inflate durations;
      envelope decay metrics already reported)
- [ ] Score-informed mode: accept a chart template (expected notes) and run
      the bounded matcher + chord validator per event — the designed
      evaluation path for polyphony/companions
- [ ] Reference-vs-learner comparison mode (output a per-feature diff) —
      currently done by diffing two `--json` descriptors externally
