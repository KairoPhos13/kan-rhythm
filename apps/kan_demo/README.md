# kan_demo — live real-instrument demo

Grab a real instrument (an ocarina works great), plug in a microphone, and
play. `kan_demo` runs the full Milestone-1 detection pipeline of **libkan**
and shows what the engine hears: rhythmic attacks (onsets), the note, and
how many cents you are off — all live in the terminal.

> **Contributing to this app?** Read [DEVELOPMENT.md](DEVELOPMENT.md) first —
> design direction, known corners, and the rules that keep polish
> consistent with the project's goals.

```
====================================================
  kan! demo — live instrument check (libkan v0.1)
  Instrument: ocarina   wind, 15 ms blanking, 480-1450 Hz (C5-F6)
  Audio: 48000 Hz / 128 samples
  Mode: free tuner (onset + note + cents)
====================================================
Ready. Play your instrument!
▶ ONSET
♪ C5   523.85 Hz    +2.0 cents  .......|#.......  (+32 ms)
```

## Build & run

```bash
cmake -B build -DCMAKE_BUILD_TYPE=Release
cmake --build build -j
./build/apps/kan_demo/kan_demo                 # free tuner, ocarina preset
./build/apps/kan_demo/kan_demo --instrument flute
./build/apps/kan_demo/kan_demo --expect C5     # score-informed confirmation
./build/apps/kan_demo/kan_demo --calibrate     # Δc detuning readout
ctest --test-dir build -R kan_demo_selftest    # offline pipeline check
```

Requirements: CMake ≥ 3.20, C++20, a microphone (Linux: ALSA; the first
configure downloads miniaudio + Cycfi Q + KissFFT — MIT/BSD only).

## Options

| option | meaning |
|---|---|
| `--instrument <name>` | `ocarina` (default) · `flute` · `recorder` · `guitar` · `piano` |
| `--expect <note>` | score-informed mode: confirm the charted note (`C5`, `A4`, `F#5`) with the bounded matcher — the exact confirmation path the game will use |
| `--engine <yin\|bacf>` | free-tuner pitch engine. **Default `yin`** (robust). `bacf` = Cycfi Q bitstream autocorrelation (see caveat below) |
| `--calibrate` | play one long comfortable note → prints your instrument's global detuning Δc in cents vs equal temperament (A4 = 440 Hz) and **applies** it |
| `--vibrato` | live technique readout: vibrato rate (Hz) / depth (cents) from the cents trajectory — wide/slow *yuri*-style and classical vibrato both visible |
| `--csv <file>` | write per-note events (t, note, Hz, cents, vibrato) — the same feature stream the learner is evaluated on |
| `--input <wav>` | **offline validation**: run the exact live pipeline over a recording (no audio device) — same consumer code, same event schema as a live session |
| `--events <f.jsonl>` | the per-note description stream (HIT / provisional / CONTENT / final / EXPRESSION / CLOSE facts) — progressive on screen, JSONL on disk; same schema as `kan_analyze --events`, live/offline diffable |
| `--candidates <f>` | candidate hypotheses per attack: `@<onset_ms> midi midi ...` binds to the attack at that time (or plain `midi ...` for the next attack). Include notes still sustaining from earlier attacks. Conditions the CONTENT measurements; absent hypotheses get low presence, never invented facts |
| `--no-gate` | disable the adaptive noise gate |
| `--noise-cal <ms>` | room-silence calibration window at startup (default 500) |
| `--selftest` | offline check on a synthesized note (no audio device needed) |

## What works today

- **Live onset detection** (HFC energy derivative, stage 0 preconditioning:
  35 Hz high-pass + room-calibrated adaptive noise gate).
- **Live monophonic pitch**: note name + Hz + cents vs equal temperament.
  Default engine is **YIN** (rolling 2048-sample window) — reliable and
  dependency-free. `--engine bacf` switches to Cycfi Q bitstream
  autocorrelation.
- **Instrument presets** with profile-driven transient blanking
  (8 ms plucked / 15 ms wind / 6 ms keyboard) and pitch ranges.
- **Score-informed confirmation** (`--expect`): the bounded-lag matcher
  confirms the expected note in ~20 ms of clean tone (measured 21 ms in the
  selftest) — the same code path the game will call per chart note.
- **Δc calibration (`--calibrate`)**: the 2-second reference-note flow of
  SPECIFICATION §2 — prints your instrument's global detuning Δc and then
  **applies** it: the tuner shows your residual vs your own tuning, and
  `--expect` targets shift by the same Δc (measured: a +12-cent-sharp
  instrument reads +0.0 cents after calibration).
- Offline regression test (`--selftest`, wired into ctest).

## Validating the backend on recordings (before building the game)

`--input` runs the **exact live pipeline** over an audio file — the same
consumer code path a live session uses — so the description stream can be
inspected end to end without an instrument. Two worked examples (audio not
committed; supply your own recordings under `test_results/audio/`):

```bash
# Polyphonic piano (the hard case): LH chords under an RH melody. The
# candidate file supplies chart-like hypotheses for 28 hand-verified
# attacks; every hypothesis gets a measured presence/cents/amplitude.
./build/apps/kan_demo/kan_demo \
    --input test_results/audio/07-hallelujah-piano-tutorial_piano.wav \
    --instrument piano \
    --candidates apps/kan_demo/examples/hallelujah-piano-tutorial.candidates \
    --events test_results/logs/07-kan_demo-hallelujah.jsonl

# Monophonic ocarina (the easy case): every attack classified mono,
# provisional -> final pitch, vibrato + dynamics facts.
./build/apps/kan_demo/kan_demo \
    --input test_results/audio/02-frieren-time-flows_ocarina.wav \
    --instrument ocarina --events test_results/logs/02-kan_demo-frieren.jsonl
```

What was measured on those runs (this machine, see the JSONLs):

- **Hallelujah (piano)**: 245 attacks → 72 mono / 173 polyphonic class
  facts; 131 candidate facts from the 28 annotated attacks (similarity
  0.93 typical); 52 final pitches and 315 honest `unresolved`; dynamics
  tiers pp..ff across the session. An absent hypothesis measures presence
  0.00; a wrong early estimate (G4) is corrected by the conditioned final
  (C3, presence 1.00).
- **Frieren (ocarina)**: 80 attacks → 80/80 mono; 78 provisional and 76
  final pitches; 16 vibrato claims; dynamics tiers spread over the piece.

Reading rule for the JSONL: `PITCH` events carry **one** consistent
estimate (`hz` derived from `midi`+`cents`) or are `unresolved` with flags
— treat `unresolved` as "not measured", never as "wrong". `CONTENT`
candidate facts always carry `similarity` (how much of the window the
whole hypothesis set explains) next to `presence`.

## Known caveats (measured, not hidden)

- **Cycfi Q BACF free-tuner lock-in.** `--engine bacf` occasionally latches
  onto a wrong frequency right after a note onset (roughly +80 to +130 cents
  high on a pure test tone) and holds it for the rest of the note. It is
  accurate on steady, already-sounding tone, which is why the pipeline uses
  it for *confirmation* of a known note — not for blind free-tuning. This is
  a real finding from the A/B work (`docs/dsp/bacf-vs-yin.md`); the demo
  defaults to YIN so your first real-instrument session just works. The
  `--expect` path never relies on BACF (it uses the bounded matcher).
- **`--expect` confirms on clean tone.** On breathy/ocarina attacks the
  confirmation lands ~20–50 ms after the onset once the transient clears,
  not the theoretical minimum — correct behavior, since the attack is
  inharmonic (stage-2 blanking).

## What does NOT work yet (by design — Milestone 1 in progress)

- **No scoring / judgment.** The demo measures timing and pitch but never
  judges 300/100/50 windows. That is `game/` (Milestone 2).
- **No backing-track pitch shift.** The dynamic-consonance shifter
  (Signalsmith Stretch, ±Δc) is a Milestone-1 TODO; your instrument is not
  yet "joined" by an auto-tuned song.
- **No chord validation in the demo.** The chord validator exists in
  `libkan/tests/chord_test.cpp` but has no live UI yet; monophonic
  instruments (ocarina!) do not need it.
- **No latency guarantee numbers from this machine.** The measured gate
  tables live in `docs/dsp/latency-measurements.md` (offline benches);
  hardware→event latency needs a loopback-cable run (item 8).
- **No visuals, no chart, no game loop.** Pure terminal.
- **Per-string compensation (`Δc1..Δc6`) and relative tuning** exist as
  libkan APIs (`calibration.hpp`) but the demo drives the global Δc only —
  the guided strum flow and fretless relative-tuning readout are demo TODO.

## Ocarina quick start

1. Sit ~30–50 cm from the microphone; avoid pointing breath at it.
2. Keep quiet for the first half second (noise-gate calibration).
3. `kan_demo` (ocarina preset, YIN engine) and blow a steady note — the
   tuner line shows the note and cents. Keep the note steady; the reading
   settles within ~30 ms of the attack clearing.
4. `kan_demo --expect C5` and try to nail the charted note: the matcher
   confirms within ~10–20 ms of clean tone and reports your error.
5. `kan_demo --calibrate`, hold one note for ~2 s: get your instrument's
   Δc (ocarinas are often 5–20 cents off A440 — that is normal!).

## Troubleshooting

- **"Could not open the audio capture device"** — another app holding the
  mic, or no default capture device (`arecord -l` on Linux).
- **No onsets** — too far / too quiet; bring the instrument closer or run
  with `--no-gate` to check raw sensitivity.
- **Wrong octave** — preset ranges bound the search; use `--instrument
  flute` for wider range, or report the gap in `apps/kan_demo/TODO.md`.
- **CPU** — the pipeline is < 0.1 % of one core offline; any stutter is
  the terminal or the audio driver, not the DSP.

## Related docs

- Pipeline spec: `docs/SPECIFICATION.md` §2
- Detector A/B results: `docs/dsp/bacf-vs-yin.md`
- Gate tracking (measured vs theoretical): `docs/dsp/latency-measurements.md`
- Chord validation: `docs/dsp/chord-validation.md`
- Component task list: `apps/kan_demo/TODO.md`
