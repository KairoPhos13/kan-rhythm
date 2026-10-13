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

### Instrument modes & selection (owner direction 2026-10-12 — see
`game/docs/instrument-modes.md`)

- [x] Remove stock game modes (osu!standard/taiko/catch/mania) from the
      client build + osu!stable legacy IPC — instruments own the mode slots
      (fork deltas F-9/F-10, 2026-10-13; build green, run verified)
- [ ] Instrument taxonomy v0 (owner scope: **piano** = universal "Keys"
      profile, **ocarina** = "winds/others"; later bass, guitar, drums) —
      resolve the open questions in `game/docs/instrument-modes.md` §6 when
      the owner describes the next work item
- [ ] Instrument-as-ruleset architecture (recommended: shared
      `Kan.Ruleset.Core` + thin per-instrument facades, one per
      `osu.Game.Rulesets.*.dll` assembly per the loader constraint) — needs
      owner sign-off before implementation
- [x] Instrument selection v0 (owner 2026-10-13): **category-tied selector**
      (Category → Instrument dropdowns, `KanInstrumentCatalog`) + **local
      persistence** via ruleset config (`client.realm` — remembered across
      runs). Lives in Settings → Rulesets → kan! (plugin surface, zero fork
      divergence) — 2026-10-13
- [ ] Instrument selection at play start (song select / play setup
      placement) + variant selector (ocarina types) + wiring the selection
      to instrument-wise effects (kanfx packs) once the packs exist
- [ ] Instrument pack schema v0: piano (universal) + ocarina variants
      (range/transposition profiles) per platform-architecture §5
- [ ] Personalization menu for the instrument list (deferred by owner —
      many instruments per category)

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

## Milestone 3 — kan!forge editor (implementation notes:
`game/docs/editor-pipeline.md`)

- [x] Entry-window pipeline (prompt §2.1): "Separate audio stems" +
      "Generate transcription" with live progress bars driven by the libkan
      progress API, background jobs while the form stays usable —
      `KanCreationSection` in the editor setup screen (2026-10-13)
- [x] libkan C-ABI bridge (`game/forge/native`): POD notes, progress
      contract, fetch/separate/transcribe, environment probes + native
      self-test with synthesized input (2026-10-13)
- [x] Transcription draft model + temporal-edit propagation (cut/move/delete
      → shift/split/remove, physically faithful, tested — the §2.2 hard
      requirement) (2026-10-13)
- [ ] Editor authors per instrument (shared taxonomy with the client per
      `game/docs/instrument-modes.md` §5)
- [x] **Merged media area** (owner 2026-10-13): creation flow lives inside
      the stock Resources section (`KanMediaSection : ResourcesSection`) —
      navigator-based track import (like background/video), **video provides
      the audio** merge (extract from the container, visuals-only option),
      per-action **track pickers** for demix and transcription (loaded
      unmixed tracks), **mixdowns never re-demixed** (tested), volume/mute
      per track, render single track → beatmap audio. Verified live in QA
      (2026-10-13)
- [x] URL/YouTube policy (owner 2026-10-13): yt-dlp fetch is **developer-
      build tooling only** (hidden outside dev builds / `KAN_DEV_TOOLS=1`);
      the official flow is local files only — no claims exposure
- [ ] NLE timeline UI (prompt §2.2): up to 6 stem tracks + source audio +      a video track (extend background visuals to VIDEO sources); volume/mute
      per channel; cut/move/delete on any track; the video trim use case
      (cutting intros/outros/ads from a YouTube recording); every edit drives
      the propagation engine
- [ ] Transcription editor UI (prompt §2.3): per-note pitch/onset/duration +
      EXPRession facts (vibrato, bend, dynamics tier) editing; comparison
      metadata inspection (onset anchor, bias-corrected duration + raw end +
      `end_faded`, `saturated`, presence/sim) with "unjudged" states; edited
      fragments shown as proposals
- [ ] Persist the draft into the level store (backend-data-model.md) +
      `.score` / MIDI / JSONL import/export (libkan interchange formats)
- [ ] In-editor per-track **playback** toggles (the render-side volume/mute
      exists; the timeline needs audition/playback per channel —
      chart-transcription-evaluation.md §3d)
- [ ] Environment polish (acceptance criterion 5): GPU/CPU automatic device
      + model auto-download UX; first-class error surfaces (missing model,
      missing bs_roformer, unsupported codec, zero resolved notes)
- [ ] Interactive spectrogram timeline @ 60 FPS (editor overlay on osu!
      editor framework where possible)
- [ ] Inference stack per chart-transcription-evaluation.md (**GGUF +
      Vulkan, single stack**): bs_roformer source separation + transcription;
      heavy AI dependencies remain optional layers. Gates: 3-min song → stems
      < 25 s (GTX 1650/M1), ≥ 80% pre-placement draft accuracy
- [ ] Drag-and-drop MP3 → stems → draft notes workflow
- [ ] Importers: `.gp5`, `.musicxml`, `.mid` → Layer 2 canonical score
      (consume `chart_scribe` MIDI drafts directly)
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
