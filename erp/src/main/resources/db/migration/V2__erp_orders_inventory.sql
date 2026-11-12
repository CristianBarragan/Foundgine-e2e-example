-- Products, inventory, sales orders and stock reservations (Phase 7, ERP side). The ERP owns all of these ids.
CREATE SEQUENCE erp_order_seq START 5001;

-- Product catalogue. min_unit_price is the pricing floor: the ERP rejects an order line priced below it.
CREATE TABLE erp_products (
    tenant_id      varchar(64)   NOT NULL,
    sku            varchar(64)   NOT NULL,
    name           varchar(200)  NOT NULL,
    list_price     numeric(14,2) NOT NULL CHECK (list_price >= 0),
    min_unit_price numeric(14,2) NOT NULL CHECK (min_unit_price >= 0 AND min_unit_price <= list_price),
    status         varchar(16)   NOT NULL DEFAULT 'Active' CHECK (status IN ('Active', 'Discontinued')),
    PRIMARY KEY (tenant_id, sku)
);

-- One row per product; available = on_hand - reserved. The CHECKs make over-reservation impossible even under races.
CREATE TABLE erp_inventory (
    tenant_id varchar(64) NOT NULL,
    sku       varchar(64) NOT NULL,
    on_hand   integer     NOT NULL DEFAULT 0 CHECK (on_hand >= 0),
    reserved  integer     NOT NULL DEFAULT 0 CHECK (reserved >= 0),
    PRIMARY KEY (tenant_id, sku),
    FOREIGN KEY (tenant_id, sku) REFERENCES erp_products (tenant_id, sku),
    CONSTRAINT ck_inventory_reserved_le_on_hand CHECK (reserved <= on_hand)
);

-- The natural key (tenant, CRM quote) is unique: an accepted quote can produce at most one order.
CREATE TABLE sales_orders (
    erp_order_id    varchar(64)   PRIMARY KEY,
    tenant_id       varchar(64)   NOT NULL,
    crm_quote_id    integer       NOT NULL,
    erp_customer_id varchar(64)   NOT NULL REFERENCES erp_customers (erp_customer_id),
    total           numeric(14,2) NOT NULL,
    status          varchar(16)   NOT NULL CHECK (status IN ('Open', 'Cancelled')),
    cancel_reason   varchar(500)  NOT NULL DEFAULT '',
    correlation_id  varchar(64)   NOT NULL,
    created_at      timestamptz   NOT NULL DEFAULT now(),
    updated_at      timestamptz   NOT NULL DEFAULT now(),
    CONSTRAINT ux_sales_orders_tenant_quote UNIQUE (tenant_id, crm_quote_id)
);

CREATE TABLE sales_order_lines (
    erp_order_id varchar(64)   NOT NULL REFERENCES sales_orders (erp_order_id),
    line_no      integer       NOT NULL,
    tenant_id    varchar(64)   NOT NULL,
    sku          varchar(64)   NOT NULL,
    quantity     integer       NOT NULL CHECK (quantity > 0),
    unit_price   numeric(14,2) NOT NULL CHECK (unit_price >= 0),
    PRIMARY KEY (erp_order_id, line_no)
);

-- Reservation per order and SKU (lines of the same SKU are summed). Released = true once the order is cancelled.
CREATE TABLE stock_reservations (
    erp_order_id varchar(64) NOT NULL REFERENCES sales_orders (erp_order_id),
    tenant_id    varchar(64) NOT NULL,
    sku          varchar(64) NOT NULL,
    quantity     integer     NOT NULL CHECK (quantity > 0),
    released     boolean     NOT NULL DEFAULT false,
    PRIMARY KEY (erp_order_id, sku)
);

-- Inbox: one row per processed order message (correlation id), for traceability and duplicate detection.
CREATE TABLE order_inbox (
    tenant_id      varchar(64) NOT NULL,
    correlation_id varchar(64) NOT NULL,
    crm_quote_id   integer     NOT NULL,
    erp_order_id   varchar(64) NOT NULL REFERENCES sales_orders (erp_order_id),
    received_at    timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (tenant_id, correlation_id)
);
