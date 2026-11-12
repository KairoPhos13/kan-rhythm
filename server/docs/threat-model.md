# kan! server threat model (design phase)

Status: draft v1, design-phase deliverable ([server/TODO.md](../TODO.md)).
Scope: the `server/` backend as specified in
[docs/architecture/backend-data-model.md](../../docs/architecture/backend-data-model.md)
and [server/api/openapi.yaml](../api/openapi.yaml). Client-side threats
(game binary tampering) are covered only where they touch the server trust
boundary. Mitigations are mapped to concrete endpoints and schema columns.

> **This document describes the designed backend, not the shipped one.** None of
> the mitigations below exist in `server/app` (the MVP that speaks the lazer
> protocol today): there is no `POST /scores`, no Redis, no validation worker
> and no replay storage. What the shipped server does instead is in
> [mvp-scope.md](mvp-scope.md) §9: score values are validated and clamped, ties
> break on the server's own clock, logins are rate-limited per address and per
> account, and score *content* is otherwise the client's own claim. Read the
> tables below as target design, in the future tense.

The four primary threats, per the TODO: **score spoofing**, **replay
tampering**, **Sybil ranking farming**, **webhook forgery**. A short
"secondary threats" section closes the doc.

## Trust assumptions

1. The game client runs on hardware the player controls; it is **untrusted**.
   Everything it sends is an unverified claim until server-side checks pass.
2. `client_hash` is a *signed build fingerprint*: releases are signed by the
   project; the server only accepts hashes from the known-signed build list.
   This raises the cost of spoofing (attacker needs a patched signed-build
   emulator) but does not by itself prove a score is genuine.
3. Object storage and CDN are semi-trusted: the server issues short-lived
   presigned URLs only; blobs are content-addressed by SHA-256 recorded in
   the catalog at upload finalize.
4. Billing providers are trusted **only through verified HMAC signatures**;
   webhook payloads themselves are attacker-forgeable by anyone who can reach
   the endpoint.
5. Judgment truth is re-derivable server-side: the shared C++ judgment core
   (libkan-derived, compiled as a server tool) re-simulates hit judgments
   from replay curves. This is the root of score trust.

## Threat 1: Score spoofing

**Actor:** competitive cheater wanting leaderboard rank/kp without playing.

**Attack vectors**
- S1. Craft a `POST /scores` body with inflated counts, combo, kp-relevant
  accuracy values, never having played the chart.
- S2. Inject synthetic MIDI/synth audio through a virtual cable posing as
  acoustic mic input (ROADMAP M4 gate: ≥99% block).
- S3. Replay a *captured legitimate submission* (someone else's HTTP body)
  from a different account.
- S4. Modified client claiming a valid `client_hash` (hash lifted from a
  signed build).
- S5. Submit against a chart/manifest the client never downloaded (guessing
  `chart_id` + `manifest_sha256`).

**Mitigations → endpoints/schema**

| # | Mitigation | Where |
|---|---|---|
| S1 | Server-side validation pipeline: score never trusted at submit time; `validity` starts `pending` and only `valid` scores reach boards/kp | `POST /scores` → Redis queue → validation worker (backend-data-model §5 step 3) |
| S1 | Re-simulation: server re-derives judgments from the uploaded curves with the shared C++ judgment core; derived counts/accuracy must match the claimed `result` within tolerance | validation worker; `scores.count_*`, `scores.accuracy_cents`, `scores.timing_*` |
| S2 | Synthetic-injection classifier on curves (phase micro-fluctuation / organic-noise features, research in tools/): zero-jitter perfect runs and inhuman timing distributions flagged | `POST /scores` plausibility check (sync) + classifier (async); verdicts land in `cheat_verdicts` (`verdict='synthetic_injection'`) |
| S3 | Submission is authenticated (bearer token from device flow) and bound to the token's user; `Idempotency-Key` dedup prevents replaying the same key for duplicate rows | `POST /scores` (`Idempotency-Key` header required, 422 when missing; 401 without token) |
| S4 | `client_hash` must appear in the known-signed-build list; unknown builds rejected with 409 `UNKNOWN_CLIENT_BUILD`; build list rotation invalidates cracked clients | `POST /scores` → `scores.client_hash` |
| S5 | `chart_id` must exist and be `ranked`; `manifest_sha256` must equal the catalog value for that chart's song version | `POST /scores` → 409 `CHART_NOT_RANKED` / `MANIFEST_MISMATCH`; `song_versions.manifest_sha256` |
| - | No replay uploaded ⇒ score cannot leave `pending`; replay upload is bound to the submission via `replay_sha256` (see Threat 2) | `ScoreSubmitAck.replay_upload`, `replays` row required for validation |
| - | Rate limits per user/IP on submission (Redis-backed) blunt brute-force and flood attempts | `POST /scores` → 429 |
| - | Confirmed cheating: `validity='cheat'`, board exclusion, moderation queue, user flag/ban; all actions audited | `cheat_verdicts`, `scores.validity`, `audit_events` |

**Residual risk:** a *human* playing with an instrument-like synthetic rig
that passes the organic-noise classifier (e.g. re-performing MIDI through a
physical speaker/mic loopback). Accepted for v1; the classifier arms race is
expected (osu! precedent) and the ≥99% gate targets the naive virtual-cable
case.

## Threat 2: Replay tampering

**Actor:** cheater submitting a genuine-looking replay that does not
correspond to the claimed score, or swapping/editing replay blobs after
submission.

**Attack vectors**
- R1. Upload a crafted/edited curves blob (e.g. inject human-like jitter onto
  a synthetic performance) after claiming a high score.
- R2. Upload someone else's replay blob as proof for a spoofed score.
- R3. Tamper with the stored blob in object storage.
- R4. Submit score A, then replace the replay with replay B via a second
  upload.

**Mitigations → endpoints/schema**

| # | Mitigation | Where |
|---|---|---|
| R1 | Re-simulation is the authority: judgments are re-derived **from the curves**; if derived results disagree with the claimed `result` beyond tolerance, the score is `invalid`/flagged. A crafted replay must actually *play* the claimed performance, which reduces to Threat 1 S2 (classifier sees the curves) | validation worker; `scores.validity` |
| R2 | `replay_sha256` is declared **in the signed submission body** before upload; the presigned `PUT` accepts only bytes hashing to that value (verified at ingest). A borrowed blob has the wrong hash unless the whole submission was copied, which the auth binding (S3) already blocks | `POST /scores` request body; `ScoreSubmitAck.replay_upload` |
| R3 | Blobs are content-addressed: catalog/storage keys record SHA-256; object-storage versioning + bucket write restricted to the ingest role; download URLs are presigned and short-lived, never world-writable | `replays.storage_key`/`curves_key`, ops config (backend-data-model §3/§6) |
| R4 | One replay row per score (`replays.score_id` PK); the presigned upload URL is single-use and scoped to the score's storage key; re-upload requires a new submission | `replays` PK; `GET /scores/{id}/replay` serves only what was ingested |
| - | Public replay availability only for `valid` scores on `ranked` charts; invalid/cheat replays are evidence, restricted to owner + moderators | `GET /scores/{score_id}/replay` (403 semantics) |

**Residual risk:** a full performance-replay generation tool (bot that plays
the game through the real audio path). This is Threat 1 S2 at the highest
sophistication level; detection is the classifier + moderation, not the
schema.

## Threat 3: Sybil ranking farming

**Actor:** operator of many fake accounts inflating a rank, farming
leaderboard positions, or gaming the ranked-review/loved petition process.

**Attack vectors**
- Y1. Mass account creation (device flow is low-friction by design) to occupy
  board slots or inflate country/family ranks.
- Y2. One skilled player + N alt accounts: legit play on alts to test farm
  routes, hold multiple board entries, or evade bans.
- Y3. Coordinated votes/petitions: alt accounts with "ranked play history"
  pushing loved petitions or (if compromised) review votes.
- Y4. Ban evasion by re-registration.

**Mitigations → endpoints/schema**

| # | Mitigation | Where |
|---|---|---|
| Y1 | Board uniqueness: one score per user per board, and kp counts **one score per chart per user** (ADR-0008). Alt accounts cannot multiply a single player's chart kp; board capacity pressure is per-account, raising cost linearly | `leaderboard_entries` (PK includes user slot via `UNIQUE(score_id)` + upsert per user), ADR-0008 §2 |
| Y1 | Device-flow authorization requires a browser session; signup rate-limited per IP/ASN (Redis); country ranks derive from `users.country` which is profile data, cheap to fake; country boards are explicitly secondary to global/family ranks | `POST /auth/device/code` (429), `/ranks/*` |
| Y2 | Alt detection inputs are recorded from day one: `scores.client_hash`, submission IP metadata in ingest audit (payload, not a PII column), timing/cent "biometric" similarity of curves across accounts feeds the moderation classifier | `audit_events.payload`, `cheat_verdicts.evidence`, moderation module |
| Y3 | Loved petitions require signatures from users **with ranked play history** (ADR-0009 §5). A fresh Sybil has no ranked scores; review votes require the Chart Reviewer role (appointed humans), never open to self-serve signup | loved-petition endpoint (post-v1), `POST /charts/{id}/vote` (403 role check) |
| Y4 | Bans attach to `users.disabled_at` **and** to device/credential fingerprints (`user_credentials.external_id` per provider); OAuth-first signup means alt creation costs a real provider account each | moderation module; `user_credentials`, `users.disabled_at` |
| - | All moderation actions (flag, ban, verdict, appeal outcome) are append-only audited and appealable | `audit_events`, `cheat_verdicts`, moderation endpoints (implementation phase) |

**Residual risk:** determined operators with aged OAuth identities and
residential proxies. Accepted: the economic design (kp decay + one-score-per-
chart) makes Sybil farming *low-yield per account*, which is the real defense
(osu! precedent). Detection continues async via the classifier.

## Threat 4: Webhook forgery

**Actor:** attacker fabricating donation events to obtain `kan!supporter`
entitlements without paying, or poisoning financial records.

**Attack vectors**
- W1. `POST /webhooks/{provider}` with a crafted "payment succeeded" payload.
- W2. Replay a captured legitimate webhook many times / across accounts.
- W3. Tamper an in-flight webhook (MITM).
- W4. Flood the endpoint to exhaust processing or hide real events.

**Mitigations → endpoints/schema**

| # | Mitigation | Where |
|---|---|---|---|
| W1 | HMAC signature **required and verified before any processing**; unsigned/bad-signature → 401 without parsing the body into domain logic. Keys live in the secret manager, never in the repo | `POST /webhooks/{provider}` (`X-Kan-Webhook-Signature` header, `webhookSignature` security scheme) |
| W2 | Signed payload includes the provider delivery timestamp; deliveries older than 5 minutes are rejected (replay window). Events are deduplicated by `provider_ref` (UNIQUE in `donations`): redelivery acks 200 with no side effects, at-least-once safe | `POST /webhooks/{provider}`, `donations.provider_ref UNIQUE` |
| W3 | TLS termination at the edge; signature covers the **raw body bytes** (canonical verification), so any mutation invalidates it | webhook verification middleware |
| W4 | Per-source rate limiting + flood monitoring; verification is cheap and stateless, entitlement granting is queued (never synchronous in the handler), so floods degrade gracefully | `POST /webhooks/{provider}` → 429; billing worker queue |
| - | Entitlement grants are idempotent and derived from persisted events, not from the request: `supporter_entitlements` rows reference `donations` via `receipt_ref`; revocation on refund/chargeback is a status-driven job | `donations.status`, `supporter_entitlements` |
| - | Every webhook acceptance/rejection and every entitlement change writes an `audit_events` row for forensics | `audit_events` |

**Residual risk:** provider-side key compromise. Mitigation is operational:
key rotation runbook, provider dashboard reconciliation job (post-v1), and
`donations` rows kept raw-event-linked so a compromise window can be
identified and entitlements revoked in bulk.

## Secondary threats (tracked, mitigations at implementation)

| Threat | Primary mitigation surface |
|---|---|
| Catalog poisoning (malicious `.kan`/`.kchart` content) | `kan!check` Stage-0 hard gate (ADR-0009), size/format CHECKs (`charts.size_bytes < 51200`), ranked-review human gate |
| License laundering (uploading copyrighted audio as `cc0`) | `songs.license` + `audio_license_evidence` reviewed at qualification (ADR-0009 Stage 1), takedown → `graveyard` + audit trail |
| Token theft (bearer tokens on shared PCs) | Short-lived access tokens + rotating refresh tokens with reuse-detection family revocation (`POST /auth/refresh`) |
| Profile/PII scraping | Rate limits on `/users/*`, `/ranks/*`; PII-minimal schema (no real names, no card data); GDPR export/erasure (`/me/export`, `DELETE /me`) |
| Idempotency-key exhaustion/guessing | Keys are client UUIDs scoped per user; store keyed by (user, key) with TTL |
| Rank-reset abuse in QA workflow | Vote/veto/reset require Chart Reviewer role + COI checks, all transitions audited (`POST /charts/{id}/vote`) |

## Verification hooks (definition of done for implementation phase)

- Synthetic-MIDI injection test corpus replayed through `POST /scores` +
  validation worker must hit the ≥99% block gate (ROADMAP M4).
- Webhook endpoint contract tests: unsigned, mis-signed, stale-timestamp,
  duplicate `provider_ref`, all rejected or acked without side effects.
- Idempotency test: duplicate `Idempotency-Key` returns the original ack with
  `duplicate: true` and inserts nothing.
- Re-simulation mismatch test: edited curves blob → score never reaches
  `valid`.
