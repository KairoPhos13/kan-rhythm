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

- [x] **Per-note description stream** ("register by note"): `note_stream.hpp`
      (`NoteEvent` POD — HIT / PITCH provisional|final|unresolved / CONTENT /
      EXPRESSION / CLOSE, candidate-pitch input API incl. still-sustaining
      notes, SPSC event stream + JSONL v1, live/offline equivalence),
      six fixtures in `note_stream_test.cpp`, dynamics tiers pp..ff
      (`DynamicsClassifier`, session-relative + calibrated) — wired into
      `kan_analyze --events` and `kan_demo --events/--candidates`.
      Design + measured evidence: `docs/architecture/note-description-stream.md`
- [ ] Per-note ends in dense polyphony: the decay-relative CLOSE rule is
      mix-relative, so merged legato notes close together (and lose their
      vibrato claim); needs the candidate-conditioned amplitude trajectory
- [x] Full instrument-range coverage (complete piano range A0–C8 incl.
      bass ocarinas/bass guitars): all 88 keys detected + confirmed with
      correct semitone identity; YIN sustains within 10 cents; physics
      walls documented in `docs/dsp/range-coverage.md`
- [x] Global detuning calibrator: measure Δc (cents) from a 2 s reference note,
      expose API to shift detector target frequencies — `calibration.hpp`
      (`DetuneCalibrator` + `TuningCompensation`), numbers in
      `docs/dsp/calibration.md`, applied live in `kan_demo --calibrate`
- [x] Integrate **Signalsmith Stretch** (MIT) for real-time backing-track
      pitch shift of ±Δc (BPM preserved) — `pitch_shift.hpp`
      (`PitchShifter` via `libkan_stretch`), measured: +50 c shift lands
      458.8/452.9 Hz, 1.5x time-stretch preserves pitch within 17 c;
      the bus wiring belongs to kanfx's backing chain
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
- [x] Non-consuming detection tap API for the kanfx input bus (monitoring FX
      must never alter what judges analyze) — `tap.hpp`
- [x] Automated audio-injection test bench: feed WAV fixtures through the
      pipeline without a microphone (loopback mode) — `wav_io.hpp` +
      deterministic `fixture_gen` + `libkan_loopback_test` (CTest fixture
      ordering), `kan_cli --loopback <wav>`
- [x] Latency measurement harness: ground-truth onset latency asserted in
      `libkan_loopback_test` (+1.33 ms file-based) and reported by
      `kan_cli --latency-report`; loopback-cable procedure documented in
      `docs/dsp/latency-measurements.md` (hardware measurement pending
      cable rig)
- [x] Score-informed chord validation on **real polyphonic piano material**
      (Phase 1a evidence for the note stream): test-side probe
      `tests/harmonic_probe.cpp` (`--dump`/`--measure`/`--bench`) + 28
      hand-annotated attacks with negative controls
      (`tests/harmonic_probe_notes.hpp`). Measured: presence separates
      sounding from absent inside a complete candidate set (med 0.27 vs
      0.01), 170 ms windows halve cents error (12.0c -> 6.0c; bass <C3
      22.4c -> 13.9c), presence without similarity is meaningless, 7-8
      sounding notes bias 6-modeled shares, blind mono/poly classifier
      26/28 vs MIX/HOP 20/28, validate() cost 10.8/34.8/280 ms (n=3/6 at
      1024, n=6 at 8192). Numbers + design: `docs/architecture/
      note-description-stream.md` section 2

## Milestone 1 release gates (status: measured vs physics vs hardware)

- [ ] Onset latency (hardware → event) ≤ 4.5 ms
      — offline half measured +1.33 ms block-quantized (loopback bench);
      end-to-end needs the loopback-cable rig [H]
- [ ] Mono confirmation < 22 ms for notes > 82 Hz; < 12 ms for notes > 261 Hz
      — game-feel validated over the full piano range (range-coverage.md);
      the strict band numbers sit at the period-limited physics floor
      (~1 period + blanking) — documented, not chased
- [x] Chord verification < 32 ms with F1 > 94% (major vs minor vs wrong-string)
      — measured 29.3 ms / F1 1.0000 (`docs/dsp/chord-validation.md`)
- [x] DSP thread CPU < 3% of one core — measured < 0.1% (all stages,
      `docs/dsp/latency-measurements.md`)
- [ ] Zero buffer underruns over 60 min continuous run [H] — needs the
      soak on real hardware; offline loopback bench covers pipeline
      integrity

## Rules

- Real-time path: no allocations, no locks, no I/O (CONTRIBUTING.md §3)
- Dependencies flow toward libkan, never out of it
- Only MIT/BSD/Apache-2.0 deps (no GPL) — SPECIFICATION.md §1
