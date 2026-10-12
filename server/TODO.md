# server/ — TODO

Design of record: [docs/architecture/backend-data-model.md](../docs/architecture/backend-data-model.md).
Implementation starts Milestone 4; spec/design work can start earlier.

## Design phase (can start now, in parallel)

- [ ] ADR: implementation stack (TypeScript/NestJS vs Go vs Rust) with
      hiring/contributor-ecosystem reasoning
- [ ] **kp engine design (ADR-0012):** adopt the pp architecture from
      `ppy/osu-tools` (MIT — difficulty attributes → performance calculation
      → weighted top-N) as the reference implementation; adapt with the
      dual-axis intonation component and per-instrument-family /
      per-input-method weighting; keep upstream headers + copyright for any
      adapted code
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
