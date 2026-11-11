-- Beatmap ids reserved by the editor's submission flow
-- (PUT /beatmap-submission/beatmapsets, server/docs/lazer-protocol.md §4.8).
--
-- The client writes the reserved ids into the exported package before it is
-- uploaded, so the server must hand out ids before it has seen the files. A
-- reservation is a beatmaps.id value allocated from the same identity sequence
-- (so the two can never collide later) and claimed by the first uploaded
-- difficulty that carries it. Reservations left over from an abandoned
-- submission are dropped by the hourly purge (src/db/purge.service.ts).

CREATE TABLE beatmap_id_reservations (
    id            integer PRIMARY KEY,
    beatmapset_id integer      NOT NULL REFERENCES beatmapsets (id) ON DELETE CASCADE,
    created_at    timestamptz  NOT NULL DEFAULT now()
);

CREATE INDEX beatmap_id_reservations_set_idx ON beatmap_id_reservations (beatmapset_id);
