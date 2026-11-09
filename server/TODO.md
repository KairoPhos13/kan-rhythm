# server/ — TODO

Design of record: [docs/architecture/backend-data-model.md](../docs/architecture/backend-data-model.md).
Implementation starts Milestone 4; spec/design work can start earlier.

## MVP (lazer protocol) — `app/` (done)

- [x] Scope note: [docs/mvp-scope.md](docs/mvp-scope.md) ("Draft for owner review")
- [x] `app/` NestJS + PostgreSQL 16 MVP: OAuth (`/oauth/token`), registration,
      profiles, beatmap search/lookups/downloads, score tokens + submission,
      leaderboards — protocol inventory: [docs/lazer-protocol.md](docs/lazer-protocol.md)
- [x] Level import CLI (`npm run import -- <file.osz>`) with `.osu` id rewriting
      and checksum computation; S3-compatible storage driver with presigned
      download redirects
- [x] Unit tests + client-flow e2e (jest + supertest against docker postgres)
- [x] Production deploy docs for the 1 GB ARM64 host (`app/deploy/DEPLOY.md`,
      memory budget ≤ 700 MB)
- [ ] Client fork changes (not done here): widen the trusted texture host list
      beyond `*.ppy.sh`, point preview audio at our server
      (see [docs/mvp-scope.md](docs/mvp-scope.md) §8)

## Design phase (can start now, in parallel)

- [x] ADR: implementation stack (TypeScript/NestJS vs Go vs Rust) with
      hiring/contributor-ecosystem reasoning —
      [docs/adr/0007-server-implementation-stack.md](../docs/adr/0007-server-implementation-stack.md) (accepted)
- [x] ADR: `kp` formula (osu!-pp analog: weighted top-N, difficulty
      multiplier from chart features; dual-axis mix ratio timing vs cents) —
      [docs/adr/0008-kp-formula.md](../docs/adr/0008-kp-formula.md) (accepted)
- [x] ADR: ranked-status workflow & QA roles (kan!check pre-screen →
      community review → ranked), osu! BN precedent —
      [docs/adr/0009-ranked-status-qa-workflow.md](../docs/adr/0009-ranked-status-qa-workflow.md) (accepted)
- [ ] **kp engine implementation (ADR-0008 + ADR-0012):** use the pp
      architecture from `ppy/osu-tools` (MIT — difficulty attributes →
      performance calculation → weighted top-N) as the reference
      implementation; implement ADR-0008's formula on top of it with the
      dual-axis intonation component and per-instrument-family /
      per-input-method weighting; keep upstream headers + copyright for any
      adapted code
- [x] OpenAPI v1 contract in `server/api/openapi.yaml` (auth, catalog,
      scores, profiles, boards, billing webhooks) — contract-first rule
      (passes redocly + spectral lint; see `server/api/`)
- [x] Full SQL schema as migration files from the backend-data-model sketch
      (users, credentials, entitlements, instrument_packs, songs,
      song_versions, charts, scores partitioned by hash(chart_id), replays,
      kp_stats, leaderboard_entries, cheat_verdicts, audit_events, donations)
      — `server/migrations/` (validated on throwaway postgres:16-alpine;
      see `server/migrations/README.md`)
- [x] Threat model: score spoofing, replay tampering,Sybil ranking farming,
      webhook forgery; mitigations mapped to endpoints —
      [server/docs/threat-model.md](docs/threat-model.md)

## Implementation phase (Milestone 4)

- [ ] kp module: difficulty-attribute pipeline + performance calculation
      (osu-tools reference, ADR-0012), async recompute, per-instrument-family
      ranks, board upserts
- [ ] Multiplayer/spectator (post-M4, deferred): adopt
      `ppy/osu-server-spectator` (MIT) instead of writing a protocol;
      verify its contract against our OpenAPI at that time
- [ ] Web frontend (future `kan-web` repo): **clean-room rule (ADR-0012)** —
      `ppy/osu-web` is AGPL-3.0; emulate its UX patterns from public
      behavior/screenshots only; no code from it ever enters our repos.
      Contributors deep-familiar with osu-web source stay off the
      corresponding implementation areas

- [ ] Auth module: OAuth2 device flow for the game client
- [ ] Catalog module: `.kan` upload with SHA-256 verification, presigned CDN
      downloads (supporter fast-download hook), search
- [ ] Scores module: idempotent submission, plausibility checks, Redis queue
      → batched inserts, validity pipeline
- [ ] Replay ingest + server-side judgment re-simulation tool (shared C++
      core compiled as a server binary — coordinate with libkan/)
- [ ] kp module: async recompute, per-instrument-family ranks, board upserts
- [ ] Moderation: verdicts from the synthetic-injection classifier
      (tools/ research), flags, bans, appeals
- [ ] Billing: provider webhooks (donations/ design), entitlement checks
- [ ] Ops: nightly base backups + WAL archiving, restore drills, monitoring
      (design targets: 1M users, 10M scores, 500 submits/sec peak)

## Gates (ROADMAP M4)

- [ ] New-instrument arrangement download < 50 KB (catalog enforces budget)
- [ ] Anti-cheat: ≥ 99% block of injected synthetic MIDI posing as mic input
