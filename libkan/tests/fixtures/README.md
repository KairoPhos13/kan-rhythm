# libkan test fixtures

The WAV fixtures here are **generated, not committed** (deterministic
synthesis, byte-identical on every run). Regenerate with:

```bash
./build/libkan/libkan_fixture_gen build/libkan/fixtures
```

`ctest` wires this automatically (`libkan_fixture_gen` sets up the
`kan_fixtures` CTest fixture; `libkan_loopback_test` consumes it). The
ground-truth layout (onset positions, detuned fundamentals, chord
voicings) lives in `../fixture_layout.hpp`, shared by generator and tests.

| file | content | asserted property |
|---|---|---|
| `sine_sweep.wav` | 80 → 1200 Hz log sweep, 3 s | pitch tracking follows the sweep (< 8 % error) |
| `detuned_notes.wav` | plucked A4+10c / C5−15c / E5+21c at 0.5/1.5/2.5 s | onset within 6 ms of truth, pitch within 6 Hz |
| `chords.wav` | strummed C major / C minor / C-major-wrong-fifth at 0.5/2.0/3.5 s | onsets per chord + Goertzel template verifies the major triad |
| `noise.wav` | room-level white noise (RMS ~0.002), 3 s | **zero** ghost onsets (stage-0 gate + onset floor) |
| `percussive.wav` | broadband bursts at 0.5/1.5/2.5 s | onsets fire, pitch confirmation refuses |
| `onset_grid.wav` | 440 Hz bursts every 1 s from 0.5 s | onset latency ≤ one block (ground-truth positions) |

## Using the fixtures by hand

```bash
./build/apps/kan_cli/kan_cli --loopback build/libkan/fixtures/onset_grid.wav --latency-report
./build/apps/kan_cli/kan_cli --loopback build/libkan/fixtures/detuned_notes.wav --profile plucked
```

`--loopback` replaces the microphone with the file (no audio device needed);
`--latency-report` prints source-timestamped onsets. Ground-truth latency
numbers come from `libkan_loopback_test`; the hardware half of the
≤4.5 ms gate is the loopback-cable procedure in
`docs/dsp/latency-measurements.md`.
