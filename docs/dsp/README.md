# libkan DSP — integration & validation reference

This is the authoritative description of what `libkan` provides today: what
each API does, its contracts (units, threading, allocation), its measured
limitations, how to assemble it into the game's detection pipeline, and how
to detect when a component is misbehaving. Written for the agent that
connects libkan to `game/` and for anyone polishing the DSP further.

Status: Milestone 1 complete. All numbers quoted below are **measured
offline** by the ctest suite on this machine (reproduce: `cmake -B build &&
cmake --build build -j && ctest --test-dir build`; 18 tests). Per-topic
deep dives live alongside this file:
[bacf-vs-yin.md](bacf-vs-yin.md) · [latency-measurements.md](latency-measurements.md) ·
[chord-validation.md](chord-validation.md) · [calibration.md](calibration.md) ·
[technique-validation.md](technique-validation.md) ·
[range-coverage.md](range-coverage.md) · [performance-analysis.md](performance-analysis.md)

---

## 1. Scope and layering

`libkan` = detection & DSP core: onset, pitch (YIN / BACF / bounded
matcher), chord validation, calibration, technique measurement,
pitch-shift primitive, and the non-consuming detection tap. It must not
depend on `kanfx`, `game`, `tools`, `server`, or `charts`. Plain data in,
measurements out — instrument packs supply expectation structs; the game
judges.

### Build targets

| target | contents | dependency | option |
|---|---|---|---|
| `libkan` | all headers except the two backends below | none (header-only) | — |
| `libkan_bacf` | `bacf_detector.hpp` | Cycfi Q v1.5.0 (MIT, FetchContent) | `KAN_ENABLE_BACF=ON` |
| `libkan_fft` | `chroma_fft.hpp` | KissFFT 131.2.0 (BSD-3, FetchContent) | `KAN_ENABLE_FFT=ON` |
| `libkan_stretch` | `pitch_shift.hpp` | Signalsmith Stretch (MIT, FetchContent) | `KAN_ENABLE_STRETCH=ON` |

Link `libkan` for the core; link the others only where used. All
third-party headers are imported as SYSTEM includes; GCC's
`-Wmaybe-uninitialized` is suppressed **only** for translation units that
link `libkan_bacf` (Cycfi Q IPA diagnostics survive `-isystem`).

### Units and conventions (apply everywhere)

- Sample rate: 48 kHz nominal; APIs take `float sample_rate` explicitly.
- Frequency: Hz. MIDI numbers: A4 = 69 = 440 Hz
  (`calibration_midi_to_hz()` / `GoertzelChordValidator::midi_to_hz()`).
- Pitch deviation: **cents**, 1200·log2(f/f_ref); `PitchResult::cents_offset`
  is relative to the nearest equal-tempered note (wraps at ±50 c).
- Time: milliseconds in specs/measurements; seconds in `DetuneCalibrator`.
- Amplitude: linear float PCM in [-1, 1]; levels in dB only inside
  `NoiseGate::Config`.
- Block size: 64 or 128 samples (1.33 / 2.67 ms at 48 kHz); APIs accept any
  count, block size only affects latency quantization.

---

## 2. Component inventory

### 2.1 `ring_buffer.hpp` — transport

`LockFreeRingBuffer<T, Capacity>` (Capacity power of two). SPSC;
`push(const T*, n)` (producer, audio thread) / `pop(T*, n)` (consumer).
No allocation, no locks. Drop-on-full semantics: `push` returns false when
full; the producer decides (the capture callback ignores the flag — the
consumer keeps up in practice).

### 2.2 `precondition.hpp` — stage 0

- `HighPassFilter` — 4th-order Butterworth (two cascaded `Biquad`s),
  default 35 Hz. Zero algorithmic latency (Direct Form I, no lookahead).
  Measured: 440 Hz within −0.01 dB, 20 Hz −19.5 dB, 10 Hz −43 dB;
  first output sample responds to the first input sample.
  **Note**: 35 Hz attenuates fundamentals below ~55 Hz (bass ocarina A0
  ≈ −6 dB); construct with 20 Hz for bass-dominant input.
- `NoiseGate` — RMS gate with room-silence calibration
  (`calibrate(silence, n)`), hysteresis (`open_db` 9 / `close_db` 4 above
  the floor), smooth attack/release, slow floor adaptation while closed.
  `process_block(in, out, n)` is in-place capable and returns the open
  state.
- `Preconditioner` — HPF + gate in one object (`process_block` returns
  gate-open state). RT-safe: no allocation in any process path.

### 2.3 `instrument_profile.hpp` — instrument pack profile (plain data)

`InstrumentProfile {family, attack_type, min/max_freq_hz, blanking_ms,
judgment_offset_ms, attack_ramp_ms}` + presets `plucked()` (8 ms,
80–1400 Hz), `wind_bowed()` (15 ms, 190–2700 Hz), `keyboard()` (6 ms,
27–4300 Hz), `from_name()`. Fields mirror the instrument-pack schema
(`charts/spec` will serialize exactly these; no format coupling here).
**Contract**: `blanking_ms` must cover the instrument's attack transient
(measured: profile/fixture mismatch directly degrades accuracy — 8 ms pick
transient needs ≥ 8 ms blanking).

### 2.4 `dsp_detector.hpp` — onset + blanking + YIN pitch

- `HybridDetector::process_onset_block(const float*, n)` → bool. HFC
  energy-derivative jump vs EMA (×3.2), 25-block (~65 ms) debounce.
  Measured: fires on the first block containing the energy rise
  (+1.33 ms block-quantized on fixtures; noise → zero ghost onsets; smooth
  FX tails do not retrigger; **recall 8/8 hand-verified attacks on the real
  piano take**, jump ratios of real soft attacks measured as low as ×3.3).
  Note: a phase-locked sum of equal decaying notes beats coherently and its
  crests cross ×3.2 (synthetic-only artifact — real chord tails measured
  clean, see §6.3); fixtures synthesize realistic voicing, phases, hammer
  spread and a room noise floor. The note-boundary layer latches retriggers
  (§3 rule 1).
- `set_noise_floor(energy)` / `noise_floor()` — share the gate's calibrated
  floor (energy = mean square).
- `set_profile()` / `profile()` + `in_transient_blanking(ms_after_onset)`
  — stage-2 blanking (freeze pitch evaluation during the attack).
- `detect_pitch(const std::vector<float>& window)` → `PitchResult` — YIN
  (CMNDF + parabolic interpolation), search bounded by the profile range,
  further narrowed by `set_expected_note(hz, ±2 st)` (score-informed;
  eliminates octave errors). Measured accuracy: 88/88 piano keys within
  10 cents when the window spans ≥ 2 periods; ~7 cents average on a
  2048-window suite. `set_periodicity_threshold()` tunes the CMNDF dip
  (0.15 default; raise for bright/bass-attenuated spectra).
  **Window rule**: `window ≥ 2 periods` of the lowest expected frequency
  (blind YIN: 2048 covers ≥ ~95 Hz; below that grow the window —
  range-coverage.md §3).
- **Threading/allocations**: `detect_pitch` allocates a CMNDF buffer per
  call and `PitchResult::note_name` is a `std::string` — consumer thread
  only. `process_onset_block` and the blanking query are allocation-free.

### 2.5 `bounded_pitch.hpp` — fast score-informed confirmation

`BoundedPitchMatcher`: `set_expected_note(hz, ±2 st)` bounds an NSDF
(normalized autocorrelation) lag search; `window_samples()` = longest
candidate lag + overlap (half a period, clamped 64–512); `match(clean
window)` → `PitchResult` with parabolic sub-sample lag. Role: the game's
per-note confirmation ("was the charted note played, within how many
cents"). Measured: sub-cent average across the 10-note bench; semitone
identity correct on all 88 piano keys; confirmation latency = blanking +
window (10.7 ms at C8 … 24 ms at E2, 64-sample blocks). Requirements:
**clean window** (post-blanking; deterministic transients corrupt the
correlation — documented failure mode in the header), `count ≥
window_samples()`. Analysis loop is allocation-free; returning a
`PitchResult` allocates `note_name` once (consumer thread).

### 2.6 `bacf_detector.hpp` — Cycfi Q bitstream autocorrelation (backend)

`BacfDetector` (links `libkan_bacf`): streaming per-block
`process_block()`, score-informed via `set_expected_note(hz, ±2 st)` /
`set_range(lo, hi)` (needed above 1200 Hz — e.g. ocarina F6 1397 Hz),
`reset()` on each new note, `set_confirm_frames()` (default 2),
`periodicity()`, `predict_frequency()`. Measured: 1.7 cents average
accuracy (best of all detectors), ~1 µs/block CPU. **Known defect**
(bacf-vs-yin.md): in blind free-tuning it can latch a wrong frequency at
note onset (+80…+130 cents) and hold it; use it as the steady-tracking /
confirmation engine with an expected note, never as the sole blind tuner.

### 2.7 Chord validation (score-informed, polyphonic)

- `harmonic_chord.hpp` — **`HarmonicChordValidator`, the gate carrier**:
  joint weighted least squares over harmonic atoms (cos/sin pairs at the
  fundamental + 5 harmonics per expected note), iterative one-vs-rest
  frequency refinement (±30 c, absorbs per-string detuning), IRLS
  transient down-weighting. Output: `similarity` = explained-energy ratio
  (cᵀb / xᵀWx), `note_presence[]`, `valid` = similarity ≥ threshold
  (0.80 default) **and** every expected note ≥ presence floor (0.45/n).
  `refined_cents(k)` returns per-note detuning. Measured: **macro-F1
  1.0000** on major/minor/one-wrong-string (40 trials, C3–A4), 29.3 ms
  verification (8 ms blanking + 1024-sample window), true chords 0.94+
  similarity. Cost (measured, Release, this machine): the conditioned fit
  runs **~11 ms** for 6 candidates on 2048 samples (AVX2 normal equations +
  atom-major residual loops — the old scalar/strided form measured 37 ms;
  ~quadratic in candidate count, ~linear in window). Call per chord event /
  scheduled measurement, never per block; the note stream schedules them
  (`note-description-stream.md` §8 records the resource budget). Window
  must not exceed the constructor's `max_window`.
- `chord_goertzel.hpp` — marginal Goertzel slot bank (fundamental + first
  3 harmonics per note): verifies correct chords (40/40 at threshold),
  but marginal magnitudes cannot separate semitones within 21 ms windows
  (measured F1 0.71) — keep for extended-technique slot templates, not
  for classification.
- `chroma_fft.hpp` — KissFFT 12-bin chroma (`ChromaProjector`,
  `ChromaChordValidator`): octave-invariant templates; at gate windows it
  accepts few true chords (2/24) but rejects confusions outright (0/72).
  Secondary machinery for instrument-pack templates.
- **Boundary**: blind polyphonic transcription is out of scope by design
  (SPEC §2: no blind AMT). Polyphonic material needs the chart template;
  otherwise flag and exclude (see §5).

### 2.8 `calibration.hpp` — dynamic consonance (detector side)

- `DetuneCalibrator`: collect `add_frequency(hz, t_seconds)` for
  `finish()` → `DetuneMeasurement{delta_cents, measured_hz, standard_hz,
  standard_midi, spread_cents}`; guided (`finish(expected_midi)`) or
  nearest-ET inference. Median + 1.4826·MAD, fixed 512-reading storage,
  no allocation. Measured error 0.05 cents on a +12 c reference.
- `TuningCompensation`: `global_cents` + per-string `string_cents[6]`
  (Δc1..Δc6); `correction_cents(string_index)`,
  `target_frequency/midi(standard, string)` (detector targets),
  `residual_cents(measured, standard, string)` (judgment readout).
  Semantics: targets shift **by** the instrument's detune, so playing in
  tune with oneself is judged in tune while scoring stays strict. The
  same correction must be applied to the backing track
  (`PitchShifter::set_detune_cents`) and consumed by the judge.
- `RelativeTuningTracker`: fretless/bowed/voice mode —
  `observe(expected_midi, measured_midi)` returns
  `IntervalResult{interval_ok, error_cents, delta_interval_cents,
  center_cents}`; the EMA tonal center absorbs slow detune drift and is
  **not** updated by notes outside `center_capture_cents` (a wrong note
  cannot drag the reference). Config: `ema_alpha`, `interval_tolerance_cents`,
  `center_capture_cents`.

### 2.9 `technique.hpp` — how the note is played

Measurement classes (consumer thread; fixed-capacity buffers; the analyze
loops allocate nothing) + plain-data `*Spec` structs (instrument-pack
fields):

| measurement | spec | verdict semantics |
|---|---|---|
| `VibratoAnalyzer::measure(TrajectoryBuffer)` → rate/depth/center/periodic | `VibratoSpec{rate band, depth band, min_duration, expect_straight, straight_tolerance}` | `check()`; `periodic` requires **≥3 oscillation cycles** (rate physics); straight-tone passages judged via `expect_straight` |
| `BendAnalyzer::measure()` → endpoints/duration/monotonicity/fit error | `BendSpec{start/end cents, duration, CurveShape::Linear\|EaseOut, tolerances}` | shape-fit RMS ≤ `fit_tolerance_cents` |
| `EnvelopeAnalyzer::measure(rms, n, hop_ms)` → duration/attack/release/sustain ratio/peak-to-−20 dB decay | `EnvelopeSpec{Articulation::Staccato\|Legato\|Accent\|Mute, thresholds}` | per-articulation boolean bands |
| `DynamicsClassifier::observe(attack_peak)` → `DynamicsTier` (warming-up / pp..ff) | `Config{min_notes, floor_db}` + optional `calibrate(soft, loud)` anchors | session-relative ranking (converging) or calibrated extremes; "warming up" until `min_notes` (technique-validation.md) |
| `SpectralSignatureValidator::measure(window)` → slot amplitudes + cosine similarity | `SpectralSignature{slot_hz[], weight[], threshold}` | similarity ≥ threshold (harmonic templates: 1.000 vs 0.558 measured) |

`TrajectoryBuffer` holds `TrajectoryPoint{t_ms, cents}` (512 max). Feed
**continuous** deviation from a fixed per-note reference; nearest-note
cents wraps at ±50 and destroys vibrato analysis (measured failure).

### 2.10 `tap.hpp` — non-consuming detection tap (kanfx boundary)

`TapBuffer<T, BlockSize, NumBlocks>`: `publish()` (audio thread, fixed
storage, single atomic, no allocation/locks) and independent `Reader`
cursors with `next(TapFrame&)` — observing the stream cannot affect other
consumers (contract test: two readers both see all blocks; input buffers
byte-identical after detection). Overwrite semantics with per-reader
`dropped()` accounting: freshness over completeness, the bus never stalls.
`TapFrame::samples` is `const float*` and valid until the writer wraps
(NumBlocks blocks) — analyze in the same consumer pass. This is the
attachment point for the judgment taps of platform-architecture §3.

### 2.11 `pitch_shift.hpp` — backing-track pitch shift (kanfx chain)

`PitchShifter` (links `libkan_stretch`, Signalsmith Stretch): mono
`process(in, in_count, out, out_count)` (equal counts = consonance shift
at natural speed; unequal = time-stretch at fixed pitch),
`set_detune_cents(Δc)`, `reset()`, `input_latency()/output_latency()`.
Measured: +50 c shift → 458.8 Hz (target 452.9), 1.5× stretch keeps pitch
within 17 c. Construct on the setup path; the bus wiring (backing chain,
ducker, scheduling) is kanfx's.

### 2.12 Test-side helpers (`libkan/tests/`, not shipped API)

`test_signals.hpp` (deterministic synthesis), `chord_suite.hpp`,
`wav_io.hpp`, `fixture_layout.hpp` + `fixture_gen` (generated WAV fixture
set with ground-truth onset positions), `harmonic_probe.cpp` +
`harmonic_probe_notes.hpp` (evidence probe for candidate-conditioned chord
validation on real recordings — `--dump` / `--measure` / `--bench`; the
Phase-1a experiment behind
[architecture/note-description-stream.md](../architecture/note-description-stream.md)
§2). Apps must not include these.

### 2.13 `note_stream.hpp` — the per-note description stream

"Register by note": each attack opens a record and facts stream as they
become available — `HIT` (fixed at the onset, never revised), `PITCH`
(provisional → one final estimate or `unresolved`), `CONTENT` (blind
mono/polyphonic class + per-candidate presence/cents/amplitude when the
caller supplies hypotheses, including notes still sustaining), `EXPRESSION`
(envelope, vibrato, level), `CLOSE` (decay-relative end). `NoteEvent` is a
fixed-size POD with audio-true flags only (MIX/HOP/POLYPHONIC/DOMINANT/
UNEXPLAINED_ENERGY/LOW_REGISTER/SEARCH_SATURATED) — the stream never emits
numbers the tool distrusts. `NoteEventStream` is lock-free SPSC with a
visible drop counter; `NoteDescriber` splits the work into an
allocation-free `process_block` fast path and scheduled `finalize_due()`
long-window fits (an app may run the latter on a worker thread). Includes
the JSONL schema-v2 writer and the blind `classify_content()` (Goertzel
series counting). Records are per STRUCK NOTE (an attack striking N notes
opens N records with their own lifetimes); HIT carries the record's
hypothesis identity and strike group; wind/bowed profiles also open records
on sustained pitch changes (`TRANSITION`). Design, measured evidence and
the fairness rule: [note-description-stream.md](../architecture/
note-description-stream.md); tests: `libkan/tests/note_stream_test.cpp`
(ten fixtures).

---

## 3. Assembling the game pipeline

Canonical order (SPEC §2 dual-path); all analysis on the **consumer
thread** fed by `LockFreeRingBuffer` from the audio callback:

```cpp
// setup thread:
kan::InstrumentProfile prof = pack.profile;             // from instrument pack
kan::TuningCompensation comp = calibration_result;      // from --calibrate flow
kan::Preconditioner pre{48000.0f};
kan::HybridDetector det{48000.0f};
det.set_profile(prof);
det.set_noise_floor(pre.gate().noise_floor_rms() * pre.gate().noise_floor_rms());
kan::BoundedPitchMatcher confirm{48000.0f};             // per chart note:
confirm.set_expected_note(comp.target_frequency(chart_hz, string_index));
// confirm.window_samples() tells you how much clean audio to collect.

// consumer thread, per block:
pre.process_block(in, out, n);                          // stage 0
if (det.process_onset_block(out, n)) { /* ONSET -> judge timing NOW */ }
float ms = ms_since_onset();
if (!det.in_transient_blanking(ms)) {                   // stage 2
    collect_clean_window(out, n);                       // post-blanking only
    if (clean.size() >= confirm.window_samples()) {
        auto p = confirm.match(clean.data(), clean.size()); // stage 3
        if (p.valid) judge_note(p, comp.residual_cents(p.frequency_hz, chart_hz, string_index));
    }
    // sustain/technique: feed TrajectoryBuffer + BacfDetector here
}
```

Rules that matter (each prevents a measured failure):

1. **Latch the first onset per note.** Decaying notes retrigger the onset
   detector; restarting analysis on the retrigger destroys confirmations
   (made YIN appear to miss 26/88 keys until fixed).
2. **Clean windows only** for correlation/matching; keep transients out
   (deterministic broadband bursts beat true peaks on short windows).
3. **Window ≥ 2 periods** of the lowest expected note for YIN; the bounded
   matcher sizes itself via `window_samples()`.
4. **Blanking must cover the attack** (profile ↔ instrument physics).
5. **Shift targets with `TuningCompensation`** and judge with
   `residual_cents()`; feed the same `correction_cents()` to the backing
   shifter.
6. **Chords use `HarmonicChordValidator` with the chart template** at the
   same window (8 ms blanking + 1024 samples); call per chord event.
7. **Technique analysis** runs on the per-block trajectory after the note
   closes (vibrato needs ≥ 3 oscillation cycles to be claimed).
8. `PitchResult::note_name` allocates — detectors stay off the audio thread.

---

## 4. Level seeding: the performance descriptor

`apps/kan_analyze` (CLI) turns audio into the learner-evaluation feature
set; `apps/kan_demo --csv` produces the same event stream live. JSON
schema (`--json` / `--json-out`):

```json
{ "source", "duration_s", "notes", "mixed_regions", "tempo_bpm",
  "intonation_mean_abs_cents", "intonation_max_abs_cents",
  "events": [ { "onset_ms", "midi", "hz", "cents", "dur_ms",
                "attack_ms", "sustain", "vibrato", "vib_hz",
                "valid_ratio", "reliable" } ] }
```

Semantics: each event's `midi` / `hz` / `cents` are **one consistent
estimate** — `midi` is the fixed per-note reference (nearest
equal-tempered note of the first reading), `cents` is the mean deviation
from that reference over the note (no ±50 wrap), and `hz` is derived from
the (midi, cents) pair; the three fields always describe the same reading
of the note. When the tool refuses to evaluate an event (`MIX`: pitch validity <
0.5 = polyphonic/overlapped; `HOP`: tracker hopping between sources) or
has no pitch reading at all, `midi`/`hz`/`cents` are **null** and the text
table prints `-` — **filter on `reliable` before trusting any event**.
Level seed = `events`;
quality bar = the summary fields. Reference-vs-learner comparison =
per-feature diff of two descriptors (timing = onset deltas, intonation =
cents, technique = vibrato/envelope match) — the comparison layer is
`game/`'s.

`apps/kan_analyze/run_reference_set.sh` runs a whole folder of
recordings, auto-selecting profiles by filename (`*piano*`, `*ocarina*`,
`*xun*`, …); `--demo` regenerates the audible synthesized baselines.
All artifacts live under `test_results/` (gitignored).

---

## 5. Limitations (measured — treat as contracts, not TODOs)

| # | limitation | measured evidence | consequence for integration |
|---|---|---|---|
| L1 | Confirmation latency floor = blanking + ~1 period + overlap | A0: 64–88 ms; ≤24 ms from A2 up (range-coverage.md) | bass confirm lags; the onset (rhythm reward) is instant everywhere |
| L2 | Blind mono analysis cannot separate polyphony/companions | piano: 35/265 evaluable; xun duet: 33/162 (performance-analysis.md) | chords/duets need chart templates; otherwise MIX/HOP-flag and exclude |
| L3 | Fast-estimate cents are confirmation-grade | sparse spectra → 30–50 c scatter below ~200 Hz; semitone identity always correct | use BACF/YIN for intonation scoring |
| L4 | YIN needs ≥2-period windows | blind 2048 fails below ~47 Hz; marginal below ~95 Hz | grow windows or use the bounded matcher |
| L5 | BACF free-tuner onset lock-in | +80…+130 c hold on pure tones (bacf-vs-yin.md) | BACF with expected notes / steady tracking only |
| L6 | Correlation windows must exclude transients | deterministic noise × tone interference beats true peaks | stage-2 blanking is mandatory before matching |
| L7 | FX tails inflate durations; no ghost onsets | delay/reverb fixture: 0 ghost onsets, durations 900 vs 400 ms | duration-based articulation needs decay-relative ends (follow-up) |
| L8 | Vibrato claims need ≥3 cycles | rate physics (technique-validation.md) | short notes: app-level fallback (2+ cycles) if desired |
| L9 | Trajectories must not wrap | ±50 c nearest-note wrap destroys vibrato | track continuous deviation from a fixed reference |
| L10 | Chord validation is score-informed only | blind F1 0.71 marginal / 0.58 chroma at gate windows | never expect blind chord classification |
| L11 | Recording-level detune is not normalized in descriptors | real ocarina songs read 40–70 c mean vs ET | report/normalize via Δc before judging intonation of recordings |
| L12 | 35 Hz HPF attenuates sub-55 Hz fundamentals | A0 ≈ −6 dB | use 20 Hz HPF for bass-dominant sources |

Hardware-gated, not yet measured: end-to-end onset latency (loopback-cable
procedure in latency-measurements.md) and 60-min underrun soak.

---

## 6. Validation & bug detection — how to catch a tool that lies

Three layers, all present and runnable today:

### 6.1 Ground-truth oracles (the ctest suite)

Every detector change must ship a test with **known input → expected
output**. The suite (20 tests) and its oracles:

| test | oracle (known truth) | asserts |
|---|---|---|
| `libkan_dsp_test` | synthesized +10 c A4 | ring integrity, onset, pitch ±3 Hz |
| `libkan_precondition_test` | pure tones + noise | HPF attenuation bands, zero-latency impulse, gate open/close, ghost suppression |
| `libkan_blanking_test` | plucked note with 8 ms pick transient | blanking windows per profile, transient-only rejection, range bounding |
| `libkan_bacf_test` | detuned notes, octave/off-band, noise | confirmation inside ±2 st, out-of-band rejection |
| `libkan_bacf_bench` | 10-note suite (E2–A5) | latency/accuracy/CPU baselines per detector |
| `libkan_chord_test` | 40 strummed triads (C3–A4, ±15 c detune) | **F1 > 0.94** classification, threshold separation, 29.3 ms budget |
| `libkan_chroma_test` | pure note + triads | pitch-class peak, secondary bounds |
| `libkan_calibration_test` | +12 c reference, matrix, ±10 c player | Δc error < 3 c, target-shift residual, EMA behavior |
| `libkan_technique_test` | FM vibrato, bend curves, envelope shapes, spectral mixes | measured vs truth per technique |
| `libkan_tap_test` | published blocks | non-consuming readers, drop accounting, const-input contract |
| `libkan_range_test` | **all 88 piano keys** | onset 88/88, semitone identity 88/88, YIN < 10 c |
| `libkan_stretch_test` | known shifts/stretch | 452.9 Hz target, pitch preserved under stretch |
| `libkan_loopback_test` | WAV fixtures with **known onset sample positions** (fixture_layout.hpp) | zero ghost onsets on noise, no pitch on percussion, notes within 6 ms/6 Hz, chord template pass, **onset latency ≤ 1 block** |
| `kan_demo_selftest` / `kan_analyze_selftest` | synthesized ocarina note | full app pipelines |
| `kan_analyze_hop_test` | synthesized **hopping note** (one attack, two competing sources swapping dominance) | each row = one estimate (name/Hz/cents verify each other), MIX/HOP rows print `-` / `null` pitch cells |
| `libkan_note_stream_test` | six synthesized fixtures (mono corrected-later, chord all-present, chord one-absent, sustained LH under RH, unexplained extra, live-vs-offline) | per-note facts: HIT fixed, provisional → final one estimate, presence/cents/amplitude per candidate with similarity, `UNEXPLAINED_ENERGY` flagged, byte-identical JSONL |

The fixture set (`libkan_fixture_gen`, deterministic synthesis — byte
identical across runs) is the ground-truth corpus: sine sweep, detuned
notes at known times, major/minor/wrong-fifth chords, room noise (must
produce zero onsets), percussive bursts (onsets without pitch), onset grid
(latency truth).

### 6.2 Invariants (properties that must hold regardless of input)

Encode any new bug as one of these, not as a spot-value:

- The matcher never reports outside ±2 st of `set_expected_note` (edge
  rejection is built in — an "impossible" report is a regression).
- `TuningCompensation::residual_cents(target_frequency(f)) == 0`.
- Reading a tap cannot change what another consumer sees (const input).
- Noise input → zero onsets; percussive input → zero pitch confirmations.
- Detector output for identical input is deterministic (no time/state
  leakage; latch-first-onset rule).
- Descriptor events with `reliable == false` never contribute to summary
  statistics.
- One event's note name, Hz and cents are one estimate: Hz must equal
  440·2^((midi−69)/12 + cents/1200) implied by the other two columns, and
  events without a trusted estimate (MIX/HOP or no reading) print no pitch
  numbers at all (`-` in the table, `null` in JSON) —
  `kan_analyze --selftest` checks this on a synthesized hopping note.

### 6.3 Failure signatures (what "lying" looks like)

Learned from real bugs this project hit — check these first when outputs
look wrong:

| signature | probable cause | precedent |
|---|---|---|
| +1200/−1200 c errors, "note" name ≠ Hz | octave grab (polyphony / subharmonic) | piano scenario pre-fix |
| ±100 c staircase in a trajectory | nearest-note cents wrap | vibrato analysis pre-fix |
| first readings 30–50 c off, then settling | transient inside the analysis window | matcher pre-fix |
| spurious vibrato > 15 Hz, huge depth | FM synthesis bug or tracker hop on MIX material | duet scenario pre-fix (t·vib(t) synthesis) |
| huge confirm latencies on one note only | onset retrigger reset the clean window | range_test pre-fix |
| a whole octave band missing from YIN | lag range truncated (top octave) or window < 2 periods (bass) | C8 pre-fix |
| durations ≈ note+delay time | FX tail counted as sustain | fx scenario (documented, L7) |
| notes "evaluated" but source is polyphonic | reliability filter bypassed | always filter on `reliable` |
| phantom notes on decaying chords | phase-locked sums of equal decaying notes beat coherently and their crests cross ×3.2 (synthetic-only: real chord tails measured clean — 8/8 verified attacks caught, a 3.3 s decaying tail with zero ghosts) | dsp_test ghost-onset regression (fixtures must synthesize realistic voicing/phases/hammer spread/noise floor) |

When a new signature appears: reproduce with the smallest synthesized
signal, add it to `test_signals.hpp`/`fixture_layout.hpp`, encode the
expected behavior as an assertion in the matching ctest, then fix.

### 6.4 Baselines and regression discipline

The measured tables in [bacf-vs-yin.md](bacf-vs-yin.md),
[range-coverage.md](range-coverage.md), [chord-validation.md](chord-validation.md),
[latency-measurements.md](latency-measurements.md) and this file are the
baselines. Tests assert derived bounds (slightly looser than the measured
values to absorb scheduling noise). A change that moves a baseline must
update the corresponding doc table in the same commit — the numbers are
contract, not commentary.

### 6.5 Field validation on real audio

`kan_analyze` self-reports uncertainty (`MIX`/`HOP`, `valid_ratio`) — on
real recordings these flags ARE the bug detectors for hard cases. For
quantified validation against known performances, diff the descriptor of
a reference recording against a learner's `kan_demo --csv` log; residual
differences beyond the recorded baseline indicate tool regressions.

---

## 7. Document index

| file | contents |
|---|---|
| `README.md` (this) | inventory, contracts, integration, validation |
| `bacf-vs-yin.md` | detector A/B: accuracy/latency/CPU, selection rationale |
| `latency-measurements.md` | gate-by-gate measured vs theoretical, loopback procedure |
| `chord-validation.md` | chord backends, F1 evidence, spec-deviation proposal |
| `calibration.md` | Δc calibrator, compensation matrix, relative tuning |
| `technique-validation.md` | vibrato/bend/articulation/spectral validators |
| `range-coverage.md` | A0–C8 sweep, physics walls |
| `performance-analysis.md` | descriptor, real-song runs, corner cases |
| `../architecture/note-description-stream.md` | per-note description stream: Phase-1a real-piano evidence + design proposal (NoteEvent, candidates, JSONL) |

App-level development context: [apps/kan_demo/DEVELOPMENT.md](../apps/kan_demo/DEVELOPMENT.md)
(live demo design direction and polish rules) ·
[apps/kan_analyze/README.md](../apps/kan_analyze/README.md) (descriptor tool).

See also: [`../architecture/measurement-invariants.md`](../architecture/measurement-invariants.md)
— the measured invariants and accepted tradeoffs (onset threshold, judging
gates, duration bias, resource budget) that any DSP change must respect or
deliberately re-negotiate via its change checklist.
