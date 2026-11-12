-- Score content is the client's own claim and is validated before it is
-- stored (src/scores/score-rules.ts, audit S6). These constraints are the
-- database's own backstop. They are added NOT VALID: rows written before this
-- migration stay as they are and new writes are enforced. Run
--   ALTER TABLE scores VALIDATE CONSTRAINT <name>;
-- one by one once the existing rows have been checked (they were written by
-- the trusted client, so they should validate straight away).

ALTER TABLE scores
    ADD CONSTRAINT scores_accuracy_range
        CHECK (accuracy >= 0 AND accuracy <= 1) NOT VALID,
    ADD CONSTRAINT scores_total_score_range
        CHECK (total_score >= 0 AND total_score <= 1000000000000) NOT VALID,
    ADD CONSTRAINT scores_total_score_without_mods_range
        CHECK (total_score_without_mods >= 0 AND total_score_without_mods <= 1000000000000) NOT VALID,
    ADD CONSTRAINT scores_max_combo_range
        CHECK (max_combo >= 0 AND max_combo <= 10000000) NOT VALID,
    ADD CONSTRAINT scores_rank_known
        CHECK (rank IN ('XH', 'X', 'SH', 'S', 'A', 'B', 'C', 'D')) NOT VALID;
