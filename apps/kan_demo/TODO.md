# apps/kan_demo: TODO

Live real-instrument demo for the libkan pipeline. Reference consumer of
the detection stack; gate validation stays in `apps/kan_cli`.

## Done

- [x] Live miniaudio capture @ 48 kHz / 128 samples (RT-safe callback)
- [x] Full pipeline on the consumer thread: stage 0 (HPF + adaptive noise
      gate with room calibration) → onset → profile-driven blanking →
      harmonic pitch (Cycfi Q BACF bounded to the instrument range, YIN
      fallback)
- [x] Instrument presets: ocarina, flute, recorder, guitar, piano
      (app-level pack-like data mapped onto InstrumentProfile)
- [x] Free-tuner mode: onset flash + note + Hz + cents + stability bar
- [x] `--expect <note>`: score-informed bounded-matcher confirmation
- [x] `--calibrate`: 2 s reference-note flow printing Δc (read-only)
- [x] `--selftest` offline regression test (wired into ctest)
- [x] README with can/cannot-do boundaries

## Next

- [ ] **Per-note description stream**: DONE in libkan + wiring:
      `--events <f.jsonl>` (shared schema with kan_analyze, live/offline
      identical), progressive display (provisional → final pitch;
      "polyphonic, N unresolved" instead of garbage), `--candidates`
      exercising the conditioned measurement. Fulfils §4 A/D (explicit note
      boundaries, per-note facts) with one schema. Follow-up: per-string /
      chart-bound candidate ids when `game/` supplies them.
- [ ] **BACF free-tuner lock-in**: `--engine bacf` can latch a wrong
      frequency right after onset and hold it (measured +80..+130 cents on a
      pure tone). Investigate Cycfi Q `median3`/bias warmup; until fixed the
      demo defaults to YIN. Feeds the bacf-vs-yin.md A/B work.
- [x] Wire item-5 detuning compensation: `--calibrate` now applies Δc to the
      tuner readout and `--expect` targets (`calibration.hpp`)
- [x] `--vibrato`: live technique readout (rate/depth from the cents
      trajectory), `technique.hpp` VibratoAnalyzer
- [x] `--csv <file>`: per-note event log (the descriptor feature stream)
- [ ] Per-string matrix UI (`Δc1..Δc6` guided strum flow): API ready in
      `calibration.hpp`, demo shows global only
- [ ] Live chord display once a two-handed instrument is picked up
      (validator exists in libkan; needs UI + strum-onset handling)
- [ ] CSV event log (onset time, note, cents) for offline analysis
- [x] `--input <wav>` file input (the old `--loopback` wish): the exact
      live consumer runs offline over recordings, validated on the
      Hallelujah piano take (245 attacks, 131 candidate facts from 28
      hand-verified attacks) and a real ocarina take (80/80 mono, 16
      vibrato claims); worked example in `examples/`
- [x] `--candidates <f>` (timed hypothesis lists) + `--expect` auto-supply
- [ ] Soak/CPU readout in the status line (feeds the 60-min stability gate)
- [ ] Windows/macOS smoke testing (CI is Linux-only today)

## Rules

- Real-time callback: push to the ring buffer only (CONTRIBUTING.md §3)
- Demo-only code (presets, terminal UI) stays here; detection logic lives
  in `libkan/` and must not be duplicated
