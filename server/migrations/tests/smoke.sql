-- tests/smoke.sql: round-trip through every table of the schema.
-- Not part of the deploy sequence; run after migrations on a throwaway DB.
-- Every statement must succeed; final SELECTs print what landed.

BEGIN;

-- identity ------------------------------------------------------------------
INSERT INTO users (username, email, country)
VALUES ('smoke_mapper', 'smoke_mapper@example.test', 'DE')
RETURNING id AS user_id \gset

INSERT INTO user_credentials (user_id, provider, external_id, secret_hash)
VALUES (:'user_id', 'discord', 'discord-12345', NULL);

INSERT INTO supporter_entitlements (user_id, tier, source, receipt_ref)
VALUES (:'user_id', 'supporter', 'donation', 'provider-token-abc');

-- catalog -------------------------------------------------------------------
INSERT INTO instrument_packs
    (slug, name, family, format_version, manifest_sha256, storage_key,
     uploaded_by)
VALUES ('guitar-acoustic', 'Acoustic Guitar', 'plucked', '1.0.0',
        repeat('a', 64), 'packs/guitar-acoustic-1.0.0.tar.zst', :'user_id')
RETURNING id AS pack_id \gset

INSERT INTO songs (title, artist, source, license, created_by)
VALUES ('Smoke Test Anthem', 'The Migrations', NULL, 'cc0', :'user_id')
RETURNING id AS song_id \gset

INSERT INTO song_versions
    (song_id, version_no, manifest_sha256, storage_key, size_bytes,
     timing_points, canonical_score_key, published_at)
VALUES (:'song_id', 1, repeat('b', 64), 'songs/smoke/v1.kan', 10485760,
        '[{"time_ms": 0, "bpm": 120.0}]'::jsonb, 'songs/smoke/v1.score', now())
RETURNING id AS version_id \gset

INSERT INTO charts
    (song_version_id, instrument_pack_id, difficulty, chart_key, storage_key,
     size_bytes, mapper_id, status)
VALUES (:'version_id', :'pack_id', 'hard', 'guitar_hard',
        'charts/smoke/guitar_hard.kchart', 40960, :'user_id', 'ranked')
RETURNING id AS chart_id \gset

-- gameplay ------------------------------------------------------------------
INSERT INTO scores
    (chart_id, user_id, instrument_family, input_method, mod_mask,
     total_score, max_combo, count_perfect, count_great, count_ok, count_miss,
     accuracy_cents, timing_avg_ms, timing_dev_ms, kp, client_hash, validity)
VALUES (:'chart_id', :'user_id', 'plucked', 'mic', 0,
        987654, 210, 200, 8, 2, 0,
        7.35, -1.20, 12.50, 145.678, repeat('c', 64), 'valid')
RETURNING id AS score_id \gset

INSERT INTO replays (score_id, chart_id, storage_key, size_bytes, curves_key)
VALUES (:'score_id', :'chart_id', 'replays/smoke/1.bin', 20480,
        'replays/smoke/1.curves.bin');

INSERT INTO kp_stats (user_id, kp, global_rank, country_rank, instrument_ranks)
VALUES (:'user_id', 145.678, 1, 1,
        '{"plucked": {"kp": 145.678, "rank": 1}}'::jsonb);

INSERT INTO leaderboard_entries
    (chart_id, input_method, mod_mask, user_id, score_id, kp, rank_in_board)
VALUES (:'chart_id', 'mic', 0, :'user_id', :'score_id', 145.678, 1);

-- trust ---------------------------------------------------------------------
INSERT INTO cheat_verdicts
    (user_id, score_id, score_chart_id, classifier_version, verdict,
     confidence, evidence)
VALUES (:'user_id', :'score_id', :'chart_id', 'synthclf-0.1.0', 'clean',
        0.970, '{"jitter_ms": 3.1}'::jsonb);

INSERT INTO audit_events (actor_id, action, entity_type, entity_id, payload)
VALUES (:'user_id', 'chart.status.ranked', 'chart', :'chart_id',
        '{"votes": 2, "vetoes": 0}'::jsonb);

-- billing -------------------------------------------------------------------
INSERT INTO donations (user_id, provider, provider_ref, amount_cents,
                       currency, recurring, status)
VALUES (:'user_id', 'stripe', 'evt_smoke_001', 500, 'EUR', true, 'completed');

-- read back -------------------------------------------------------------------
SELECT 'users' AS t, count(*) FROM users
UNION ALL SELECT 'user_credentials', count(*) FROM user_credentials
UNION ALL SELECT 'supporter_entitlements', count(*) FROM supporter_entitlements
UNION ALL SELECT 'instrument_packs', count(*) FROM instrument_packs
UNION ALL SELECT 'songs', count(*) FROM songs
UNION ALL SELECT 'song_versions', count(*) FROM song_versions
UNION ALL SELECT 'charts', count(*) FROM charts
UNION ALL SELECT 'scores', count(*) FROM scores
UNION ALL SELECT 'replays', count(*) FROM replays
UNION ALL SELECT 'kp_stats', count(*) FROM kp_stats
UNION ALL SELECT 'leaderboard_entries', count(*) FROM leaderboard_entries
UNION ALL SELECT 'cheat_verdicts', count(*) FROM cheat_verdicts
UNION ALL SELECT 'audit_events', count(*) FROM audit_events
UNION ALL SELECT 'donations', count(*) FROM donations;

-- Partitioning sanity: the score row must live in a hash partition, and the
-- audit row in this month's range partition.
SELECT tableoid::regclass AS score_partition FROM scores;
SELECT tableoid::regclass AS audit_partition FROM audit_events;

-- UUIDv7 sanity: version nibble must be 7.
SELECT substring(id::text, 15, 1) = '7' AS uuid_is_v7 FROM users;

-- Append-only sanity: these must be no-ops (rules swallow them).
UPDATE audit_events SET action = 'tampered';
DELETE FROM audit_events;
SELECT count(*) AS audit_still_one FROM audit_events;

COMMIT;
