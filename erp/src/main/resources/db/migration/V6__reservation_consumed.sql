-- Shipping consumes the stock an order reserved: on_hand and reserved both drop by the reserved quantity.
-- "consumed" records that this happened exactly once per (order, SKU); cancellation must then not release it again.
ALTER TABLE stock_reservations ADD COLUMN consumed boolean NOT NULL DEFAULT false;
ALTER TABLE stock_reservations ADD CONSTRAINT ck_reservation_released_xor_consumed CHECK (NOT (released AND consumed));
