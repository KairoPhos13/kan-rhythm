-- 0003_identity.sql: users, credentials, supporter entitlements.
-- Implements docs/architecture/backend-data-model.md §3 "identity" exactly.
-- PII-minimal: no real names; email only for account recovery/notifications.

CREATE TABLE users (
    id             uuid PRIMARY KEY DEFAULT uuid_v7(),
    username       citext NOT NULL UNIQUE
                   CHECK (char_length(username::text) BETWEEN 3 AND 24),
    email          citext NOT NULL UNIQUE,
    country        char(2) NOT NULL DEFAULT '??',   -- ISO 3166-1 alpha-2
    created_at     timestamptz NOT NULL DEFAULT now(),
    disabled_at    timestamptz,                     -- ban/disable marker
    avatar_key     text,                            -- object-storage key
    profile_bg_key text
);

-- OAuth-first: one row per (user, provider). secret_hash is NULL for pure
-- OAuth identities and only set for the local 'kan' provider.
CREATE TABLE user_credentials (
    user_id     uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
    provider    credential_provider NOT NULL,
    external_id text NOT NULL,       -- provider-scoped subject id
    secret_hash text,                -- argon2id / scrypt hash; NULL for OAuth
    created_at  timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (user_id, provider),
    UNIQUE (provider, external_id)
);

CREATE TABLE supporter_entitlements (
    id          uuid PRIMARY KEY DEFAULT uuid_v7(),
    user_id     uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
    tier        supporter_tier NOT NULL,
    source      entitlement_source NOT NULL,
    valid_from  timestamptz NOT NULL DEFAULT now(),
    valid_until timestamptz,                    -- NULL = open-ended
    receipt_ref text,                           -- provider token only, never
                                                -- card data (PCI-free design)
    created_at  timestamptz NOT NULL DEFAULT now(),
    CHECK (valid_until IS NULL OR valid_until > valid_from)
);

-- Lookup active entitlements per user; the "is it active right now" filter is
-- applied in queries (now() is not immutable, so no partial index).
CREATE INDEX supporter_entitlements_user_idx
    ON supporter_entitlements (user_id, valid_until);
