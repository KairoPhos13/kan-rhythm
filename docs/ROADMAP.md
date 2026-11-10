# kan! Engineering Roadmap

No milestone is declared done by subjective feel: every phase has hard
engineering metrics (**Release Gates**) that must pass before moving on.
Technical details in [SPECIFICATION.md](SPECIFICATION.md).

---

## Milestone 1: C++ DSP core & zero-latency proof (CLI, no graphics)
**Months 1-2** · Component: `libkan/` (+ `apps/kan_cli/`)

Build the C++ library integrating miniaudio, Cycfi Q (BACF), KissFFT, and
Signalsmith Stretch, plus an automated audio-injection test bench.

**Deliverables**
- Dual-path pipeline (onset + monophonic/polyphonic pitch)
- Global detuning calibrator (Δc cents) with backing-track pitch shift
- Offline test bench with synthesized signals (no microphone needed)

**Release Gates**
| Metric | Target |
|---|---|
| Onset latency (hardware → event), loopback cable @ 48 kHz / 64 samples | ≤ **4.5 ms** |
| Monophonic confirmation latency, notes > 82 Hz (guitar E2) | < **22 ms** |
| Monophonic confirmation latency, notes > 261 Hz (flute/piano C4) | < **12 ms** |
| Chord verification latency (Goertzel/chroma) | < **32 ms** |
| Chord discrimination F1-score (major vs minor vs one-wrong-string) | > **94%** |
| DSP thread CPU usage | < **3%** of one core |
| Stability | Zero buffer underruns over **60 min** continuous |

**Status:** ✅ skeleton done (lock-free ring buffer, HFC onset detector,
YIN monophonic pitch in cents, live CLI, offline ctest).
⬜ remaining: see [libkan/TODO.md](../libkan/TODO.md).

---

## Milestone 2: One-lane playable prototype ("vertical slice" of game feel)
**Months 3-4** · Component: `game/` (on osu!lazer, ADR-0010)

Build the kan! client as a **fork of `ppy/osu` (osu!lazer, MIT) + kan
ruleset** per ADR-0010 and
[docs/architecture/osu-integration.md](architecture/osu-integration.md).
libkan/kanfx stay the real-time DSP core, bridged via a C-ABI shim.
Frame-stability is largely inherited from osu!framework; effort goes to
input bridging, dual-axis judgment, and calibration UX.

**Deliverables**
- `kan-osu` fork + kan ruleset plugin loading (with license-compliance
  setup: THIRD_PARTY_NOTICES, fork-divergence log, BASS licensing decision)
- C-ABI shim + P/Invoke bridge preserving the M1 latency gates
- 3-step calibration screen (acoustic latency, noise floor, fixed-instrument detune)
- Dual judgment engine (instant attack + pitch stability)
- `.kan` ↔ `.osz` converter + one demo song playable end-to-end
- Real support for 3 test instruments: acoustic guitar (mic), recorder flute, keyboard/piano

**Release Gates**
| Metric | Target |
|---|---|
| Visual-audio jitter | Frame time variation < **2 ms** @ 120 FPS |
| Auto-pitch backing track: instrument artificially detuned −35 cents | Track matched in < **2 s** calibration, no audible artifacts, no score loss |
| False-onset rate from ambient noise (normal room, 45 dB SPL, 50 cm from mic) | < **1%** |
| End-to-end input latency added by the C#/P-Invoke bridge | < **1 frame** @ 120 FPS on top of the C++ gates |
| License compliance | All ADR-0010 checklist items resolved before any public build |

---

## Milestone 3: Integrated editor with local AI + biomechanical linter (`kan!forge`)
**Months 5-7** · Components: `game/` (editor UI), `tools/`, `charts/`

The key tool for community growth: in-game visual editor with real-time
spectrogram and ONNX Runtime integration (HTDemucs + Basic Pitch).

**Deliverables**
- MP3 → stems → draft-notes automatic importer
- Direct importers for `.gp5` / `.musicxml` / `.mid`
- Interactive spectrogram @ 60 FPS
- `kan!check` biomechanical playability linter (string/wind/keyboard rules:
  impossible fret jumps, missing breathing windows, etc.)

**Release Gates**
| Metric | Target |
|---|---|
| Mapping time: import a 3-min song from scratch → playable, synced level for 2 instruments | < **20 min** (vs 4-6 h traditional manual mapping) |
| AI draft accuracy (Basic Pitch on isolated stem) | ≥ **80%** of real notes pre-placed with correct time and pitch before human intervention |

---

## Milestone 4: Community platform, per-instrument ranking, open beta
**Months 8-10** · Components: `charts/`, `donations/`, future backend repo

Backend infrastructure for `.kan` package uploads, per-instrument arrangement
versioning, anti-cheat leaderboards, and the `kp` performance-points system.

**Deliverables**
- In-client map repository (1-click download, no leaving the game)
- Lightweight replays (store only the player's micro-timing and cents curves)
- Leaderboards segregated by instrument family and input method
- `kan!supporter` donation flow (see `donations/`)

**Release Gates**
| Metric | Target |
|---|---|
| Download weight to add a new instrument arrangement (violin/flute) to an existing song | < **50 KB** (reuses Layers 1-2) |
| Anti-cheat: synthetic-audio classifier blocking injected perfect MIDI/synth via virtual cable posing as acoustic mic | ≥ **99%** detection |

---

## MVP sequencing note

Do **not** start with the graphics engine or the community web. The build
order is: C++20 console executable reading the microphone and printing the
exact onset millisecond + detected note (done) → harden DSP to Milestone 1
gates → only then the vertical slice. When the console response feels
instant, the technical heart of the project is solved.
