# libkan — TODO

Milestone 1 component. Gates: [docs/ROADMAP.md](../docs/ROADMAP.md) · Spec: [docs/SPECIFICATION.md](../docs/SPECIFICATION.md) §2.

## Done

- [x] Lock-free SPSC ring buffer (audio thread → consumer thread)
- [x] Fast-path onset detector (HFC derivative, ~65 ms debounce)
- [x] Monophonic pitch: lightweight YIN + parabolic interpolation, cents vs A4=440
- [x] Offline ctest with synthesized +10-cent A4 (onset + pitch within ±3 Hz)
- [x] Live CLI benchmark (`apps/kan_cli`) @ 48 kHz / 128 samples

## In progress / next (Milestone 1)

- [ ] Preconditioning stage 0: 35 Hz high-pass filter + adaptive noise gate
      calibrated to room silence
- [ ] Transient blanking (stage 2): freeze pitch evaluation 8–12 ms post-onset,
      configurable per instrument profile (8 ms plucked / 15 ms wind-bowed)
- [ ] Instrument profiles struct (attack ramp offset, blanking window,
      expected frequency range)
- [ ] Integrate **Cycfi Q** (MIT) BACF monophonic detector bounded to
      ±2 semitones of the score-expected note; compare accuracy/latency vs YIN
- [ ] Integrate **KissFFT** or **PFFFT** (BSD-3)
- [ ] Score-informed chord validation: Goertzel bank / 12-bin chroma
      projection over expected fundamentals + first 3 harmonics, cosine
      similarity vs chart template (threshold ≈ 0.80)
- [ ] Global detuning calibrator: measure Δc (cents) from a 2 s reference note,
      expose API to shift detector target frequencies
- [ ] Integrate **Signalsmith Stretch** (MIT) for real-time backing-track
      pitch shift of ±Δc (BPM preserved)
- [ ] Per-string compensation matrix API (`Δc1..Δc6`, 3 s guided strum input)
- [ ] Relative-tuning mode for fretless instruments: interval evaluation vs
      moving tonal center of gravity (EMA)
- [ ] Automated audio-injection test bench: feed WAV fixtures through the
      pipeline without a microphone (loopback mode)
- [ ] Latency measurement harness: loopback cable @ 48 kHz / 64 samples

## Milestone 1 release gates (all must pass)

- [ ] Onset latency (hardware → event) ≤ 4.5 ms
- [ ] Mono confirmation < 22 ms for notes > 82 Hz; < 12 ms for notes > 261 Hz
- [ ] Chord verification < 32 ms with F1 > 94% (major vs minor vs wrong-string)
- [ ] DSP thread CPU < 3% of one core
- [ ] Zero buffer underruns over 60 min continuous run

## Rules

- Real-time path: no allocations, no locks, no I/O (CONTRIBUTING.md §3)
- Dependencies flow toward libkan, never out of it
- Only MIT/BSD/Apache-2.0 deps (no GPL) — SPECIFICATION.md §1
