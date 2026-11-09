# kan! (`kan-rhythm`)

> *"Grab your instrument. You kan!"*

**kan!** is a free, open-source, community-driven rhythm game built natively for
**Linux, Windows, and macOS**.

The name comes from the Japanese **奏でる (*kanaderu*), "to play an
instrument, to make it sound"** — *kanade* is the playing itself. And, read
in English: grab your instrument, you *kan*.

You play community-made levels on **your own instrument** — flute, ocarina,
violin, guitar, voice, whatever you have — through a plain microphone. The
game listens and judges each note by its timing and its pitch, the way a
rhythm game judges a key press.

Practice made fun is the point. Rhythm games are great at making people
practise for hours; kan! points that at real music, so what players learn
goes with them — to their church, their friends, their community. kan! is
built to be calm: forgiving by default, strict only when you choose, and it
never aims at anxiety or compulsion to keep you playing.

> **Collaboration language: English.** All code, comments, commits, docs and
> discussions in this repository are in English. See [CONTRIBUTING.md](CONTRIBUTING.md).

---

## Core Philosophy

1. **Instrument Agnostic:** Whether you have a $5 plastic recorder, an acoustic
   guitar, a violin, or a full MIDI/acoustic piano, **kan!** adapts to your
   instrument's physics.
2. **Sound Good First (Dynamic Consonance):** Fixed-pitch or slightly detuned
   instrument? Instead of punishing you or forcing dissonant audio, our DSP
   engine micro-shifts the backing track in real time (`±cents`) to lock into
   harmonic consonance with your instrument.
3. **By the Community, For the Community:** Powered by donations and open
   collaboration — no predatory DLCs, no proprietary hardware cables, and no
   console walled gardens.

---

## Technical Architecture

- **`libkan` (Real-Time C++ DSP Engine):**
  - **Fast Path (<5 ms):** High-Frequency Content (HFC) & spectral-flux
    **onset detection** triggers instant visual/rhythmic hit judgments
    (`PERFECT / GREAT / MISS`) the exact millisecond you strike a string, key,
    or blow air.
  - **Harmonic Path (<30 ms):** Score-informed **BACF** (Bitstream
    Autocorrelation) for monophonic instruments and targeted
    **Goertzel/Chroma template matching** for polyphonic chords.
- **3-Layer Beatmap Format (`.kan` & `.kchart`):**
  - **Layer 1 (Core Sync):** audio stems + variable BPM timing points.
  - **Layer 2 (Universal Score):** canonical pitch/chord events and articulations.
  - **Layer 3 (Instrument Chart):** lightweight (`<50 KB`) community
    arrangements that adapt any song to specific instruments and difficulty
    tiers without re-uploading audio.
- **`kan!forge` (AI-Assisted Map Editor):** integrated spectrogram timeline
  featuring local ONNX inference (**HTDemucs** for stem separation +
  **Basic Pitch** for automated draft note transcription) and **`kan!check`**,
  a biomechanical linter that validates physical playability per instrument.
- **Client base — osu!lazer, rebranded as kan!:** the game client is a
  fork of `ppy/osu` (MIT, ADR-0010) + a kan ruleset plugin, giving us
  osu!-class menus, scoring, skins and frame stability from day one —
  redesigned into KAN! (kanade, "you kan!") per ADR-0011: a standalone
  game, not a mod. Instrument capture and judgment stay in `libkan`/`kanfx`
  behind a C-ABI bridge; `.kan` maps convert bidirectionally to `.osz`.
  License compliance is ship-blocking (attribution preserved,
  `THIRD_PARTY_NOTICES` shipped, fixes contributed back upstream).

Full details: [docs/SPECIFICATION.md](docs/SPECIFICATION.md) ·
[docs/ROADMAP.md](docs/ROADMAP.md)

---

## Repository Layout (monorepo)

Each top-level component is designed to be splittable into its own repository later.
Every component keeps a `TODO.md` with its scoped task list.

```
kan-rhythm/
├── libkan/             Real-time DSP engine (header-only core, future libkan-dsp)
│   ├── include/libkan/   ring_buffer.hpp, dsp_detector.hpp
│   ├── tests/            Offline DSP tests with synthesized signals
│   └── TODO.md
├── kanfx/              Real-time audio FX graph: buses, consonance pitch-shift, ducker
├── apps/
│   └── kan_cli/        Milestone 1 real-time microphone benchmark CLI
├── game/               kan! client + kan!forge editor (future)
├── charts/             .kan / .kchart format spec + song data (future)
├── tools/              CLI utilities: validators, analysis, calibration (future)
├── server/             Backend: users, catalog, scores, kp leaderboards (future)
├── donations/          Donation engine — financial support for the project (future)
├── assets/             Graphics, sounds, fonts (future)
└── docs/               Specification, roadmap, architecture, ADRs
```

---

## Requirements

- CMake ≥ 3.20
- C++20 compiler (GCC/Clang/MSVC)
- Internet connection the first time (FetchContent downloads miniaudio, MIT-0)
- Linux: ALSA (`libasound2-dev` on Debian/Ubuntu)

## Build & Run

```bash
cmake -B build -DCMAKE_BUILD_TYPE=Release
cmake --build build -j
./build/apps/kan_cli/kan_cli
```

Play a guitar string, a piano key, or blow a flute in front of the microphone:

```
⚡ [ONSET! 300/PERFECT] Hit detected (<3ms) -> Evaluating pitch...
✅ [HIT CONFIRMED at +22.3 ms] Note:  A4 | Freq:  441.2 Hz | Offset: +4.7 cents
🥁 [PERCUSSIVE / MUTE] Rhythmic attack without sustained tone
```

## Tests

```bash
ctest --test-dir build --output-on-failure
```

The offline DSP test feeds a synthesized, slightly detuned A4 (+10 cents)
through the full pipeline and asserts onset detection plus pitch accuracy
within ±3 Hz.

---

## Roadmap

- [ ] **Milestone 1 (`libkan` CLI):** lock-free C++ audio pipeline
      (`miniaudio` + `Cycfi Q` + `Signalsmith Stretch`), sub-5 ms onset
      detection, score-informed chord validation, and dynamic backing-track
      pitch shifting. *(in progress — monophonic onset+YIN done)*
- [ ] **Milestone 2 (Vertical Slice):** single-lane playable prototype
      @ 120+ FPS, 3-step room/instrument calibration, and dual-axis scoring
      (Timing + Intonation stability).
- [ ] **Milestone 3 (`kan!forge`):** in-game spectrogram editor, local AI
      auto-charting, `.gp5`/`.musicxml` importers, and biomechanical
      playability linter (`kan!check`).
- [ ] **Milestone 4 (Community Ecosystem):** `.kan` repository,
      per-instrument leaderboards, and **`kp` (*kan! points*)** ranking system.

Detailed milestones with hard engineering gates (KPIs): [docs/ROADMAP.md](docs/ROADMAP.md)

---

## Ecosystem Naming

| Component          | Name            | Role                                                        |
|--------------------|-----------------|-------------------------------------------------------------|
| Game / client      | **kan!**        | osu!lazer fork + kan ruleset (ultra-low-latency client)      |
| DSP engine (C++)   | **libkan**      | Milestone 1 library (onset + BACF + Goertzel + pitch-shift)  |
| Song package       | **`.kan`**      | Compressed container: audio, tempo map, all instruments      |
| Arrangement file   | **`.kchart`**   | Lightweight (<50 KB) community per-instrument chart          |
| Map editor         | **kan!forge**   | Spectrogram + HTDemucs + Basic Pitch environment             |
| Playability linter | **kan!check**   | Biomechanical validator per instrument                       |
| Ranking system     | **kp**          | *kan! points* — rhythmic precision + intonation purity       |

---

## Join in

kan! is playable and useful today, and it is a long way from the polish of
osu!. It can grow there with help:

- **Issues are open to anyone.** The work still open is listed in each
  component's `TODO.md` (the game's: [game/TODO.md](game/TODO.md)) and in
  the repository's issues. Pick one that inspires you.
- **Artists:** levels need songs that can be shared freely. If you write,
  play or arrange music — melodies and their accompaniments (companions)
  — and would release some of it under a free licence for kan!, you are
  very welcome. Art and sound for kan!'s own identity too (logo, menu,
  intro: see [game/brand/](game/brand/README.md)).
- **Players of other instruments:** kan! has been played mostly on an
  ocarina. Playing it on yours and reporting what feels wrong helps
  everyone.

See [CONTRIBUTING.md](CONTRIBUTING.md) before a pull request.

---

## License

MIT — see [LICENSE](LICENSE). Built with love for music and rhythm gaming,
following the osu! philosophy: the client is open, the community is the value.
miniaudio is MIT-0 / public domain.
