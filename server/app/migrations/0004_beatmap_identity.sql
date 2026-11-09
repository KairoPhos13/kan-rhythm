-- Difficulty identity is (beatmapset_id, version, mode_int): an archive can
-- carry two difficulties with the same Version in different modes (e.g.
-- "241526 Soleily - Renatus.osz" has "Normal" in mode 0 and mode 3) and those
-- are distinct difficulties. The mode column already exists (0002); this
-- migration pins the identity down at the database level.

CREATE UNIQUE INDEX beatmaps_set_version_mode_key
    ON beatmaps (beatmapset_id, version, mode_int);
