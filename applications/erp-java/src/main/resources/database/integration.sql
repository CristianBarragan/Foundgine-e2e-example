-- ERP-owned tables backing the /api/integration/* contract used by the Temporal saga.
-- Statements are split on semicolons by PostgresSchema, so keep comments free of them.

CREATE TABLE IF NOT EXISTS customer_external_refs (
  tenant_id VARCHAR(80) NOT NULL,
  source_system VARCHAR(40) NOT NULL,
  external_id VARCHAR(100) NOT NULL,
  customer_id INT NOT NULL REFERENCES customers(customer_id),
  PRIMARY KEY (tenant_id, source_system, external_id)
);

CREATE TABLE IF NOT EXISTS integration_orders (
  tenant_id VARCHAR(80) NOT NULL,
  source_system VARCHAR(40) NOT NULL,
  source_order_id VARCHAR(100) NOT NULL,
  order_id INT NOT NULL UNIQUE REFERENCES orders(order_id),
  PRIMARY KEY (tenant_id, source_system, source_order_id)
);

CREATE TABLE IF NOT EXISTS inventory_reservations (
  reservation_id UUID PRIMARY KEY,
  tenant_id VARCHAR(80) NOT NULL,
  order_id INT NOT NULL REFERENCES orders(order_id),
  status VARCHAR(20) NOT NULL CHECK (status IN ('Reserved','Released','Rejected')),
  rejection_reason VARCHAR(500),
  created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_inventory_reservations_active_order
  ON inventory_reservations(order_id) WHERE status = 'Reserved';

CREATE TABLE IF NOT EXISTS inventory_reservation_lines (
  reservation_id UUID NOT NULL REFERENCES inventory_reservations(reservation_id) ON DELETE CASCADE,
  lot_id INT NOT NULL REFERENCES inventory_lots(lot_id),
  product_id INT NOT NULL REFERENCES products(product_id),
  quantity INT NOT NULL CHECK (quantity > 0),
  PRIMARY KEY (reservation_id, lot_id)
);

CREATE TABLE IF NOT EXISTS integration_commands (
  tenant_id VARCHAR(80) NOT NULL,
  idempotency_key VARCHAR(200) NOT NULL,
  command VARCHAR(40) NOT NULL,
  request_fingerprint CHAR(64) NOT NULL,
  response TEXT NOT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (tenant_id, idempotency_key)
);
