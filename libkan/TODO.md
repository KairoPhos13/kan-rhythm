# libkan — TODO

Milestone 1 component. Gates: [docs/ROADMAP.md](../docs/ROADMAP.md) · Spec: [docs/SPECIFICATION.md](../docs/SPECIFICATION.md) §2.

## Done

- [x] Lock-free SPSC ring buffer (audio thread → consumer thread)
- [x] Fast-path onset detector (HFC derivative, ~65 ms debounce)
- [x] Monophonic pitch: lightweight YIN + parabolic interpolation, cents vs A4=440
- [x] Offline ctest with synthesized +10-cent A4 (onset + pitch within ±3 Hz)
- [x] Live CLI benchmark (`apps/kan_cli`) @ 48 kHz / 128 samples
- [x] Preconditioning stage 0: 35 Hz high-pass filter + adaptive noise gate
      calibrated to room silence (`precondition.hpp`, offline ctest)
- [x] Transient blanking (stage 2): freeze pitch evaluation 8–12 ms post-onset,
      configurable per instrument profile (8 ms plucked / 15 ms wind-bowed)
- [x] Instrument profiles struct (attack ramp offset, blanking window,
      expected frequency range) — `instrument_profile.hpp`, `--profile` in kan_cli
- [x] Integrate **Cycfi Q** (MIT) BACF monophonic detector bounded to
      ±2 semitones of the score-expected note; compare accuracy/latency vs YIN
      — `bacf_detector.hpp`, `bounded_pitch.hpp` (fast confirmation stage),
      A/B results in `docs/dsp/bacf-vs-yin.md`, `--detector` in kan_cli

- [x] Integrate **KissFFT** or **PFFFT** (BSD-3) — `libkan_fft` target,
      chroma backend in `chroma_fft.hpp`
- [x] Score-informed chord validation: Goertzel bank / 12-bin chroma
      projection over expected fundamentals + first 3 harmonics, cosine
      similarity vs chart template (threshold ≈ 0.80) —
      `chord_goertzel.hpp`, `chroma_fft.hpp`, gate carried by
      `harmonic_chord.hpp` (joint harmonic LS, F1 = 1.0000), results +
      spec-deviation proposal in `docs/dsp/chord-validation.md`

## In progress / next (Milestone 1)

## In progress / next (Milestone 1)

- [x] Global detuning calibrator: measure Δc (cents) from a 2 s reference note,
      expose API to shift detector target frequencies — `calibration.hpp`
      (`DetuneCalibrator` + `TuningCompensation`), numbers in
      `docs/dsp/calibration.md`, applied live in `kan_demo --calibrate`
- [ ] Integrate **Signalsmith Stretch** (MIT) for real-time backing-track
      pitch shift of ±Δc (BPM preserved) — lives in kanfx's chain, not libkan
- [x] Per-string compensation matrix API (`Δc1..Δc6`, 3 s guided strum input)
- [x] Relative-tuning mode for fretless instruments: interval evaluation vs
      moving tonal center of gravity (EMA) — `RelativeTuningTracker`
- [x] Technique validators (platform-architecture.md §5): cents-trajectory
      tracking for bend/vibrato/slide vs expected curves; envelope-shape
      checks for staccato/legato/accent/mute; spectral signature templates
      for extended techniques (harmonics, flutter-tongue, pedal) —
      `technique.hpp` (plain-data *Spec structs for instrument packs),
      numbers in `docs/dsp/technique-validation.md`, live `--vibrato`
      readout in kan_demo
- [ ] Non-consuming detection tap API for the kanfx input bus (monitoring FX
      must never alter what judges analyze)
- [ ] Automated audio-injection test bench: feed WAV fixtures through the
      pipeline without a microphone (loopback mode)
- [ ] Latency measurement harness: loopback cable @ 48 kHz / 64 samples

## Milestone 1 release gates (all must pass)

- [ ] Onset latency (hardware → event) ≤ 4.5 ms
- [ ] Mono confirmation < 22 ms for notes > 82 Hz; < 12 ms for notes > 261 Hz
- [ ] Chord verification < 32 ms with F1 > 94% (major vs minor vs wrong-string)
      — ✅ measured 29.3 ms / F1 1.0000 (`docs/dsp/chord-validation.md`);
      remaining: track here once the judge integrates it in-game
- [ ] DSP thread CPU < 3% of one core
- [ ] Zero buffer underruns over 60 min continuous run

## Rules

- Real-time path: no allocations, no locks, no I/O (CONTRIBUTING.md §3)
- Dependencies flow toward libkan, never out of it
- Only MIT/BSD/Apache-2.0 deps (no GPL) — SPECIFICATION.md §1
