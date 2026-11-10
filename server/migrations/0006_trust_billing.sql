-- 0006_trust_billing.sql: cheat_verdicts, audit_events (range-partitioned,
-- append-only), donations. Implements docs/architecture/backend-data-model.md
-- §3 "trust" and "billing" exactly.
--
-- PII-minimal / PCI-free: donations reference provider tokens only
-- (provider + provider_ref). No card data, ever.

CREATE TABLE cheat_verdicts (
    id                 uuid PRIMARY KEY DEFAULT uuid_v7(),
    user_id            uuid NOT NULL REFERENCES users (id),
    score_id           uuid,                 -- NULL for account-level verdicts
    score_chart_id     uuid,                 -- partition-FK companion column
    classifier_version text NOT NULL,
    verdict            cheat_verdict NOT NULL,
    confidence         numeric(4,3) NOT NULL
                       CHECK (confidence >= 0 AND confidence <= 1),
    evidence           jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at         timestamptz NOT NULL DEFAULT now(),
    CHECK ((score_id IS NULL) = (score_chart_id IS NULL)),
    FOREIGN KEY (score_id, score_chart_id)
        REFERENCES scores (id, chart_id) ON DELETE SET NULL
);

CREATE INDEX cheat_verdicts_user_idx ON cheat_verdicts (user_id, created_at);

-- Append-only audit log, range-partitioned by month (backend-data-model §3).
-- Partitioned PK must include the partition column: (id, created_at).
CREATE TABLE audit_events (
    id          bigint GENERATED ALWAYS AS IDENTITY,
    actor_id    uuid,                  -- NULL = system actor
    action      text NOT NULL,
    entity_type text NOT NULL,
    entity_id   text NOT NULL,
    payload     jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at  timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (id, created_at)
) PARTITION BY RANGE (created_at);

CREATE INDEX audit_events_entity_idx ON audit_events (entity_type, entity_id);
CREATE INDEX audit_events_actor_idx ON audit_events (actor_id, created_at);

-- Append-only enforcement: no UPDATE/DELETE through SQL. (The ingest role
-- should additionally be granted INSERT/SELECT only; see migrations README.)
CREATE RULE audit_events_no_update AS ON UPDATE TO audit_events
    DO INSTEAD NOTHING;
CREATE RULE audit_events_no_delete AS ON DELETE TO audit_events
    DO INSTEAD NOTHING;

-- Monthly partitions: helper first (cron/ops uses it to extend the range),
-- then a DEFAULT partition as a safety net, then the launch window up front.
CREATE OR REPLACE FUNCTION audit_events_ensure_partition(month_start date)
RETURNS text
LANGUAGE plpgsql AS $$
DECLARE
    part_name text := 'audit_events_' || to_char(month_start, 'YYYY_MM');
    next_month date := (month_start + interval '1 month')::date;
BEGIN
    IF to_regclass(part_name) IS NULL THEN
        -- Fails loudly if DEFAULT already holds rows for this month; ops must
        -- move them first. With an empty DEFAULT this is a cheap no-op scan.
        EXECUTE format(
            'CREATE TABLE %I PARTITION OF audit_events
             FOR VALUES FROM (%L) TO (%L)', part_name, month_start, next_month);
    END IF;
    RETURN part_name;
END
$$;

CREATE TABLE audit_events_default PARTITION OF audit_events DEFAULT;

DO $$
DECLARE
    m date;
BEGIN
    m := date_trunc('month', now())::date;
    WHILE m < (date_trunc('month', now()) + interval '15 months')::date LOOP
        PERFORM audit_events_ensure_partition(m);
        m := (m + interval '1 month')::date;
    END LOOP;
END
$$;

CREATE TABLE donations (
    id           uuid PRIMARY KEY DEFAULT uuid_v7(),
    user_id      uuid REFERENCES users (id) ON DELETE SET NULL,
                                 -- NULL = anonymous donation (privacy option)
    provider     text NOT NULL,  -- e.g. 'stripe', 'paypal' (ADR pending)
    provider_ref text NOT NULL UNIQUE,
                                 -- provider event/charge id; dedup key for
                                 -- at-least-once webhooks. No card data.
    amount_cents integer NOT NULL CHECK (amount_cents > 0),
    currency     char(3) NOT NULL,        -- ISO 4217
    recurring    boolean NOT NULL DEFAULT false,
    status       donation_status NOT NULL DEFAULT 'pending',
    created_at   timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX donations_user_idx ON donations (user_id, created_at);
