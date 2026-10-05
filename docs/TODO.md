# docs/ — TODO

## Immediate

- [ ] ADR template (`adr/0000-template.md`)
- [ ] ADR-0001: Lock-free SPSC ring buffer for audio → game communication (accepted)
- [ ] ADR-0002: Dual-path DSP architecture — onset first, pitch after (accepted)
- [ ] ADR-0003: MIT license + no-GPL dependency policy (accepted)
- [ ] ADR-0004: PC-only platforms (Linux/Windows/macOS), no consoles (accepted)
- [ ] ADR-0005: Monorepo with splittable top-level components (accepted)
- [ ] ADR-0006: `.kan`/`.kchart` 3-layer decoupled map format (proposed —
      ratify when charts/spec/ lands)

## When milestones progress

- [ ] `architecture/` — system overview + audio pipeline diagram + threading model
- [ ] `dsp/` — onset detector tuning notes, YIN vs BACF comparison results,
      latency budget breakdown (measurements from apps/kan_cli harness)
- [ ] ADR: game engine choice (Godot 4 GDExtension vs SDL3/Vulkan) — Milestone 2
- [ ] ADR: stem/transcription model choices (HTDemucs, Basic Pitch) — Milestone 3
- [ ] ADR: donation provider selection — Milestone 4

## Rules

- English only; ADRs immutable once accepted (supersede, don't edit)
- Keep docs close to code; link source files
