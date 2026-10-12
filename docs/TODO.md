# docs/ — TODO

## Immediate

- [ ] ADR template (`adr/0000-template.md`)
- [ ] ADR-0001: Lock-free SPSC ring buffer for audio → game communication (accepted)
- [ ] ADR-0002: Dual-path DSP architecture — onset first, pitch after (accepted)
- [x] ADR-0003: MIT license + no-GPL dependency policy — `adr/0003-mit-license-no-gpl-policy.md`
- [ ] ADR-0004: PC-only platforms (Linux/Windows/macOS), no consoles (accepted)
- [ ] ADR-0005: Monorepo with splittable top-level components (accepted)
- [ ] ADR-0006: `.kan`/`.kchart` 3-layer decoupled map format (proposed —
      ratify when charts/spec/ lands)

## When milestones progress

- [x] `architecture/platform-architecture.md` — extensibility: FX graph,
      visuals, instrument packs, mods (design direction accepted)
- [x] `architecture/backend-data-model.md` — server data model, leaderboards,
      anti-cheat (design direction accepted)
- [ ] `architecture/` — system overview + audio pipeline diagram + threading model
- [ ] `dsp/` — onset detector tuning notes, YIN vs BACF comparison results,
      latency budget breakdown (measurements from apps/kan_cli harness)
- [ ] ~~ADR: game engine choice (Godot 4 GDExtension vs SDL3/Vulkan)~~ —
      closed by ADR-0010 (osu!lazer base client)
- [ ] ADR: native plugin ABI for custom DSP/visual nodes (deferred until
      data-driven packs prove insufficient)
- [x] ADR: `kp` formula (weighted top-N + difficulty multiplier + dual-axis mix)
      — `adr/0008-kp-formula.md` (proposed draft)
- [x] ADR: server implementation stack (TypeScript vs Go vs Rust)
      — `adr/0007-server-implementation-stack.md` (proposed draft)
- [ ] ADR: stem/transcription model choices (HTDemucs, Basic Pitch) — Milestone 3
- [ ] ADR: donation provider selection — Milestone 4
- [x] ADR-0010: osu!lazer base client (fork + ruleset) —
      `adr/0010-osu-lazer-base-client.md` (accepted; supersedes the "engine
      selection" item above — Godot/SDL3 alternatives closed)
- [x] ADR-0011: rebrand to KAN! identity (standalone game, not a mod) —
      `adr/0011-rebrand-kan-identity.md` (accepted; keep osu! UX patterns,
      redesign gameplay visuals, namespace/trademark policy)

## Rules

- English only; ADRs immutable once accepted (supersede, don't edit)
- Keep docs close to code; link source files
