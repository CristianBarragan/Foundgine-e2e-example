-- CRM owns the CRM customer id; the ERP owns the ERP customer id. The relationship lives here.
CREATE TABLE customer_integration_mappings (
    crm_customer_id integer PRIMARY KEY REFERENCES customers (customer_id),
    tenant_id       varchar(64)  NOT NULL,
    erp_customer_id varchar(64),
    status          varchar(16)  NOT NULL CHECK (status IN ('Pending', 'Active', 'Failed')),
    attempt         integer      NOT NULL DEFAULT 1,
    last_error      text,
    created_at      timestamptz  NOT NULL,
    updated_at      timestamptz  NOT NULL
);
CREATE UNIQUE INDEX ux_mappings_tenant_erp ON customer_integration_mappings (tenant_id, erp_customer_id)
    WHERE erp_customer_id IS NOT NULL;

-- Transactional outbox: written in the same transaction as the business change.
CREATE TABLE outbox_messages (
    id              uuid PRIMARY KEY,
    tenant_id       varchar(64)  NOT NULL,
    type            varchar(100) NOT NULL,
    aggregate_id    integer      NOT NULL,
    attempt         integer      NOT NULL DEFAULT 1,
    payload         jsonb        NOT NULL,
    created_at      timestamptz  NOT NULL,
    published_at    timestamptz,
    publish_attempts integer     NOT NULL DEFAULT 0,
    next_attempt_at timestamptz  NOT NULL DEFAULT now(),
    last_error      text
);
CREATE INDEX ix_outbox_unpublished ON outbox_messages (next_attempt_at) WHERE published_at IS NULL;
