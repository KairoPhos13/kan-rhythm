# apps/kan_cli — TODO

Reference CLI consumer of `libkan`. Used to validate Milestone 1 gates
without any graphics engine.

## Done

- [x] miniaudio capture @ 48 kHz / 128 samples, low-latency profile
- [x] Real-time callback pushing into lock-free ring buffer (zero allocs/locks)
- [x] Live dual-path output: onset flash → pitch confirmation (note, Hz, cents)
- [x] Clean SIGINT/SIGTERM shutdown
- [x] Stage-0 preconditioning on the consumer thread (35 Hz HPF + adaptive
      noise gate, `--noise-cal <ms>`, `--no-gate`)
- [x] `--profile <instrument>`: select instrument profile (plucked/wind/keyboard);
      stage-2 transient blanking driven by the profile
- [x] `--detector <yin|bacf>`: harmonic-path detector selection (Cycfi Q BACF
      blind mode vs rolling-window YIN)

## Next

- [ ] `--block 64` option to test the 1.33 ms buffer configuration
- [x] `--loopback` mode: inject a WAV file instead of the microphone
      (automated testing without hardware)
- [x] `--latency-report`: print onset source timestamps + block-quantization
      summary (ground truth in libkan_loopback_test; hardware half follows
      the cable procedure in docs/dsp/latency-measurements.md)
- [ ] `--calibrate`: 2-second reference-note flow printing the instrument's
      Δc global offset in cents
- [ ] Underrun counter + 60-minute soak mode (stability gate)
- [ ] CSV event log mode for offline analysis of detection accuracy
