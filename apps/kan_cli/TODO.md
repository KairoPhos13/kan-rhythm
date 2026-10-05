# apps/kan_cli — TODO

Reference CLI consumer of `libkan`. Used to validate Milestone 1 gates
without any graphics engine.

## Done

- [x] miniaudio capture @ 48 kHz / 128 samples, low-latency profile
- [x] Real-time callback pushing into lock-free ring buffer (zero allocs/locks)
- [x] Live dual-path output: onset flash → pitch confirmation (note, Hz, cents)
- [x] Clean SIGINT/SIGTERM shutdown

## Next

- [ ] `--block 64` option to test the 1.33 ms buffer configuration
- [ ] `--loopback` mode: inject a WAV file instead of the microphone
      (automated testing without hardware)
- [ ] `--latency-report`: measure and print hardware-to-event onset latency
      (supports the ≤4.5 ms gate)
- [ ] `--calibrate`: 2-second reference-note flow printing the instrument's
      Δc global offset in cents
- [ ] `--profile <instrument>`: select instrument profile (plucked/wind/keyboard)
- [ ] Underrun counter + 60-minute soak mode (stability gate)
- [ ] CSV event log mode for offline analysis of detection accuracy
