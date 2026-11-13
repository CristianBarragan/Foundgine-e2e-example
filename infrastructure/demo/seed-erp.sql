-- Demo stock for tenant "acme" (the tenant of the demo users alice and dave). Idempotent: safe to re-run.
INSERT INTO erp_products (tenant_id, sku, name, list_price, min_unit_price) VALUES
  ('acme', 'SKU-1', 'Widget',  100.00, 80.00),
  ('acme', 'SKU-2', 'Gadget',   50.00, 40.00)
ON CONFLICT (tenant_id, sku) DO NOTHING;

INSERT INTO erp_inventory (tenant_id, sku, on_hand, reserved) VALUES
  ('acme', 'SKU-1', 100, 0),
  ('acme', 'SKU-2',  25, 0)
ON CONFLICT (tenant_id, sku) DO NOTHING;
