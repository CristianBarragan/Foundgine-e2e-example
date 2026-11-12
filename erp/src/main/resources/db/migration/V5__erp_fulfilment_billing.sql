CREATE SEQUENCE erp_shipment_seq START 1;
CREATE SEQUENCE erp_invoice_seq START 1;
CREATE SEQUENCE erp_payment_seq START 1;

CREATE TABLE erp_shipments (
    erp_shipment_id varchar(64) PRIMARY KEY,
    tenant_id varchar(64) NOT NULL,
    erp_order_id varchar(64) NOT NULL REFERENCES sales_orders(erp_order_id),
    status varchar(16) NOT NULL CHECK (status IN ('Packed','Shipped','InTransit','Delivered','Cancelled')),
    carrier varchar(120),
    tracking_number varchar(120),
    version integer NOT NULL DEFAULT 1 CHECK (version > 0),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ux_erp_shipment_tenant_order UNIQUE (tenant_id, erp_shipment_id)
);
CREATE INDEX ix_erp_shipments_order ON erp_shipments(tenant_id, erp_order_id);

CREATE TABLE erp_invoices (
    erp_invoice_id varchar(64) PRIMARY KEY,
    tenant_id varchar(64) NOT NULL,
    erp_order_id varchar(64) NOT NULL REFERENCES sales_orders(erp_order_id),
    invoice_number varchar(64) NOT NULL,
    total numeric(14,2) NOT NULL CHECK (total >= 0),
    status varchar(16) NOT NULL CHECK (status IN ('Issued','PartiallyPaid','Paid','Overdue','Void')),
    issued_at timestamptz NOT NULL,
    due_at timestamptz,
    version integer NOT NULL DEFAULT 1 CHECK (version > 0),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ux_erp_invoice_tenant_id UNIQUE (tenant_id, erp_invoice_id),
    CONSTRAINT ux_erp_invoice_tenant_number UNIQUE (tenant_id, invoice_number),
    CONSTRAINT ux_erp_invoice_order UNIQUE (tenant_id, erp_order_id)
);

CREATE TABLE erp_payments (
    erp_payment_id varchar(64) PRIMARY KEY,
    tenant_id varchar(64) NOT NULL,
    erp_order_id varchar(64) NOT NULL REFERENCES sales_orders(erp_order_id),
    erp_invoice_id varchar(64) NOT NULL REFERENCES erp_invoices(erp_invoice_id),
    amount numeric(14,2) NOT NULL CHECK (amount > 0),
    status varchar(16) NOT NULL CHECK (status IN ('Received','Reconciled','Reversed')),
    version integer NOT NULL DEFAULT 1 CHECK (version > 0),
    received_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ux_erp_payment_tenant_id UNIQUE (tenant_id, erp_payment_id)
);
CREATE INDEX ix_erp_payments_invoice ON erp_payments(tenant_id, erp_invoice_id);

ALTER TABLE erp_shipments ADD COLUMN idempotency_key varchar(128);
CREATE UNIQUE INDEX ux_erp_shipment_idempotency ON erp_shipments(tenant_id,idempotency_key) WHERE idempotency_key IS NOT NULL;
ALTER TABLE erp_payments ADD COLUMN idempotency_key varchar(128);
CREATE UNIQUE INDEX ux_erp_payment_idempotency ON erp_payments(tenant_id,idempotency_key) WHERE idempotency_key IS NOT NULL;
