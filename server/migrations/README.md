# server/migrations/

SQL migrations implementing the schema sketch in
[docs/architecture/backend-data-model.md](../../docs/architecture/backend-data-model.md)
§3 **exactly**, plus the minimum structure PostgreSQL requires for
partitioning (documented per file).

Target: **PostgreSQL 16+** (validated on `postgres:16-alpine`, see below).

## Files (apply in lexicographic order)

| File | Contents |
|---|---|
| `0001_extensions.sql` | `citext`, `pgcrypto` |
| `0002_types_and_uuidv7.sql` | enum types + portable `uuid_v7()` (RFC 9562; drop when the fleet reaches PG 18, which has it built in) |
| `0003_identity.sql` | `users`, `user_credentials`, `supporter_entitlements` |
| `0004_catalog.sql` | `instrument_packs`, `songs`, `song_versions`, `charts` (incl. the < 50 KB gate as a CHECK) |
| `0005_gameplay.sql` | `scores` (HASH-partitioned by `chart_id`, 32 partitions day one), `replays`, `kp_stats`, `leaderboard_entries` |
| `0006_trust_billing.sql` | `cheat_verdicts`, `audit_events` (RANGE-partitioned monthly, append-only rules + `audit_events_ensure_partition()` helper), `donations` (no card data, ever) |
| `tests/smoke.sql` | insert/select round-trip through every table; not part of the deploy sequence |

## Deliberate, minimal deviations from the sketch

1. `scores` PRIMARY KEY is `(id, chart_id)`: PostgreSQL requires a
   partitioned table's PK to include the partition column. UUIDv7 ids remain
   globally unique in practice.
2. `replays`, `leaderboard_entries`, and `cheat_verdicts` carry the score's
   `chart_id` alongside `score_id` so their foreign keys can reference the
   partitioned parent (composite FK, supported since PG 12). `replays` keeps
   `score_id` as its single-column PK, as in the sketch.
3. `audit_events` PRIMARY KEY is `(id, created_at)` for the same partitioning
   reason (RANGE by `created_at`).
4. `charts.status` reuses the `ranked_status` enum (the sketch leaves its
   domain open); `donations.status` uses a dedicated `donation_status` enum.

## Applying

Plain `psql`, one transaction per file, stop on first error:

```bash
psql "$DATABASE_URL" -v ON_ERROR_STOP=1 --single-transaction -f 0001_extensions.sql
# ... same for each file, in order
```

A migration runner (schema_migrations bookkeeping) is an implementation-phase
concern (ADR-0007 stack); until then apply files in order by hand.

### Validation (throwaway Postgres via Docker)

```bash
docker run --rm -d --name kan-mig-test \
  -e POSTGRES_PASSWORD=test -p 55432:5432 postgres:16-alpine
sleep 3
for f in 0*.sql; do
  psql "postgresql://postgres:test@localhost:55432/postgres" \
    -v ON_ERROR_STOP=1 --single-transaction -f "$f" || break
done
psql "postgresql://postgres:test@localhost:55432/postgres" \
  -v ON_ERROR_STOP=1 -f tests/smoke.sql
docker stop kan-mig-test
```

## Operations notes

- **Append-only audit log**: `audit_events` has DO-INSTEAD-OF-NOTHING rules
  for UPDATE/DELETE. Additionally, the application's ingest role should be
  granted `INSERT, SELECT` only on `audit_events*`; the partition-maintenance
  role owns DDL.
- **Monthly partitions**: a cron/ops job calls
  `SELECT audit_events_ensure_partition(date_trunc('month', now())::date + interval '2 months')`
  so partitions always exist ahead of insert traffic; the DEFAULT partition
  catches anything unexpected and should be monitored (non-empty DEFAULT =
  missed maintenance).
- **Hash partition count**: 32 partitions sized for the 10M-scores design
  envelope (~310k rows/partition). Growing past 32 means a re-partition
  migration; do not change MODULUS in place.
