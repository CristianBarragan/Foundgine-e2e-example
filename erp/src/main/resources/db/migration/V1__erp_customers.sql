-- ERP owns the ERP customer id. The CRM id is kept only as the natural idempotency key of the onboarding saga.
CREATE SEQUENCE erp_customer_seq START 9001;

CREATE TABLE erp_customers (
    erp_customer_id varchar(64)  PRIMARY KEY,
    tenant_id       varchar(64)  NOT NULL,
    crm_customer_id integer      NOT NULL,
    kind            varchar(16)  NOT NULL CHECK (kind IN ('Business', 'Individual')),
    name            varchar(200) NOT NULL,
    email           varchar(320) NOT NULL DEFAULT '',
    phone           varchar(50)  NOT NULL DEFAULT '',
    status          varchar(16)  NOT NULL CHECK (status IN ('Active', 'Inactive')),
    created_at      timestamptz  NOT NULL DEFAULT now(),
    CONSTRAINT ux_erp_customers_tenant_crm UNIQUE (tenant_id, crm_customer_id)
);

-- Inbox: one row per processed onboarding message (correlation id), for traceability and duplicate detection.
CREATE TABLE customer_inbox (
    tenant_id       varchar(64) NOT NULL,
    correlation_id  varchar(64) NOT NULL,
    crm_customer_id integer     NOT NULL,
    erp_customer_id varchar(64) NOT NULL REFERENCES erp_customers (erp_customer_id),
    attempt         integer     NOT NULL,
    received_at     timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (tenant_id, correlation_id)
);
