-- 0005_gameplay.sql — scores (hash-partitioned), replays, kp_stats,
-- leaderboard_entries. Implements docs/architecture/backend-data-model.md
-- §3 "gameplay" and §6 scalability decisions:
--
--   * scores is PARTITION BY HASH (chart_id) with 32 partitions day one.
--   * Per-board reads hit the precomputed leaderboard_entries table, never
--     scan scores.
--
-- Partitioning note (deliberate, minimal deviation from the sketch): a
-- partitioned table's PRIMARY KEY must include the partition column, so
-- scores' PK is (id, chart_id) rather than (id) alone; UUIDv7 keeps ids
-- globally unique in practice, and child tables (replays, leaderboard_entries,
-- cheat_verdicts) carry chart_id alongside score_id to form composite foreign
-- keys into the partitioned parent (supported since PostgreSQL 12).

CREATE TABLE scores (
    id               uuid NOT NULL DEFAULT uuid_v7(),
    chart_id         uuid NOT NULL REFERENCES charts (id),
    user_id          uuid NOT NULL REFERENCES users (id),
    instrument_family instrument_family NOT NULL,
    input_method     input_method NOT NULL,
    mod_mask         bigint NOT NULL DEFAULT 0,
    total_score      integer NOT NULL CHECK (total_score >= 0),
    max_combo        integer NOT NULL CHECK (max_combo >= 0),
    count_perfect    integer NOT NULL CHECK (count_perfect >= 0),
    count_great      integer NOT NULL CHECK (count_great >= 0),
    count_ok         integer NOT NULL CHECK (count_ok >= 0),
    count_miss       integer NOT NULL CHECK (count_miss >= 0),
    accuracy_cents   numeric(6,2) NOT NULL
                     CHECK (accuracy_cents >= 0),   -- intonation purity axis
    timing_avg_ms    numeric(6,2) NOT NULL,
    timing_dev_ms    numeric(6,2) NOT NULL CHECK (timing_dev_ms >= 0),
    kp               numeric(8,3),                -- NULL until ranked+valid
    client_hash      char(64) NOT NULL
                     CHECK (client_hash ~ '^[0-9a-f]{64}$'),
                                              -- signed build fingerprint
    submitted_at     timestamptz NOT NULL DEFAULT now(),
    validity         score_validity NOT NULL DEFAULT 'pending',
    PRIMARY KEY (id, chart_id)
) PARTITION BY HASH (chart_id);

-- 32 hash partitions, day one (backend-data-model §6).
DO $$
BEGIN
    FOR i IN 0..31 LOOP
        EXECUTE format(
            'CREATE TABLE scores_p%s PARTITION OF scores FOR VALUES
             WITH (MODULUS 32, REMAINDER %s)', i, i);
    END LOOP;
END
$$;

-- Write path (validation pipeline) and profile reads.
CREATE INDEX scores_user_submitted_idx ON scores (user_id, submitted_at DESC);
CREATE INDEX scores_validity_idx ON scores (validity, submitted_at)
    WHERE validity = 'pending';
CREATE INDEX scores_chart_board_idx ON scores (chart_id, input_method, mod_mask)
    WHERE validity = 'valid';

-- Replays store only lightweight player data (ROADMAP M4): the micro-timing
-- and cents curves, not audio. score_id is the PK as in the sketch; chart_id
-- is carried solely to form the composite FK into the partitioned parent.
CREATE TABLE replays (
    score_id    uuid PRIMARY KEY,
    chart_id    uuid NOT NULL,
    storage_key text NOT NULL,
    size_bytes  bigint NOT NULL CHECK (size_bytes > 0),
    curves_key  text NOT NULL,   -- micro-timing + cents curves blob
    created_at  timestamptz NOT NULL DEFAULT now(),
    FOREIGN KEY (score_id, chart_id) REFERENCES scores (id, chart_id)
        ON DELETE CASCADE
);

-- Per-user ranking summary; recomputed asynchronously (ADR-0008: weighted
-- top-N, w = 0.95, N = 100, dual-axis mix).
CREATE TABLE kp_stats (
    user_id          uuid PRIMARY KEY REFERENCES users (id) ON DELETE CASCADE,
    kp               numeric(8,3) NOT NULL DEFAULT 0,
    global_rank      integer,
    country_rank     integer,
    instrument_ranks jsonb NOT NULL DEFAULT '{}'::jsonb,
                   -- { "<family>": { "kp": <num>, "rank": <int> }, ... }
    updated_at       timestamptz NOT NULL DEFAULT now()
);

-- Precomputed boards: board key = (chart_id, input_method, mod_mask)
-- (ROADMAP M4 fairness gate). rank_in_board is dense from 1; a full board
-- re-sort is a transactional swap performed by the kp module.
CREATE TABLE leaderboard_entries (
    chart_id      uuid NOT NULL,
    input_method  input_method NOT NULL,
    mod_mask      bigint NOT NULL DEFAULT 0,
    user_id       uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
    score_id      uuid NOT NULL,
    kp            numeric(8,3) NOT NULL,
    rank_in_board integer NOT NULL CHECK (rank_in_board > 0),
    updated_at    timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (chart_id, input_method, mod_mask, rank_in_board),
    UNIQUE (score_id),     -- one score occupies at most one board slot
    FOREIGN KEY (score_id, chart_id) REFERENCES scores (id, chart_id)
        ON DELETE CASCADE
);

CREATE INDEX leaderboard_entries_user_idx
    ON leaderboard_entries (user_id);
