# server/

The kan! backend platform: accounts, song/chart catalog, score submission,
leaderboards (`kp`), replays, ranked workflow, events/audit, and donation
entitlements.

**Status:** planned (Milestone 4) — see [TODO.md](TODO.md).
Design of record: [docs/architecture/backend-data-model.md](../docs/architecture/backend-data-model.md).

## Design artifacts (design phase — complete, drafts pending ratification)

| Artifact | Location |
|---|---|
| API contract (OpenAPI 3.1, lint-clean) | [`api/openapi.yaml`](api/openapi.yaml) |
| SQL migrations (PostgreSQL 16+, docker-validated) | [`migrations/`](migrations/README.md) |
| Threat model (spoofing, replay tampering, Sybil, webhooks) | [`docs/threat-model.md`](docs/threat-model.md) |
| ADR-0007 stack (TypeScript/NestJS accepted) | [`../docs/adr/0007-server-implementation-stack.md`](../docs/adr/0007-server-implementation-stack.md) |
| ADR-0008 `kp` formula (dual-axis mix accepted) | [`../docs/adr/0008-kp-formula.md`](../docs/adr/0008-kp-formula.md) |
| ADR-0009 ranked workflow & QA roles | [`../docs/adr/0009-ranked-status-qa-workflow.md`](../docs/adr/0009-ranked-status-qa-workflow.md) |

## Key decisions (from the design doc)

- Modular monolith first (auth, catalog, scores, kp, billing, moderation,
  events), extract services only when load demands
- PostgreSQL = single source of truth; S3-compatible object storage for
  `.kan`/replay/pack blobs; Redis for cache, rate limits, ingestion queue
- Leaderboard key = `(chart_id, input_method, mod_mask)`; ranked charts only
- Dual-axis scores: timing counts + intonation purity (`accuracy_cents`)
- Anti-cheat: server-side replay re-simulation + synthetic-injection
  classifier (≥99% block gate)
- Contract-first: OpenAPI spec before implementation

## Ground rules

- English only (root `CONTRIBUTING.md`)
- PII-minimal, GDPR-aware; no payment card data ever (provider tokens only)
- Secrets via environment/secret manager only — never in the repo
- This component will extract into the `kan-web` repository at Milestone 4;
  keep it free of dependencies on game/libkan code except the shared
  judgment re-simulation tool (explicit, versioned)
