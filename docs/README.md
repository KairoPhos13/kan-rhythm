# docs/

Architecture documentation and design decisions for kan-rhythm.

Start here: [SPECIFICATION.md](SPECIFICATION.md) (technical spec) ·
[ROADMAP.md](ROADMAP.md) (milestones + release gates) ·
[TODO.md](TODO.md) (documentation tasks, ADR backlog).

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
