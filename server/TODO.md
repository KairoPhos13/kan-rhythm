# server/ — TODO

Design of record: [docs/architecture/backend-data-model.md](../docs/architecture/backend-data-model.md).
Implementation starts Milestone 4; spec/design work can start earlier.

## Design phase (can start now, in parallel)

- [ ] ADR: implementation stack (TypeScript/NestJS vs Go vs Rust) with
      hiring/contributor-ecosystem reasoning
- [ ] ADR: `kp` formula (osu!-pp analog: weighted top-N, difficulty
      multiplier from chart features; dual-axis mix ratio timing vs cents)
- [ ] ADR: ranked-status workflow & QA roles (kan!check pre-screen →
      community review → ranked), osu! BN precedent
- [ ] OpenAPI v1 contract in `server/api/openapi.yaml` (auth, catalog,
      scores, profiles, boards, billing webhooks) — contract-first rule
- [ ] Full SQL schema as migration files from the backend-data-model sketch
      (users, credentials, entitlements, instrument_packs, songs,
      song_versions, charts, scores partitioned by hash(chart_id), replays,
      kp_stats, leaderboard_entries, cheat_verdicts, audit_events, donations)
- [ ] Threat model: score spoofing, replay tampering,Sybil ranking farming,
      webhook forgery; mitigations mapped to endpoints

## Implementation phase (Milestone 4)

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
