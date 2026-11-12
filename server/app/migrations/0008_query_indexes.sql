-- Indexes for the queries that grew past a full-table scan (audit S5, S9).
--
--  - users.join_date: counted on every registration attempt (the daily
--    account ceiling, src/users/users.service.ts).
--  - scores (beatmap, ruleset, user, score, created_at): the leaderboard
--    picks every user's best score with DISTINCT ON (user_id) ordered by
--    score (src/scores/score-queries.ts).
--
-- The search index needs pg_trgm, which not every database role may create:
-- it is attempted and skipped with a NOTICE when the extension is not
-- allowed, so the migration can never block a deployment. Search stays
-- correct without it, only slower on a big catalogue.

CREATE INDEX users_join_date_idx ON users (join_date);

CREATE INDEX scores_beatmap_user_idx
    ON scores (beatmap_id, ruleset_short_name, user_id, total_score DESC, created_at ASC, id ASC);

DO $$
BEGIN
    CREATE EXTENSION IF NOT EXISTS pg_trgm;
    CREATE INDEX beatmapsets_title_trgm_idx
        ON beatmapsets USING gin (title gin_trgm_ops, title_unicode gin_trgm_ops);
    CREATE INDEX beatmapsets_artist_trgm_idx
        ON beatmapsets USING gin (artist gin_trgm_ops, artist_unicode gin_trgm_ops);
    CREATE INDEX beatmapsets_creator_trgm_idx
        ON beatmapsets USING gin (creator gin_trgm_ops);
    CREATE INDEX beatmapsets_source_trgm_idx
        ON beatmapsets USING gin (source gin_trgm_ops);
    CREATE INDEX beatmapsets_tags_trgm_idx
        ON beatmapsets USING gin (tags gin_trgm_ops);
EXCEPTION WHEN OTHERS THEN
    RAISE NOTICE 'pg_trgm search indexes skipped: %', SQLERRM;
END $$;
