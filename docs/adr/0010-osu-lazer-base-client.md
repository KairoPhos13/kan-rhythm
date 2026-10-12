# ADR-0010: Build the kan! client on osu!lazer (fork + ruleset), not from scratch

- **Status:** Accepted (2026-10-12, project owner decision)
- **Deciders:** KairoPhos
- **Supersedes:** the "engine decision" open item (ADR backlog in docs/TODO.md)

## Context

kan! needs a game client with osu!-class quality: menus, song selection,
skins, particles, replays, multiplayer-ready architecture, 120 FPS frame
stability. Earlier options were: independent osu!framework-based client,
Godot 4 + GDExtension, or own SDL3/Vulkan C++ client (all still open when
this decision was made).

The project owner decided to change strategy: instead of emulating osu!,
**use osu! as the base**. osu!lazer (`ppy/osu`) and its engine
(`ppy/osu-framework`) are both **MIT-licensed** — the same license as
kan-rhythm (ADR-0003) — so they can be forked, adapted, and shipped with
attribution. Much of what kan! needs already exists there: game-feel
feedback, UI framework, skinning, import pipeline, leaderboard client-side
infrastructure. Building on it yields a rich prototype from day one and lets
us spend effort only where kan! is genuinely novel (real-instrument input,
dual-axis judgment, instrument packs, dynamic consonance).

## Decision

1. **kan! client = fork of `ppy/osu` (osu!lazer)** with a kan-specific
   **ruleset** (game mode) plus custom screens (calibration, instrument
   packs, kan!forge later). The fork lives in its own repository
   (e.g. `kan-org/kan-osu`); the monorepo's `game/` folder holds the
   kan-specific ruleset/screen code and design docs, extractable to that repo.
2. **Minimize fork divergence.** Prefer the ruleset plugin path (osu!lazer
   loads rulesets as plugins) so most kan! code compiles *against* the
   fork rather than *inside* it. Fork-level changes are limited to:
   asset/resource wiring, input-service integration, and screens that must
   be first-class (kan!forge). Rebase onto upstream on a regular cadence;
   every fork delta must justify its merge-conflict cost in the PR body.
3. **DSP stays ours.** `libkan` + `kanfx` remain the C++ real-time core
   (miniaudio capture, onset/pitch, FX graph). Bridged to C# through a
   **C-ABI shim** consumed via P/Invoke. osu!framework's audio stack is used
   only for music/backing-track playback and SFX, never for instrument
   capture or judgment (keeps our latency gates authoritative).
4. **Formats stay ours.** `.kan`/`.kchart` (3-layer) remain the community
   format of record; a bidirectional converter maps them to osu! beatmaps
   (`.osz`) so existing osu! content and tooling are reachable. Canonical
   score events map onto ruleset hit objects; instrument charts map onto
   beatmap difficulties.
5. **License compliance is first-class** (this project loves open source and
   respects it):
   - The fork keeps every upstream copyright header and the MIT license text.
   - A `THIRD_PARTY_NOTICES`/attribution screen ships in the client.
   - Adapted osu! code is marked where substantially changed.
   - Upstream-worthy fixes are contributed back to `ppy/osu` via PRs.
   - **BASS caveat:** osu!framework's audio backend (BASS, un4seen) is free
     for non-commercial use; a donation-funded project is a gray zone.
     Action: track as a compliance item (see game/TODO.md) — options: verify
     non-commercial applicability, purchase a commercial license (one-time
     fee, hundreds of USD), or replace/limit the backend. Our own capture
     path (miniaudio, MIT-0) is unaffected.

## Consequences

- Milestone 2 re-scopes from "build a vertical slice engine" to "fork +
  ruleset vertical slice": the frame-stability gate (< 2 ms jitter @ 120 FPS)
  is largely inherited, and effort moves to input bridging, dual-axis
  judgment, and calibration UX.
- C#/.NET 8 joins the toolchain; contributors need dotnet + CMake. Cross-
  platform PC (Linux/Windows/macOS) unaffected — osu!lazer runs natively on
  all three.
- Anti-cheat note: running our own judgment core over P/Invoke keeps score
  semantics under our control (Milestone 4 server re-simulation still works).
- Strategic risk: upstream drift and BASS licensing. Mitigations above;
  revisit at Milestone 4 if fork maintenance cost exceeds the benefit.
