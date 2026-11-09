-- CRM commercial-order aggregate. Apply AFTER Outbox.sql (the producer writes to foundgine_outbox
-- in the same transaction) and after the EF AddInitial migration (FK to crm_companies).
-- Owned by the CRM bounded context; ERP never reads or writes these tables.

CREATE TABLE IF NOT EXISTS crm_commercial_orders (
  id                  uuid PRIMARY KEY,
  tenant_id           text NOT NULL,
  order_number        text NOT NULL,
  company_id          uuid NOT NULL,
  currency            char(3) NOT NULL,
  status              text NOT NULL DEFAULT 'Accepted',
  failure_reason      text NULL,
  erp_reference       text NULL,          -- opaque ERP reservation reference (ERP is authoritative)
  client_request_key  text NOT NULL,      -- caller Idempotency-Key for POST /api/commercial-orders
  request_fingerprint char(64) NOT NULL,  -- SHA-256 of the normalized request
  created_at          timestamptz NOT NULL DEFAULT now(),
  updated_at          timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT ck_crm_commercial_orders_status
    CHECK (status IN ('Accepted','FulfillmentConfirmed','FulfillmentFailed')),
  CONSTRAINT uq_crm_commercial_orders_tenant_id UNIQUE (tenant_id, id),
  CONSTRAINT uq_crm_commercial_orders_tenant_number UNIQUE (tenant_id, order_number),
  CONSTRAINT uq_crm_commercial_orders_tenant_key UNIQUE (tenant_id, client_request_key),
  CONSTRAINT fk_crm_commercial_orders_company FOREIGN KEY (tenant_id, company_id)
    REFERENCES crm_companies (tenant_id, id) ON DELETE RESTRICT
);
CREATE INDEX IF NOT EXISTS ix_crm_commercial_orders_tenant_created
  ON crm_commercial_orders (tenant_id, created_at DESC);

CREATE TABLE IF NOT EXISTS crm_commercial_order_lines (
  tenant_id       text NOT NULL,
  order_id        uuid NOT NULL,
  line_no         integer NOT NULL,
  product_id      text NOT NULL,          -- ERP-owned product reference, passed through unchanged
  quantity        numeric(18,3) NOT NULL,
  unit_of_measure text NOT NULL,
  PRIMARY KEY (order_id, line_no),
  CONSTRAINT ck_crm_commercial_order_lines_qty CHECK (quantity > 0),
  CONSTRAINT fk_crm_commercial_order_lines_order FOREIGN KEY (tenant_id, order_id)
    REFERENCES crm_commercial_orders (tenant_id, id) ON DELETE CASCADE
);

-- Inbound callback de-duplication (Temporal activities retry; every command carries a stable key).
CREATE TABLE IF NOT EXISTS crm_integration_inbox (
  tenant_id        text NOT NULL,
  idempotency_key  text NOT NULL,
  order_id         uuid NOT NULL,
  outcome          text NOT NULL,
  received_at      timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (tenant_id, idempotency_key),
  CONSTRAINT ck_crm_integration_inbox_outcome CHECK (outcome IN ('FulfillmentConfirmed','FulfillmentFailed')),
  CONSTRAINT fk_crm_integration_inbox_order FOREIGN KEY (tenant_id, order_id)
    REFERENCES crm_commercial_orders (tenant_id, id) ON DELETE CASCADE
);
