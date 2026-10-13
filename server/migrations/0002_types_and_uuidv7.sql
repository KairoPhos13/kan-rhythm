-- 0002_types_and_uuidv7.sql — shared enum types and the UUIDv7 generator.
--
-- Per docs/architecture/backend-data-model.md §3: identifiers are UUIDv7
-- (time-ordered — chosen for scores/leaderboards locality). PostgreSQL 18
-- ships a built-in uuidv7(); this function is the portable equivalent for
-- PG 16/17 and can be dropped when the fleet moves to PG 18.
-- Layout: RFC 9562 — 48-bit unix-ms timestamp, version nibble 0x7,
-- variant bits 10, remaining 74 bits from gen_random_bytes().

CREATE OR REPLACE FUNCTION uuid_v7() RETURNS uuid
LANGUAGE plpgsql VOLATILE PARALLEL SAFE AS $$
DECLARE
    ts_ms bigint;
    b     bytea;
BEGIN
    ts_ms := floor(extract(epoch FROM clock_timestamp()) * 1000)::bigint;
    b := gen_random_bytes(16);
    -- 48-bit big-endian millisecond timestamp into bytes 0..5
    b := set_byte(b, 0, ((ts_ms >> 40) & 255)::int);
    b := set_byte(b, 1, ((ts_ms >> 32) & 255)::int);
    b := set_byte(b, 2, ((ts_ms >> 24) & 255)::int);
    b := set_byte(b, 3, ((ts_ms >> 16) & 255)::int);
    b := set_byte(b, 4, ((ts_ms >> 8)  & 255)::int);
    b := set_byte(b, 5, ( ts_ms        & 255)::int);
    -- version 7: high nibble of byte 6
    b := set_byte(b, 6, ((get_byte(b, 6) & 15) | 112)::int);
    -- RFC 4122 variant 10xx: high bits of byte 8
    b := set_byte(b, 8, ((get_byte(b, 8) & 63) | 128)::int);
    RETURN encode(b, 'hex')::uuid;
END
$$;

-- identity -----------------------------------------------------------------
CREATE TYPE credential_provider AS ENUM ('kan', 'steam', 'google', 'discord');
CREATE TYPE entitlement_source AS ENUM ('donation', 'code');
-- Tier vocabulary is owned by donations/; kept minimal until the
-- donation-provider ADR lands.
CREATE TYPE supporter_tier AS ENUM ('supporter', 'supporter_plus');

-- instruments --------------------------------------------------------------
CREATE TYPE instrument_family AS ENUM
    ('plucked', 'bowed', 'wind', 'keyboard', 'percussion', 'other');
CREATE TYPE pack_visibility AS ENUM ('public', 'unlisted', 'private');

-- catalog ------------------------------------------------------------------
CREATE TYPE song_license AS ENUM
    ('cc0', 'cc-by', 'cc-by-sa', 'cc-by-nc', 'original', 'licensed');
CREATE TYPE ranked_status AS ENUM
    ('unranked', 'pending', 'ranked', 'loved', 'graveyard');
CREATE TYPE chart_difficulty AS ENUM
    ('easy', 'normal', 'hard', 'insane', 'expert', 'expertplus');

-- gameplay -----------------------------------------------------------------
CREATE TYPE input_method AS ENUM ('mic', 'line_in', 'midi');
CREATE TYPE score_validity AS ENUM ('pending', 'valid', 'invalid', 'cheat');

-- trust --------------------------------------------------------------------
CREATE TYPE cheat_verdict AS ENUM
    ('clean', 'suspicious', 'synthetic_injection');

-- billing ------------------------------------------------------------------
CREATE TYPE donation_status AS ENUM
    ('pending', 'completed', 'failed', 'refunded', 'cancelled');
