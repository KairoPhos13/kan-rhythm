# game/ — TODO

The **kan!** client on osu!lazer (ADR-0010) + integrated **kan!forge** editor.
Integration architecture: [docs/architecture/osu-integration.md](../docs/architecture/osu-integration.md).
Gates: [docs/ROADMAP.md](../docs/ROADMAP.md).

## Milestone 2 — Fork + ruleset vertical slice

### Setup & compliance (do first)

- [x] Fork `ppy/osu` (osu!lazer, MIT) → **`kan`** (github.com/KairoPhos13/kan);
      submodule wired at `game/client/` (2026-10-12)
- [x] Baseline build green on **Linux** (.NET 10 SDK — the fork targets
      `net10.0`, "dotnet 8" plans are superseded) + `game/docs/environment.md`
      (toolchain, commands, timing notes) — verified 2026-10-12: build
      0 warnings / 0 errors, running **kan!** window, ruleset plugin loading
      (log-proven)
- [ ] Verify baseline build on Windows/macOS (same commands; unverified —
      record results in `game/docs/environment.md`)
- [ ] **Push fork commits to the fork repo** (`0f5db37`, `2970c68` →
      github.com/KairoPhos13/kan): blocked on this build machine having no
      GitHub credentials — the maintainer must push before merging
      `agent/game` into `main` (see `game/docs/fork-divergence.md` pin log)
- [x] Rebrand baseline (ADR-0011 — visible from the first build): window
      title + splash text say **kan!**; about screen (Settings → General →
      About kan!) credits "Built on osu!lazer (MIT) — ppy/osu contributors";
      `THIRD_PARTY_NOTICES` generated (`game/tools/gen-third-party-notices.py`)
      + embedded + shown in about screen; modified-file marking policy in
      `game/docs/fork-divergence.md` (2026-10-12; interactive screenshot QA
      of the about screen still pending)
- [ ] String sweep: user-facing `osu!` references → `kan!` in adapted code
      (scope list in `game/docs/fork-divergence.md`; mechanical deltas logged
      there)
- [ ] Identity design v0: logo/wordmark, color palette, typography, default
      skin shell (original or license-clear assets per `assets/` rules) —
      includes replacing the osu!-derived intro visuals and `lazer.ico` app
      icon (trademark hygiene, ADR-0011 §5)
- [x] BASS licensing resolution — analysis + recommendation in
      `game/docs/bass-licensing.md` (2026-10-12: verify applicability with
      un4seen first; budget one-time commercial licence if `kan!supporter`
      ships paid perks; backend replacement not needed — capture path is
      miniaudio already). Closing action: written answer from un4seen on file
- [x] Create `game/docs/fork-divergence.md` (every fork-level change logs:
      what/why/merge-conflict cost/upstream-PR status) — started 2026-10-12
- [x] Empty kan ruleset plugin loading inside the fork (proves plugin path,
      `Kan.*` namespaces for new code) — `game/ruleset/` (Kan.Ruleset),
      assembly `osu.Game.Rulesets.Kan` (loader-glob constraint, see
      fork-divergence.md F-8); placeholder mode screen; log line
      "kan! ruleset loaded" proves discovery (2026-10-12)
- [ ] QA pass: screenshot-verify about screen + play the placeholder mode on
      a real beatmap (2026-10-12 fork test suite status:
      `osu.Game.Tests` ran headless — 5481 passed / 9 failed / 23 skipped in
      16m20s; 2 of the 9 are flaky (spectator `TestClientSendsCorrectRuleset`,
      editor `TestRestart` — pass on rerun), 7 fail deterministically in code
      the Phase 0 deltas do not touch: 4× chat-link action mapping, beatmap
      importer case-folding, star-display migration)
- [ ] Verify those 7 deterministic failures reproduce on a pristine
      `816df139fd` build (upstream/environment issue, not ours) and, if so,
      consider reporting upstream

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
- [ ] Judgment indicators (dual-axis: attack timing + intonation) — kan!
      visual vocabulary, not osu! hit bursts (ADR-0011)
- [ ] HUD redesign: intonation meters, sustain lanes, instrument helpers —
      keep osu!'s score/combo/accuracy presentation grammar underneath
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
