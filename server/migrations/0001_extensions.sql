-- 0001_extensions.sql: required PostgreSQL extensions.
-- Target: PostgreSQL 16+ (validated on postgres:16-alpine).
--
-- citext  : case-insensitive usernames/emails (PostgreSQL License)
-- pgcrypto: gen_random_bytes() for the UUIDv7 generator in 0002
--           (PG 13+ also has core gen_random_bytes via pgcrypto only;
--            gen_random_uuid() is core but not time-ordered)

CREATE EXTENSION IF NOT EXISTS citext;
CREATE EXTENSION IF NOT EXISTS pgcrypto;
