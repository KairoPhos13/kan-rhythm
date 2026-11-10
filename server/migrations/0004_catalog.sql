-- 0004_catalog.sql: instrument packs, songs, song versions, charts.
-- Implements docs/architecture/backend-data-model.md §3 "instruments" and
-- "catalog" exactly. Blobs live in object storage; the DB stores keys +
-- SHA-256 only (never bytes).

CREATE TABLE instrument_packs (
    id             uuid PRIMARY KEY DEFAULT uuid_v7(),
    slug           text NOT NULL UNIQUE
                   CHECK (slug ~ '^[a-z0-9\-]{2,64}$'),
    name           text NOT NULL,
    family         instrument_family NOT NULL,
    format_version text NOT NULL
                   CHECK (format_version ~ '^\d+\.\d+\.\d+$'),  -- semver
    manifest_sha256 char(64) NOT NULL
                   CHECK (manifest_sha256 ~ '^[0-9a-f]{64}$'),
    storage_key    text NOT NULL,
    uploaded_by    uuid NOT NULL REFERENCES users (id),
    visibility     pack_visibility NOT NULL DEFAULT 'public',
    created_at     timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE songs (
    id                     uuid PRIMARY KEY DEFAULT uuid_v7(),
    title                  text NOT NULL,
    artist                 text NOT NULL,
    source                 text,
    license                song_license NOT NULL,
    audio_license_evidence text,       -- required by review when 'licensed'
    created_by             uuid NOT NULL REFERENCES users (id),
    ranked_status          ranked_status NOT NULL DEFAULT 'unranked',
    created_at             timestamptz NOT NULL DEFAULT now(),
    updated_at             timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE song_versions (
    id                  uuid PRIMARY KEY DEFAULT uuid_v7(),
    song_id             uuid NOT NULL REFERENCES songs (id) ON DELETE CASCADE,
    version_no          integer NOT NULL CHECK (version_no > 0),
    manifest_sha256     char(64) NOT NULL
                        CHECK (manifest_sha256 ~ '^[0-9a-f]{64}$'),
    storage_key         text NOT NULL,
    size_bytes          bigint NOT NULL CHECK (size_bytes > 0),
    timing_points       jsonb NOT NULL DEFAULT '[]'::jsonb,
                        -- Layer 1 BPM snapshots: [{time_ms, bpm}, ...]
    canonical_score_key text,
                        -- Layer 2 blob in object storage
    published_at        timestamptz,
    UNIQUE (song_id, version_no)
);

CREATE TABLE charts (
    id                 uuid PRIMARY KEY DEFAULT uuid_v7(),
    song_version_id    uuid NOT NULL REFERENCES song_versions (id),
    instrument_pack_id uuid NOT NULL REFERENCES instrument_packs (id),
    difficulty         chart_difficulty NOT NULL,
    chart_key          text NOT NULL,           -- e.g. "guitar_hard"
    storage_key        text NOT NULL,
    size_bytes         integer NOT NULL
                       CHECK (size_bytes > 0 AND size_bytes < 51200),
                       -- ROADMAP M4 GATE: new-instrument arrangement < 50 KB
    mapper_id          uuid NOT NULL REFERENCES users (id),
    status             ranked_status NOT NULL DEFAULT 'unranked',
    created_at         timestamptz NOT NULL DEFAULT now(),
    UNIQUE (song_version_id, instrument_pack_id, chart_key)
);

CREATE INDEX charts_status_idx ON charts (status);
CREATE INDEX songs_ranked_status_idx ON songs (ranked_status);
