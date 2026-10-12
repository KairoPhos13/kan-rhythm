# game/ — TODO

The **kan!** client on osu!lazer (ADR-0010) + integrated **kan!forge** editor.
Integration architecture: [docs/architecture/osu-integration.md](../docs/architecture/osu-integration.md).
Gates: [docs/ROADMAP.md](../docs/ROADMAP.md).

## Milestone 2 — Fork + ruleset vertical slice

### Setup & compliance (do first)

- [ ] Fork `ppy/osu` (osu!lazer, MIT) → `kan-osu` repository; submodule into
      `game/osu/`; green build on Linux/Windows/macOS (dotnet 8 + CMake)
- [ ] Create `game/docs/fork-divergence.md` (every fork-level change logs:
      what/why/merge-conflict cost/upstream-PR status)
- [ ] License compliance setup (ship-blocking, ADR-0010): `THIRD_PARTY_NOTICES`
      generation + about-screen entry, policy for marking modified files
- [ ] BASS licensing resolution (verify non-commercial applicability vs
      purchase one-time license vs replace backend) — decision + evidence in
      `game/docs/bass-licensing.md`
- [ ] Empty kan ruleset plugin loading inside the fork (proves plugin path)

### Input & judgment core

- [ ] C-ABI shim in `libkan/abi/`: fixed-size POD event queue (onset, pitch
      sample, technique findings), atomic parameter handles, no per-frame
      allocations across the boundary
- [ ] C# P/Invoke wrapper (`kan.ruleset.audio`) consuming live onset events;
      measure end-to-end input→event latency (must preserve M1 gates)
- [ ] Dual-axis judgment on top of osu!'s judgment infrastructure:
      instant attack (300/GREAT/MISS) + delayed pitch validation visual
      (~+30 ms: keep glowing vs fail morph)
- [ ] Consume libkan events via the C-ABI queue only — never call DSP from
      the update/draw threads

### Game feel & calibration

- [ ] Single-lane highway using ruleset drawables @ 120+ FPS
      (gate: frame jitter < 2 ms — largely inherited, verify with our skin)
- [ ] Instant hit feedback <15 ms from onset event to pixels: sparks, impact
      SFX, judgment text
- [ ] 3-step calibration screen (acoustic latency, noise floor, fixed-
      instrument detune → Δc applied to kanfx backing-track pitch shift)
- [ ] Instrument helper renderers (per instrument pack): fret/string bubbles,
      fingerings, intonation sustain lanes — all toggleable beginner→pro
- [ ] Skin support: kan! default skin + osu! skin compatibility where
      concepts map cleanly
- [ ] Mods: reuse osu! `Mod` classes; add kan-specific mods (NoHelpers,
      intonation strictness) with ranked-eligibility flags
- [ ] Support 3 test instruments end-to-end: acoustic guitar (mic),
      recorder flute, keyboard/piano
- [ ] Ambient-noise false-trigger test (gate: < 1% @ 45 dB SPL, 50 cm)
- [ ] Auto-pitch backing-track gate: −35-cent detuned instrument matched
      in < 2 s calibration, no artifacts, no score loss

### Content pipeline

- [ ] `.kan` ↔ `.osz` converter in `tools/` (Layer 1/2/3 mapping per
      osu-integration.md §3); round-trip lossless for kan! data
- [ ] Import one converted demo song (charts/official/) as beatmap
      difficulties and play it end-to-end

## Milestone 3 — kan!forge editor

- [ ] Interactive spectrogram timeline @ 60 FPS (editor overlay on osu!
      editor framework where possible)
- [ ] ONNX Runtime integration (local, async — never during gameplay):
      HTDemucs stems (gate: 3-min song < 25 s GTX 1650/M1), Basic Pitch
      draft notes (gate: ≥ 80% pre-placement accuracy)
- [ ] Drag-and-drop MP3 → stems → draft notes workflow
- [ ] Importers: `.gp5`, `.musicxml`, `.mid` → Layer 2 canonical score
- [ ] Chart authoring UI for Layer 3 (`.kchart`) incl. fingering fields
- [ ] `kan!check` biomechanical linter integration (red alerts: impossible
      fret jumps at BPM, missing breathing windows for winds)
- [ ] Mapping-time gate: 3-min song → playable synced level for 2 instruments
      in < 20 min

## Milestone 4 — Community client features

- [ ] In-client map browser (1-click `.kan` download) — osu! browser
      patterns adapted to our `server/` OpenAPI
- [ ] Lightweight replay system (micro-timing + cents curves)
- [ ] `kp` display, per-instrument/per-input-method leaderboards
- [ ] `kan!supporter` cosmetics, medals, fast-download hooks (donations/)

## Rules

- English only; minimize fork divergence (log every fork delta);
  license compliance ship-blocking; depends on libkan/kanfx C-ABI only —
  never on their internals from C#.
