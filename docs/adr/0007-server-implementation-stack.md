# ADR-0007: Server implementation stack — TypeScript (NestJS) vs Go vs Rust

- **Status:** Accepted (2026-10-05, confirmed by project owner)
- **Deciders:** server/ component maintainers (draft by Agent 6)
- **Related:** [docs/architecture/backend-data-model.md](../architecture/backend-data-model.md)
  (§1.6 leaves the language open), [server/TODO.md](../../server/TODO.md)

## Context

The kan! backend is a modular monolith (auth, catalog, scores, kp, replays,
billing, moderation, events) backed by PostgreSQL, Redis, and S3-compatible
object storage. Design targets: 1M users, 100k songs, 10M scores,
500 score-submits/sec peak — on a single primary + replica. The heavy lifting
(transactional integrity, partitioned scores, precomputed boards) lives in
PostgreSQL; the application tier is mostly I/O-bound request handling, queue
consumption, and webhook processing.

Two hard constraints shape the choice:

1. **Open-source contributor ecosystem.** kan! is donation-funded and MIT
   licensed. The backend must attract outside contributors the way osu-web /
   osu-server-cho attract contributors to the osu! ecosystem. A future public
   web frontend (catalog browse, profiles, leaderboards, appeals) is certain.
2. **Shared C++ judgment core.** Score validation re-simulates judgment with
   the shared libkan-derived C++ core compiled as a server-side tool
   ([server/TODO.md](../../server/TODO.md), implementation phase). The
   application language only needs to invoke this tool out-of-process; it does
   not need to embed C++.

The contract-first rule (OpenAPI committed before implementation) means all
three candidates generate server stubs and typed clients equally well.

## Options considered

### A. TypeScript on Node.js (NestJS)

- Largest open-source contributor pool by a wide margin; web developers can
  contribute from day one without systems-programming experience.
- The future web frontend (React/Svelte catalog + profiles) shares types with
  the backend via the OpenAPI-generated client — one language across the whole
  web surface, which matters for a small maintainer team.
- NestJS gives enforced module boundaries (matches the modular-monolith rule),
  DI, validation pipes (class-validator/zod), and a familiar test story.
- Weaknesses: CPU-bound work (kp recomputation) blocks the event loop —
  mitigated by running recompute as a separate worker process and by pushing
  aggregation into SQL; runtime typing is erased — mitigated by strict mode,
  zod schemas at every boundary, and contract tests against openapi.yaml.

### B. Go

- Excellent fit for queue-heavy ingestion workers; single static binary
  simplifies ops; goroutines handle 500 submits/sec trivially.
- Smaller contributor pool than TS for web-adjacent work; no type sharing with
  the future frontend (generated clients help, but two languages remain).
- Weaker structure story: module boundaries are convention, not enforced;
  large Go codebases drift without strong review discipline.

### C. Rust (axum/actix-web)

- Conceptual proximity to the C++ DSP core and the strongest correctness
  guarantees; great performance ceiling.
- Smallest web-contributor pool of the three; slowest iteration speed for a
  CRUD-heavy catalog/billing/moderation surface; hiring/volunteering friction
  is real for a donation-funded project.
- The C++ re-simulation tool is out-of-process regardless, so Rust buys no
  integration advantage here.

## Decision (proposed)

Adopt **TypeScript (strict mode) with NestJS** for the application tier:

- Node.js LTS runtime, `pnpm`, PostgreSQL via `pg`/Kysely (typed SQL, no
  heavyweight ORM hiding the partitioned-table semantics), Redis via `ioredis`,
  S3 via the official AWS SDK v3 (Apache-2.0).
- Separate **worker processes** (same codebase, different entrypoint) for:
  score-ingestion queue consumer, kp recompute, validation orchestration
  (invoking the C++ re-simulation tool), webhook processing.
- OpenAPI-first: `server/api/openapi.yaml` is the source of truth; server
  routes and the frontend client are generated/checked against it in CI.
- All dependencies must satisfy the license policy (ADR-0003): MIT/Apache-2.0/
  BSD/ISC/Zlib only.

## Consequences

- Contributors can work across backend and future web frontend with one
  skill set; contributor onboarding friction is minimized.
- CPU-heavy paths (kp recompute over 10M scores) must never run on API
  request threads — they live in workers and/or SQL. This is an architectural
  rule, not just a preference.
- If profiling ever shows the ingestion path is language-bound (it should not
  be at 500 submits/sec — Postgres write batching dominates), the scores
  ingestion worker is the single module to rewrite in Go/Rust; the modular
  monolith boundaries make that a contained change.
- Go remains the runner-up and the natural choice for the standalone C++
  re-simulation tool's supervisor if a polyglot sidecar is ever preferred.
