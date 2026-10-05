# kan! Platform Architecture — Extensibility for osu!-class Quality

Status: accepted design direction. Supersedes nothing; extends
[SPECIFICATION.md](../SPECIFICATION.md) §2/§6 with the extensibility layer.
Goal: reach (and exceed) osu! polish by making everything a *versioned,
data-driven pack* — maps, instruments, effects, skins — over a small stable core.

## 1. Design principles

1. **Small stable core, big plugin surface.** The engine core (audio I/O,
   DSP detection, judgment, render loop, networking) stays lean and
   versioned. Everything creative lives in packs the community can author
   without compiling C++.
2. **Everything is a pack with a semver contract.** `.kan` (song),
   instrument pack, skin pack, FX pack. Each declares the engine API version
   it targets; unknown optional fields are ignored (forward compatibility).
3. **Data-driven first, native plugins later.** v0 extensions are JSON/binary
   data consumed by built-in systems. A sandboxed native ABI (for custom DSP
   nodes or render effects) is a *later* ADR — do not block on it.
4. **Real-time boundary is sacred.** No pack, effect, or helper may allocate,
   lock, or do I/O on the audio thread. Packs are *loaded* on worker threads;
   the audio thread only reads immutable loaded state + atomic parameters.

## 2. Component layering

```
┌─────────────────────────────────────────────────────────────┐
│ game/  kan! client + kan!forge editor                        │
│   scene graph · UI · skins · input · mods · networking       │
├───────────────┬─────────────────────────────────────────────┤
│ kanfx/        │ charts/ (data)                               │
│ audio FX graph│ .kan/.kchart/instrument-pack/skin-pack specs │
├───────────────┴─────────────────────────────────────────────┤
│ libkan/  detection & DSP core (onset, BACF/YIN, Goertzel,    │
│ chroma, calibration, pitch-shift primitives)                │
├─────────────────────────────────────────────────────────────┤
│ miniaudio · Cycfi Q · KissFFT · Signalsmith Stretch (MIT/BSD)│
└─────────────────────────────────────────────────────────────┘
         server/ (backend: users, songs, scores, kp) — separate lifecycle
```

Dependency rule: arrows point down only. `kanfx` may use `libkan` primitives;
`libkan` never knows about `kanfx` or `game`.

## 3. Audio architecture: buses & FX graph (`kanfx/`)

A real-time DSP graph, per-bus effect chains, lock-free parameter automation.

### Buses

| Bus           | Content                                        | Example chain                                      |
|---------------|------------------------------------------------|----------------------------------------------------|
| `input`       | Player's live instrument (post-capture)        | HPF 35 Hz → noise gate → (monitor pitch-correct) → meter |
| `backing`     | Song stems from the `.kan` package             | stem gain → **pitch shifter (Δc consonance)** → ducker (mute-on-miss/combo attenuation −12 dB) |
| `sfx`         | Skin-driven hit/miss/UI sounds                 | per-sound gain → shared reverb send                |
| `master`      | Mix of all buses                               | limiter → output                                   |

### Node contract

- `process(const float* in, float* out, uint32 frames)` — RT-safe, no allocs
- Parameters are `std::atomic<float>` (or small atomic POD structs) written by
  game threads, read by the audio thread — the same SPSC discipline as the
  ring buffer
- Graph topology is immutable while playing; rebuild = double-buffered swap
  at a safe point
- Built-in nodes v0: highpass, noisegate, gain, pitchshift (Signalsmith),
  ducker, limiter, meter, reverb-send. Community FX packs = parameter presets
  over built-in nodes (true custom DSP nodes come with the native ABI ADR).

### Judgment taps

The detection pipeline (libkan) attaches to `input` via non-consuming taps —
effects applied for *monitoring* must never alter what the judges analyze,
except where explicitly designed (e.g. transient blanking happens pre-tap).

## 4. Visual architecture (in `game/`)

- **Scene graph + systems** (ECS-flavored): highway layer, note layer,
  hit-effects layer, HUD, background/video, storyboard-like intro layer —
  each an ordered, composable render stage (osu!-framework precedent).
- **Particle system** data-driven: hit sparks, combo flames, chord bursts
  defined in skin JSON (emitters, curves, sprite sheets), GPU-instanced.
- **Shader pipeline**: post-processing chain (bloom, chromatic pulse on
  downbeats, miss desaturation) with skin-selectable presets; per-instrument
  signature effects (string-break morph, breath fog for winds).
- **Skin system**: versioned pack (JSON manifest + atlas + audio SFX), hot
  reload in editor, full replacement of every visual/aural non-gameplay
  element. Default skin ships in `assets/`.
- Frame-time gate (ROADMAP M2): jitter < 2 ms @ 120 FPS — effects must be
  budgeted; every layer reports its own frame cost in dev overlay.

## 5. Instrument packs & per-instrument helpers

The killer differentiator vs osu!: every instrument gets *native-feeling*
helpers. An **instrument pack** is a data pack containing:

1. **Profile** (already in charts/spec): family, attack type, range,
   transient-blanking window, judgment-window offsets (slow-attack winds
   judged on air-ramp start, not peak).
2. **Articulation/technique vocabulary**: which techniques this instrument
   supports and how they're judged — mapped 1:1 onto canonical score
   articulations:
   | Technique family | Examples | Detection strategy (libkan) |
   |---|---|---|
   | Pitch gestures | bend, vibrato, slide, glissando | cents-trajectory tracking vs expected curve |
   | Articulation | staccato, legato, accent, mute/palm-mute | envelope shape + duration vs expected |
   | Extended | harmonics (string), flutter-tongue (wind), pedal (piano) | spectral signature templates per pack |
3. **Fingering/notation hints**: per-note physical guidance rendered on the
   highway — guitar: fret/string bubbles + finger numbers; piano: fingerings
   and hand splits; winds: fingering diagrams; bowed: bow-direction marks.
   Data lives in the Layer-3 `.kchart` (e.g. `fret`, `string`, `finger`
   fields); the *renderer* per family lives in the pack.
4. **Intonation helper config**: sustain-lane width in cents, relative-tuning
   mode parameters (fretless instruments), tonal-center EMA weights,
   per-string compensation matrix defaults.
5. **Sound-trick validation templates**: Goertzel/chroma templates and
   thresholds for the instrument's extended techniques (feeds the
   score-informed validator, SPECIFICATION §2 stage 3).

Beginner→pro axis: every helper is toggleable (hint density, lane width,
judgment strictness) — difficulty is content, not engine forks.

## 6. Mods system (osu!-parity requirement)

- Mod interface: name, acronyms, `affects_gameplay`, `affects_score`,
  `ranked_eligible`, parameter block; applied via well-defined hooks
  (judgment windows, FX graph inserts, scroll speed, autoplay driver).
- v0 mod set target: Auto (reference bot), Easy (wider lanes/windows),
  HardRock/DoubleTime (timing map transform), NoHelpers, Mute (osu!-style
  backing attenuation extremes), Practice (section loop, no score).
- Leaderboards partition by mod combination (bitmask), like osu!.

## 7. Content pipeline & versioning

- All packs: manifest with `format_version` (semver) + SHA-256 contents,
  validated by `tools/` before upload; client refuses packs newer than its
  supported API version with a clear update message.
- Backwards compatibility: parsers ignore unknown optional fields; breaking
  changes require major-version bump + migration tool in `tools/`.
- Registry (server/): per-pack download stats, version history, and the
  ranked-status workflow (community QA → ranked, mirroring osu! BN flow with
  `kan!check` automated pre-screen).

## 8. Where the work lands

| Piece | Component | Milestone |
|---|---|---|
| FX graph core + built-in nodes | `kanfx/` (new) | M1–M2 (pitchshift/ducker needed for M2 gates) |
| Scene graph, particles, skins | `game/` | M2 |
| Instrument pack spec | `charts/spec/` | M1–M2 (spec), M2 (runtime) |
| Helper renderers (fingering, intonation lanes) | `game/` | M2–M3 |
| Technique validators (bend/vibrato/harmonics) | `libkan/` | M1 extension, M3 |
| Mods framework | `game/` | M2 skeleton, M4 ranked interaction |
| Pack registry + ranked workflow | `server/` (new) | M4 |
