# kan! Technical Specification

Status: living document — update via PR when a decision changes.
See [ROADMAP.md](ROADMAP.md) for milestones and release gates.

## 0. Guiding principles

- **Do not reinvent the wheel.** Adopt permissively licensed open source
  (MIT / BSD-3 / Apache-2.0 / Zlib) for solved problems. Build only the
  proprietary "glue": hybrid rhythmic judgment, dynamic tuning compensation,
  and the osu!-style community ecosystem.
- **No GPL/AGPL dependencies** in the engine core (keeps every future option
  open and avoids copyleft conflicts). This rules out Aubio, FFTW, RubberBand,
  Essentia, and JACK.
- **Real-time safety is non-negotiable** (see root `CONTRIBUTING.md` §3).

## 1. Open-source stack (license-audited)

| System module            | Library                          | License        | Role / why not the alternative                                        |
|--------------------------|----------------------------------|----------------|------------------------------------------------------------------------|
| Ultra-low-latency I/O    | **miniaudio** (or RtAudio)       | MIT-0 / MIT    | WASAPI Exclusive, CoreAudio, ALSA/Pulse, AAudio in one header. (Not PortAudio for simplicity; not JACK — GPL.) |
| Monophonic pitch (2 cycles) | **Cycfi Q**                   | MIT            | BACF bitstream autocorrelation via XOR ops, no FFT. (Not Aubio/Essentia — GPLv3/AGPLv3.) |
| FFT & filters (chords)   | **KissFFT** or **PFFFT**         | BSD-3          | SIMD FFT feeding Goertzel/chroma template matching in <3 ms CPU. (Not FFTW — GPL.) |
| Backing-track pitch shift| **Signalsmith Stretch**          | MIT            | Real-time ±50 cents, studio quality, BPM preserved. (Not RubberBand — GPLv2; SoundTouch LGPL only if needed.) |
| Stem separation (AI)     | **HTDemucs** via **ONNX Runtime**| MIT            | SOTA drums/bass/guitar/piano/other/vocals separation, local GPU/CPU.   |
| Audio-to-MIDI pre-mapping| **Basic Pitch** (Spotify) via ONNX | Apache-2.0   | <20 MB net, polyphonic audio → MIDI events with pitch bends.           |

## 2. Real-time audio engine (libkan)

Runs on an isolated real-time priority thread (SCHED_FIFO / MMCSS Pro Audio).
Zero dynamic allocation, zero locks, zero GC. Talks to the game engine only
through lock-free SPSC ring buffers.

### Capture & buffering
- Native sample rate: **48 kHz** (covers ~99% of modern hardware).
- DSP block size: **64 samples (1.33 ms)** or **128 samples (2.66 ms)**,
  overlapped hop for continuous analysis.

### Dual-path processing pipeline
- **Stage 0 — Preconditioning:** 35 Hz high-pass filter (kills mic stand /
  floor thumps) + adaptive noise gate calibrated to the room's silence.
- **Stage 1 — Onset detector (Fast Path):** high-frequency-content (HFC)
  energy derivative combined with phase difference. Must fire the rhythmic hit
  event to the game thread in **<4 ms** from the wave entering the buffer.
- **Stage 2 — Transient blanking:** freeze pitch evaluation for the first
  **8–12 ms** after onset (configurable per instrument profile: 8 ms plucked
  string, 15 ms wind/bowed). Attack transients are inharmonic noise that
  fools pitch algorithms.
- **Stage 3 — Score-informed validator (Harmonic Path):**
  - *Map expects 1 note (monophonic):* run Cycfi Q (BACF) bounded to
    **±2 semitones** of the expected note.
  - *Map expects a chord (polyphonic):* run a Goertzel filter bank / 12-bin
    chroma projection targeting the fundamentals + first 3 harmonics of the
    expected chord notes, computing **cosine similarity** against the chart
    template (threshold ≈ 0.80). Never run blind AMT (needs 100–200 ms
    windows — kills the osu!-style feel).

### Dynamic consonance module (auto-tuned backing track)
- Store the player's instrument deviation vector (`Δc_global` in cents) from
  a 2-second pre-game calibration ("play any long comfortable note").
- `Δc = 1200 · log2(f_measured / f_standard)`
- Route the backing track through Signalsmith Stretch applying `+Δc_global`
  pitch shift in real time, and shift the Goertzel/BACF target frequencies by
  the same amount. The instrument and the song end up 100% in tune with each
  other while scoring stays strict.
- **Per-string compensation matrix** (guitar/violin): 3-second guided open
  string strum stores `[Δc1..Δc6]`; expected frequencies adjust per mapped
  string.
- **Relative tuning mode** (fretless/bowed/voice): evaluate intervals
  (`ΔNote_n − ΔNote_{n−1}`) against a moving tonal center of gravity
  (exponential moving average); punish only harmony-breaking deviations.
- Optional **pitch quantization on direct monitoring** (<4 ms) for
  headphones + interface players while on combo.

### Latency budget (end-to-end targets)
- Rhythmic impact (onset → visual feedback): **<15 ms**.
- Harmonic validation (onset → note/chord confirmed): **<35 ms**.
- Below ~45 ms the human eye perceives impact animation and pitch validation
  as a single simultaneous event.

## 3. Instrument & hardware compensation

| Factor                     | Physical problem                                              | Engine solution                                                      |
|----------------------------|---------------------------------------------------------------|----------------------------------------------------------------------|
| Acoustic capture (mic)     | Sound travels ~343 m/s: 1 m = 3 ms + room reverb              | Initial background-noise calibration + adaptive noise gate against ghost onsets |
| Instrument physics         | Plucked string attack ≈ 2 ms; violin/flute ramp 20–40 ms       | Per-instrument-profile rhythm window offset; evaluate start of the air/bow ramp, not peak volume |
| Operating system           | Standard drivers add 20–60 ms                                  | Direct hardware APIs (WASAPI Exclusive / CoreAudio / AAudio / ALSA) with 64–128-sample buffers |

## 4. AI pipeline (local, editor-time only)

AI **never runs in real time during play** (would add >80 ms). It runs
asynchronously when importing a song in the community editor.

### Stem separator — HTDemucs-6s (FP16/INT8 quantized, ONNX)
- Input: user-provided `.mp3` / `.ogg` / `.wav` stereo file.
- Output: 5–6 stems (bass, drums, guitar, piano, other/winds, vocals).
- Purpose: mute/attenuate (−12 dB) the stem the player performs live;
  isolate stems so the transcriber is not confused by drums.
- Performance gate: 3-minute song in **<25 s** on entry-level GPU
  (GTX 1650 / Apple M1) or **<90 s** CPU-only.

### Pre-mapping transcriber — Basic Pitch (ONNX)
- Input: isolated instrument stem.
- Output: time matrix of `onset_time, duration, midi_note, confidence,
  pitch_bend_curve`.
- Purpose: auto-draw ~80% draft notes on the editor spectrogram; the human
  only fixes the 15–20% of errors and assigns difficulties.

## 5. Community file formats

### `.kan` — song package (renamed `.zip` container, like osu!'s `.osz`)
```
song.kan
├── audio_backing.ogg          # Opus/Vorbis @ 48 kHz
├── audio_target_stem.ogg      # isolated stem of the target instrument
├── manifest.json              # metadata, author, BPM timing points,
│                              # SHA-256 integrity signature (leaderboards)
├── canonical_score.bin        # Layer 2: universal symbolic score
└── charts/
    ├── guitar_hard.chart.json # Layer 3: per-instrument arrangements
    ├── flute_normal.chart.json
    └── piano_insane.chart.json
```

### Three decoupled layers
| Layer | Contents | Who creates it |
|-------|----------|----------------|
| 1. Core Sync | Audio file(s)/stems, variable BPM map, bars, exact timestamps | First mapper to upload the song; nobody re-syncs afterwards |
| 2. Universal Score (`canonical_score.bin`) | Time events (µs) + MIDI/Hz + duration + articulation (staccato, vibrato, slide, chord) | Imported from `.gp5`/`.mid`/`.musicxml` or drawn on the spectrogram |
| 3. Instrument Chart (`.kchart` files) | Physical adaptation: fret/string, octave transposition, beginner simplifications | Any community member, in minutes, reusing Layers 1–2 (<50 KB upload) |

## 6. Scoring & adrenaline systems

- **Dual-axis scoring:**
  - *Attack (Timing):* input millisecond accuracy (osu!-style 300/100/50).
  - *Note stability (Sustain/Intonation):* real-time points while holding
    tuning inside the visual sustain lane (like osu! sliders).
- **Reactive audio:** dynamic backing track with stems — attenuate the
  original stem while combo is alive (player's real instrument becomes the
  protagonist); music loses force abruptly on miss.
- **`kp` (kan! points):** osu!-pp-style ranking, leaderboards segmented by
  instrument family **and input method** (Piano MIDI vs Piano acoustic-mic,
  Guitar cable vs Guitar mic) for fair competition.
- **Anti-cheat (Milestone 4):** input-signal classifier detecting synthetic
  MIDI/injected audio disguised as acoustic mic input (measures natural
  phase micro-fluctuations and organic noise), ≥99% block rate.

## 7. Platform targets

- **PC only:** Linux, Windows, macOS (Steam Deck / SteamOS first-class).
- No console ports planned — freedom from NDAs, certification, and walled
  gardens; users freely share `.kan` maps.
- Business model: **donations** (`kan!supporter`-style: cosmetic benefits,
  fast direct downloads in-client, profile medals) — see `donations/`.

## 8. Extensibility architecture (osu!-class quality target)

Design of record: [architecture/platform-architecture.md](architecture/platform-architecture.md).
Summary:

- **Small stable core, big plugin surface:** everything creative is a
  versioned data pack (`.kan` songs, instrument packs, skin packs, FX preset
  packs); native plugin ABI deferred to its own ADR.
- **Audio FX graph (`kanfx/`):** input/backing/sfx/master buses, RT-safe
  nodes with atomic parameter automation; hosts the dynamic consonance pitch
  shifter and mute-on-miss ducker; detection taps are non-consuming.
- **Visual systems (`game/`):** layered scene graph, data-driven GPU
  particles, shader post-chain, full skin replacement, per-frame cost
  budgets against the <2 ms jitter gate.
- **Instrument packs:** profile + technique vocabulary (bends, vibrato,
  slides, harmonics, mutes) mapped onto canonical-score articulations with
  per-technique detection strategies + fingering/intonation helper renderers,
  all toggleable beginner→pro.
- **Mods system:** osu!-parity modifiers with ranked eligibility flags;
  leaderboards partition by mod bitmask.

## 9. Backend platform (`server/`)

Design of record: [architecture/backend-data-model.md](architecture/backend-data-model.md).
Summary:

- Modular monolith → services only when load demands; PostgreSQL single
  source of truth, S3-compatible object storage for blobs, Redis for cache
  and ingestion queue; OpenAPI contract-first.
- Leaderboard key `(chart_id, input_method, mod_mask)`; ranked-only boards;
  dual-axis scores (timing counts + intonation purity in cents).
- `kp` = osu!-pp-style weighted top-N with chart-difficulty multiplier
  (formula gets its own ADR).
- Score integrity: idempotent submission, manifest hash verification,
  server-side replay re-simulation, synthetic-injection classifier (≥99%
  gate), append-only audit events.
