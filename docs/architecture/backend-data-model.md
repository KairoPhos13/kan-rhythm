# kan! Backend Architecture & Data Model (`server/`)

Status: accepted design direction for Milestone 4. Scope: users, songs,
charts, scores, leaderboards (`kp`), replays, donations/entitlements, events,
anti-cheat. Lives in `server/` now; expected to extract into the `kan-web`
repository at Milestone 4.

**osu! reuse policy (ADR-0012):** the backend stays ours (this document).
Adopted from the MIT-licensed osu! ecosystem: the **pp architecture** from
`ppy/osu-tools` as the reference for `kp` (difficulty attributes →
performance calc → weighted top-N, extended with our intonation axis and
per-instrument/input-method weighting), and (deferred) `ppy/osu-server-spectator`
for multiplayer/spectating. **`ppy/osu-web` is AGPL-3.0: no code from it
may enter this repository**; its website/profile UX is emulated clean-room
from public behavior only.

## 1. Guiding decisions

1. **Modular monolith first, services later.** One deployable with strict
   internal module boundaries (auth, catalog, scores, kp, replays, billing,
   moderation). Extract a module into a service only when load demands it.
   osu! (osu-server → lazer-server) and every successful community game
   validate this path; premature microservices would kill a donation-funded
   project.
2. **PostgreSQL as the single source of truth** for relational data
   (users, catalog, scores). Rationale: transactional integrity for
   score/ranking writes, partitioning for the scores table, JSONB for
   flexible pack metadata, and a permissive ecosystem (no licensing risk).
3. **Object storage (S3-compatible) for blobs**: `.kan` packages, replay
   files, skin/instrument packs, transparency reports. DB stores keys +
   SHA-256, never blobs.
4. **Redis** for cache, rate limits, session tokens, and the score-ingestion
   queue (burst absorption after featured-map events).
5. **Analytics later**: if event analytics are needed beyond Postgres,
   ClickHouse/Timescale: deferred decision (own ADR), not day-1.
6. **Implementation language**: open: TypeScript (NestJS), Go, or Rust all
   fit; decide by ADR before Milestone 4 implementation. Data model below is
   language-agnostic.

## 2. Modules & responsibilities

| Module | Owns | Key APIs |
|---|---|---|
| `auth` | accounts, OAuth, sessions, device tokens | signup/login, API keys for client |
| `catalog` | songs, charts, packs, versions, ranked status | upload, download URLs, search, ranked workflow |
| `scores` | score submission, validation, replays ingest | submit (idempotent), replay upload/fetch |
| `kp` | kan! points computation, leaderboards, ranks | recompute jobs, board queries |
| `billing` | donations, supporter entitlements (see donations/) | webhook receivers, entitlement checks |
| `moderation` | anti-cheat verdicts, reports, bans | verdict ingest, appeals |
| `events` | audit log + community events (tournaments, featured maps) | append-only event store |

## 3. Core data model (PostgreSQL sketch)

Naming: snake_case; all tables `id BIGINT GENERATED ALWAYS AS IDENTITY` or
UUIDv7 (time-ordered, chosen for scores/leaderboards locality). Timestamps
`timestamptz UTC`. Soft-delete only where legally required.

```sql
-- identity --------------------------------------------------------------
users            (id uuid7 pk, username citext unique, email citext unique,
                  country char(2), created_at, disabled_at,
                  avatar_key, profile_bg_key)
user_credentials (user_id fk, provider enum(kan,steam,google,discord),
                  external_id, secret_hash null)     -- OAuth-first
supporter_entitlements (id, user_id fk, tier, source enum(donation,code),
                  valid_from, valid_until null, receipt_ref)

-- instruments ------------------------------------------------------------
instrument_packs (id uuid7 pk, slug unique, name, family
                  enum(plucked,bowed,wind,keyboard,percussion,other),
                  format_version semver, manifest_sha256, storage_key,
                  uploaded_by fk users, visibility, created_at)

-- catalog ----------------------------------------------------------------
songs            (id uuid7 pk, title, artist, source, license
                  enum(cc0,cc-by,cc-by-sa,cc-by-nc,original,licensed),
                  audio_license_evidence text, created_by fk users,
                  ranked_status enum(unranked,pending,ranked,loved,graveyard),
                  created_at, updated_at)
song_versions    (id uuid7 pk, song_id fk, version_no int,
                  manifest_sha256 char(64), storage_key, size_bytes,
                  timing_points jsonb,          -- Layer 1 BPM snapshots
                  canonical_score_key,          -- Layer 2 blob in storage
                  published_at, unique(song_id, version_no))
charts           (id uuid7 pk, song_version_id fk, instrument_pack_id fk,
                  difficulty enum(easy,normal,hard,insane,expert,expertplus),
                  chart_key text,               -- e.g. "guitar_hard"
                  storage_key, size_bytes,      -- GATE: < 50 KB
                  mapper_id fk users, status, created_at,
                  unique(song_version_id, instrument_pack_id, chart_key))

-- gameplay ---------------------------------------------------------------
scores           (id uuid7 pk, chart_id fk, user_id fk,
                  instrument_family, input_method enum(mic,line_in,midi),
                  mod_mask bigint not null default 0,
                  total_score int, max_combo int,
                  count_perfect int, count_great int, count_ok int,
                  count_miss int,
                  accuracy_cents numeric(6,2),  -- intonation purity axis
                  timing_avg_ms numeric(6,2), timing_dev_ms numeric(6,2),
                  kp numeric(8,3),
                  client_hash char(64),         -- signed build fingerprint
                  submitted_at,
                  validity enum(pending,valid,invalid,cheat) default 'pending')
                  PARTITION BY HASH (chart_id); -- 32 partitions day-1
replays          (score_id pk/fk, storage_key, size_bytes,
                  curves_key,                   -- micro-timing + cents curves
                  created_at)
kp_stats         (user_id pk, kp numeric(8,3), global_rank int,
                  country_rank int, instrument_ranks jsonb,  -- per-family kp
                  updated_at)                  -- recomputed async, weighted
                                               -- top-N scores (osu!-pp model)
leaderboard_entries (chart_id, input_method, mod_mask, user_id, score_id,
                     kp, rank_in_board int,
                     pk (chart_id, input_method, mod_mask, rank_in_board))

-- trust ------------------------------------------------------------------
cheat_verdicts   (id, user_id fk, score_id fk null, classifier_version,
                  verdict enum(clean,suspicious,synthetic_injection),
                  confidence numeric(4,3), evidence jsonb, created_at)
audit_events     (id bigint identity, actor_id, action text, entity_type,
                  entity_id, payload jsonb, created_at)  -- append-only,
                  PARTITION BY RANGE (created_at)        -- monthly

-- billing (donations/) ---------------------------------------------------
donations        (id uuid7 pk, user_id fk null, provider, provider_ref
                  unique, amount_cents int, currency char(3), recurring bool,
                  status, created_at)          -- no card data, ever; PII-minimal
```

## 4. Leaderboard semantics

- **Board key** = `(chart_id, input_method, mod_mask)`, per ROADMAP M4 gate:
  fair competition across instrument families and input methods (Piano MIDI ≠
  Piano acoustic-mic). Instrument family is derived from the chart's pack.
- **Ranked-only boards**: charts reach `ranked` through the QA workflow
  (`kan!check` automated pre-screen → community/maintainer review), mirroring
  osu! BN. Unranked charts keep scores but no `kp`.
- **`kp` computation** (osu!-pp analog): per-user weighted sum over top-N
  ranked scores, `kp_total = Σ kp_i · w^i` (w ≈ 0.95), with a difficulty
  multiplier estimated from chart features (density, technique vocabulary,
  tempo). The exact formula gets its own ADR + tuning against real data.
- **Dual-axis scores**: each score row carries BOTH timing counts and
  intonation purity (`accuracy_cents`) so boards can sort/weight either.
  This is the "beyond osu!" part; the mix ratio is a ranked-criteria decision.

## 5. Score submission & anti-cheat flow

```
client → POST /scores {chart_id, manifest_sha256, result, replay(curves)}
  1. Idempotency-Key header; reject duplicate submits
  2. Verify: chart exists & ranked; manifest hash matches catalog;
     client_hash is a known-signed build; timing distribution plausibility
     (humans have variance; zero-jitter perfect runs flag synthetic input)
  3. Enqueue → validation worker: replay re-simulation of judgment from
     curves (server-side libkan judgment code, shared C++ core compiled to
     a server tool) → validity verdict
  4. valid → kp recompute (async) → board insert (transactional upsert)
  5. cheat verdict → moderation queue, user flag, board exclusion
```

ROADMAP M4 gate: ≥99% block of injected synthetic MIDI posing as acoustic
mic input (phase micro-fluctuation / organic-noise classifier: research item
in tools/TODO.md feeds step 2-3).

## 6. Scalability & operations

- **Scores table**: hash-partitioned by `chart_id` from day one; per-board
  queries hit `leaderboard_entries` (precomputed), never scan scores.
- **Read path**: Redis cache for boards/profiles (TTL 30-60 s, invalidated on
  write); object-storage downloads served via CDN presigned URLs. The server
  never proxies `.kan` bytes (this is also the `kan!supporter` fast-download
  hook).
- **Write path**: score ingestion through Redis queue → batched inserts;
  kp recompute debounced per user.
- **Growth envelope** (design target before any re-architecture): 1M users,
  100k songs, 10M scores, 500 score-submits/sec peak. Postgres + partitions +
  precomputed boards handles this on a single primary + replica; sharding is
  a post-success problem.
- **Backups**: nightly base + WAL archiving; object storage versioning;
  restore drills documented before public beta.
- **Privacy**: PII-minimal (no real names, no card data; provider tokens
  only), GDPR export/delete endpoints, donation anonymity option.

## 7. Client API surface (contract-first)

OpenAPI spec committed in `server/api/openapi.yaml` BEFORE implementation;
client and server developed against it in parallel. Auth: OAuth2 device flow
for the game client. All endpoints versioned under `/api/v1/`.
