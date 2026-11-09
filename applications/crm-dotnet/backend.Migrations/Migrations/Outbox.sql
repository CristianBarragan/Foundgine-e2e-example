-- Apply in the CRM database BEFORE CommercialOrders.sql. This is Foundgine.Providers' own outbox schema
-- (Foundgine.Providers/EventStore/Schema/003_outbox.sql, 2.3.1); CommercialOrderService enqueues into it inside
-- the order-acceptance transaction and the Temporal dispatcher host leases from it. It replaces integration_outbox.
CREATE TABLE IF NOT EXISTS foundgine_outbox
(
    tenant_id        varchar(200) NOT NULL,
    id               uuid         NOT NULL,
    topic            varchar(200) NOT NULL,
    message_key      varchar(200) NULL,
    payload          jsonb        NOT NULL,
    headers          jsonb        NOT NULL DEFAULT '{}'::jsonb,
    correlation_id   varchar(200) NULL,
    created_at       timestamptz  NOT NULL,
    available_at     timestamptz  NOT NULL,
    attempts         integer      NOT NULL DEFAULT 0,
    locked_until     timestamptz  NULL,
    lock_token       uuid         NULL,
    completed_at     timestamptz  NULL,
    dead_lettered_at timestamptz  NULL,
    last_error       varchar(2000) NULL,
    PRIMARY KEY (tenant_id, id)
);
CREATE INDEX IF NOT EXISTS ix_foundgine_outbox_due
    ON foundgine_outbox (available_at, created_at)
    WHERE completed_at IS NULL AND dead_lettered_at IS NULL;
CREATE INDEX IF NOT EXISTS ix_foundgine_outbox_completed
    ON foundgine_outbox (completed_at)
    WHERE completed_at IS NOT NULL;
