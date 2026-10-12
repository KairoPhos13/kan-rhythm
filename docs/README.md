# docs/

Architecture documentation and design decisions for kan-rhythm.

Start here: [SPECIFICATION.md](SPECIFICATION.md) (technical spec) ·
[ROADMAP.md](ROADMAP.md) (milestones + release gates) ·
[TODO.md](TODO.md) (documentation tasks, ADR backlog).

Architecture deep-dives:
[architecture/platform-architecture.md](architecture/platform-architecture.md)
(extensibility: FX graph, visuals, instrument packs, mods) ·
[architecture/backend-data-model.md](architecture/backend-data-model.md)
(server: database model, leaderboards, anti-cheat) ·
[architecture/osu-integration.md](architecture/osu-integration.md)
(client base: osu!lazer fork + ruleset, C-ABI bridge, `.kan`↔`.osz`).

## Intended contents

- `architecture/` — system overview, audio pipeline diagrams, threading model
- `adr/` — Architecture Decision Records (numbered, one decision per file,
  e.g. `0001-lock-free-spsc-ring-buffer.md`)
- `dsp/` — algorithm notes: onset detector tuning, YIN implementation details,
  latency budget breakdown

## Ground rules

- English only (see root `CONTRIBUTING.md`)
- ADRs are immutable once accepted: supersede them with a new ADR instead
  of editing history
- Keep docs close to the code they describe; link to source files
