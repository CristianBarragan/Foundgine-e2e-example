-- Customer returns (Phase 8, ERP side). The ERP owns the ERP return id; the CRM return id is the idempotency key.
CREATE SEQUENCE erp_return_seq START 7001;

CREATE TABLE erp_returns (
    erp_return_id  varchar(64)  PRIMARY KEY,
    tenant_id      varchar(64)  NOT NULL,
    crm_return_id  integer      NOT NULL,
    erp_order_id   varchar(64)  NOT NULL REFERENCES sales_orders (erp_order_id),
    status         varchar(16)  NOT NULL CHECK (status IN ('Authorized', 'Rejected', 'Completed')),
    reason         varchar(500) NOT NULL DEFAULT '',
    note           varchar(500) NOT NULL DEFAULT '',
    correlation_id varchar(64)  NOT NULL,
    created_at     timestamptz  NOT NULL DEFAULT now(),
    updated_at     timestamptz  NOT NULL DEFAULT now(),
    CONSTRAINT ux_erp_returns_tenant_crm UNIQUE (tenant_id, crm_return_id)
);

CREATE TABLE erp_return_lines (
    erp_return_id varchar(64) NOT NULL REFERENCES erp_returns (erp_return_id),
    sku           varchar(64) NOT NULL,
    quantity      integer     NOT NULL CHECK (quantity > 0),
    PRIMARY KEY (erp_return_id, sku)
);
CREATE INDEX ix_erp_returns_order ON erp_returns (tenant_id, erp_order_id);
