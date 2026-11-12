# osu!lazer Integration Architecture

Design of record per [ADR-0010](../adr/0010-osu-lazer-base-client.md):
the kan! client is a fork of `ppy/osu` (osu!lazer, MIT) + a kan ruleset,
keeping `libkan`/`kanfx` as the real-time DSP core and `.kan`/`.kchart` as
the community formats.

## 1. Where each piece lives

| Piece | Home | Form |
|---|---|---|
| osu!lazer fork (`kan`) | `game/client/` (submodule of github.com/KairoPhos13/kan) | fork, rebased onto upstream regularly |
| kan ruleset (game mode) | `game/ruleset/` → ruleset plugin | C# library loaded by the fork |
| kan screens (calibration, instrument packs, kan!forge) | `game/screens/` | C# (inside ruleset where possible, fork-level only when necessary) |
| DSP core (onset/pitch/judgment) | `libkan/` | C++20 header-only + shared lib target |
| FX graph | `kanfx/` | C++20 |
| C-ABI shim | `libkan/abi/` | thin C API (`extern "C"`), stable ABI surface |
| Formats `.kan`/`.kchart`/instrument packs | `charts/spec/` | data specs + converter tools in `tools/` |
| Backend | `server/` | separate; client consumes OpenAPI |

## 2. Threading & latency boundaries

```
[miniaudio capture, RT thread]        ← libkan, unchanged (CONTRIBUTING §3)
        │ lock-free SPSC ring buffer
        ▼
[libkan detection threads]  onset / BACF-YIN / Goertzel / calibration
        │ C-ABI event queue (fixed-size, lock-free)
        ▼
[C# P/Invoke bridge: kan.ruleset.audio]
        │
        ▼
[ruleset judgment + rendering: osu!framework update/draw threads]
```

- The C-ABI boundary carries fixed-size POD events (onset, pitch sample,
  technique findings) and atomic parameter handles: no strings, no allocs
  crossing it per frame.
- Latency gates (ROADMAP M1) remain measured at the C++ boundary; the C#
  side adds presentation latency only (< 1 frame at 120 FPS target).
- osu!framework's BASS-based playback handles backing tracks; the dynamic
  consonance pitch shift happens in `kanfx` on the audio path we control.

## 3. Content mapping (`.kan` ↔ osu! beatmaps)

| kan! concept | osu!lazer concept | Notes |
|---|---|---|
| `.kan` package | beatmap archive (`.osz` import path) | converter in `tools/` (`kanchart-pack --to-osz` / `--from-osz`) |
| Layer 1 Core Sync (audio + timing points) | `Beatmap` control points + audio | direct mapping |
| Layer 2 Canonical Score (µs events, articulations) | hit objects of the ruleset | custom `HitObject` subclasses carry articulation/technique payloads |
| Layer 3 `.kchart` (per instrument) | beatmap difficulty (`BeatmapDifficulty` + ruleset mods) | one difficulty per chart; metadata keeps instrument pack reference |
| Instrument pack | ruleset resource + skin elements | fingering hints / intonation lanes rendered by ruleset drawables |
| `kp` ranking | performance points pipeline | server-side (ours); osu! pp code is reference-only |
| Mods | `Mod` classes | osu!'s mod infrastructure reused directly |

Lossless round-trip is required for Layers 1-3 data; osu!-specific extras
(slider paths, combo counters) stay in the `.osz` side only.

How a level's length, BPM and star rating travel in this mapping (the score's
marks in the `.osu`, and the difficulty model): see
[level-metadata.md](level-metadata.md).

## 6. Rebranding (ADR-0011): keep the UX, change the identity

**kan! is a standalone game, not an osu! mod.** MIT covers copyright, not
trademarks: osu! name/logo never appear in our product identity. Full
decision: [ADR-0011](../adr/0011-rebrand-kan-identity.md).

| Area | Keep (osu! patterns) | Redesign (kan! identity) |
|---|---|---|
| Menus & flow | main menu → song select → results structure, search/grouping UX | skin, palette, typography, logo, menu music |
| Scoring HUD | number/combo/accuracy presentation grammar | fed by our dual-axis judgment; intonation meters, sustain lanes |
| In-game visuals | hit-object pipeline, frame pacing | note highway, judgment indicators, fingering/breathing helpers, no approach-circle grammar |
| Mods | osu! `Mod` classes and overlay UX | kan! mods (NoHelpers, intonation strictness) |
| Settings/replays | settings screens, key bindings, replay viewer | branding strings, sound design |

Code policy (rebase-ability first): adapted upstream code keeps
`osu.Game.*`/`osu.Framework.*` namespaces (renaming them would create
catastrophic merge conflicts); **new** kan! code uses `Kan.*` namespaces;
user-facing strings in adapted code are rebranded and logged as mechanical
deltas in `game/docs/fork-divergence.md`. The client window title, about
screen and installer say **kan!**; upstream osu! appears once, in
attribution/credits.

## 4. License compliance checklist (ship-blocking)

- [ ] Fork preserves all upstream copyright headers and `LICENSE` files
- [ ] `THIRD_PARTY_NOTICES` file generated and shown in the client's about screen
- [ ] Substantially modified upstream files marked in comments
- [ ] Upstream-worthy fixes sent back to `ppy/osu` as PRs
- [ ] BASS licensing resolved (verify / purchase / replace) before any
      donation-driven release, tracked in game/TODO.md
- [ ] Kan branding assets clearly distinct (trademark hygiene)

## 5. Work sequencing

1. ~~Fork `ppy/osu` → `kan` repo; submodule into `game/client/`~~ (done
   2026-10-12); next: green build on Linux/Windows/macOS.
2. Empty kan ruleset plugin loading inside the fork (proves the plugin path).
3. C-ABI shim in `libkan/abi/` + P/Invoke wrapper consuming live onset events
   (proves the latency boundary).
4. One-lane playable slice on a converted `.kan` song (Milestone 2 gates).
5. Screens: calibration, instrument pack selection, then kan!forge.
