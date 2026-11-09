package com.foundgine.samples.supplychain.advanced.integration;

import static com.foundgine.samples.supplychain.advanced.integration.IntegrationModels.*;

import com.fasterxml.jackson.databind.ObjectMapper;

import java.math.BigDecimal;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.sql.Connection;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.sql.SQLException;
import java.util.ArrayList;
import java.util.HexFormat;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Objects;
import java.util.TreeMap;
import java.util.UUID;

/**
 * ERP-owned implementation of the saga's integration commands. Each command runs in one PostgreSQL
 * transaction, is idempotent by (tenant, idempotencyKey) with a request fingerprint, and is confined to
 * the tenant named in the command: customers, warehouses/lots, orders and reservations are all
 * filtered by tenant, so a command can never touch another tenant's data even though the calling
 * service identity is platform-wide.
 */
public final class IntegrationStore {
    public static final String SOURCE_SYSTEM = "crm";

    private final Connection connection;
    private final ObjectMapper mapper;

    public IntegrationStore(Connection connection, ObjectMapper mapper) {
        this.connection = Objects.requireNonNull(connection);
        this.mapper = Objects.requireNonNull(mapper);
    }

    // ---------------------------------------------------------------- commands

    public ErpOrderResult createOrder(CreateOrder c) {
        require(c != null, "Body is required.");
        requireText(c.tenantId(), "tenantId");
        requireText(c.crmCommercialOrderId(), "crmCommercialOrderId");
        requireText(c.customerId(), "customerId");
        var lines = validLines(c.lines());
        return idempotent(c.tenantId(), c.idempotencyKey(), "create-order", fingerprint(c), ErpOrderResult.class, () -> {
            // Natural idempotency on the CRM order id, independent of the key.
            Integer existing = scalarInt(
                    "SELECT order_id FROM integration_orders WHERE tenant_id=? AND source_system=? AND source_order_id=?",
                    c.tenantId(), SOURCE_SYSTEM, c.crmCommercialOrderId());
            if (existing != null) return new ErpOrderResult(String.valueOf(existing), orderStatus(existing));

            Integer customer = scalarInt(
                    "SELECT r.customer_id FROM customer_external_refs r JOIN customers cu ON cu.customer_id=r.customer_id"
                            + " WHERE r.tenant_id=? AND cu.tenant_id=r.tenant_id AND r.source_system=? AND r.external_id=?",
                    c.tenantId(), SOURCE_SYSTEM, c.customerId());
            if (customer == null)
                throw new IntegrationException(422, "customer_unmapped", "CRM customer has no ERP customer mapping in this tenant.");

            var priced = new LinkedHashMap<Integer, int[]>(); // productId -> {quantity}
            var prices = new LinkedHashMap<Integer, BigDecimal>();
            for (var e : new TreeMap<>(wholeQuantitiesBySku(lines)).entrySet()) {
                var row = queryOne("SELECT product_id, unit_price FROM products WHERE sku=?", e.getKey());
                if (row == null)
                    throw new IntegrationException(422, "unknown_product", "Unknown product SKU: " + e.getKey());
                int productId = ((Number) row[0]).intValue();
                priced.merge(productId, new int[] {e.getValue()}, (a, b) -> new int[] {a[0] + b[0]});
                prices.put(productId, (BigDecimal) row[1]);
            }
            var total = BigDecimal.ZERO;
            for (var e : priced.entrySet())
                total = total.add(prices.get(e.getKey()).multiply(BigDecimal.valueOf(e.getValue()[0])));

            int orderId;
            try (var ps = connection.prepareStatement(
                    "INSERT INTO orders(customer_id,status,total_amount,placed_on) VALUES (?,'Pending',?,CURRENT_DATE) RETURNING order_id")) {
                ps.setInt(1, customer);
                ps.setBigDecimal(2, total);
                try (var rs = ps.executeQuery()) {
                    rs.next();
                    orderId = rs.getInt(1);
                }
            }
            for (var e : priced.entrySet())
                update("INSERT INTO order_items(order_id,product_id,quantity,unit_price) VALUES (?,?,?,?)",
                        orderId, e.getKey(), e.getValue()[0], prices.get(e.getKey()));
            update("INSERT INTO integration_orders(tenant_id,source_system,source_order_id,order_id) VALUES (?,?,?,?)",
                    c.tenantId(), SOURCE_SYSTEM, c.crmCommercialOrderId(), orderId);
            return new ErpOrderResult(String.valueOf(orderId), "Pending");
        });
    }

    public ReservationResult reserveInventory(Reserve c) {
        require(c != null, "Body is required.");
        requireText(c.tenantId(), "tenantId");
        requireText(c.crmCommercialOrderId(), "crmCommercialOrderId");
        requireText(c.erpOrderId(), "erpOrderId");
        var lines = validLines(c.lines());
        return idempotent(c.tenantId(), c.idempotencyKey(), "reserve", fingerprint(c), ReservationResult.class, () -> {
            int orderId = resolveOrder(c.tenantId(), c.crmCommercialOrderId(), c.erpOrderId());
            String status = scalarString("SELECT status FROM orders WHERE order_id=? FOR UPDATE", orderId);
            if (!"Pending".equals(status))
                throw new IntegrationException(409, "order_not_pending", "ERP order is " + status + ", not Pending.");

            String active = scalarString(
                    "SELECT reservation_id::text FROM inventory_reservations WHERE order_id=? AND status='Reserved'", orderId);
            if (active != null) return new ReservationResult(true, active, null);

            // Plan every product before changing anything: the whole reservation is all-or-nothing.
            var plan = new LinkedHashMap<Integer, List<ReservationPlanner.Allocation>>();
            for (var e : new TreeMap<>(wholeQuantitiesBySku(lines)).entrySet()) {
                Integer productId = scalarInt("SELECT product_id FROM products WHERE sku=?", e.getKey());
                if (productId == null)
                    throw new IntegrationException(422, "unknown_product", "Unknown product SKU: " + e.getKey());
                var lots = new ArrayList<ReservationPlanner.Lot>();
                try (var ps = connection.prepareStatement(
                        "SELECT l.lot_id, (l.on_hand-l.reserved-l.quarantined) AS available, l.received_on"
                                + " FROM inventory_lots l JOIN warehouses w ON w.warehouse_id=l.warehouse_id"
                                + " WHERE l.product_id=? AND w.tenant_id=? ORDER BY l.lot_id FOR UPDATE OF l")) {
                    ps.setInt(1, productId);
                    ps.setString(2, c.tenantId());
                    try (var rs = ps.executeQuery()) {
                        while (rs.next())
                            lots.add(new ReservationPlanner.Lot(rs.getInt(1), rs.getBigDecimal(2), rs.getDate(3).toLocalDate()));
                    }
                }
                var outcome = ReservationPlanner.plan(e.getValue(), lots);
                if (!outcome.satisfied()) {
                    String reason = "Insufficient inventory for SKU " + e.getKey() + " (short by " + outcome.shortfall() + ").";
                    UUID rejectedId = UUID.randomUUID();
                    update("INSERT INTO inventory_reservations(reservation_id,tenant_id,order_id,status,rejection_reason)"
                            + " VALUES (?::uuid,?,?,'Rejected',?)", rejectedId.toString(), c.tenantId(), orderId, reason);
                    return new ReservationResult(false, null, reason);
                }
                plan.put(productId, outcome.allocations());
            }

            UUID reservationId = UUID.randomUUID();
            update("INSERT INTO inventory_reservations(reservation_id,tenant_id,order_id,status) VALUES (?::uuid,?,?,'Reserved')",
                    reservationId.toString(), c.tenantId(), orderId);
            for (var e : plan.entrySet())
                for (var a : e.getValue()) {
                    update("UPDATE inventory_lots SET reserved=reserved+? WHERE lot_id=?", a.quantity(), a.lotId());
                    update("INSERT INTO inventory_reservation_lines(reservation_id,lot_id,product_id,quantity) VALUES (?::uuid,?,?,?)",
                            reservationId.toString(), a.lotId(), e.getKey(), a.quantity());
                }
            return new ReservationResult(true, reservationId.toString(), null);
        });
    }

    public Ack releaseReservation(String reservationId, Compensation c) {
        require(c != null, "Body is required.");
        requireText(c.tenantId(), "tenantId");
        final UUID id;
        try { id = UUID.fromString(reservationId); }
        catch (IllegalArgumentException e) { throw new IntegrationException(400, "invalid_reservation_id", "Reservation id must be a UUID."); }
        return idempotent(c.tenantId(), c.idempotencyKey(), "release", fingerprint(c), Ack.class, () -> {
            var row = queryOne("SELECT tenant_id, status FROM inventory_reservations WHERE reservation_id=?::uuid FOR UPDATE", id.toString());
            if (row == null || !c.tenantId().equals(row[0]))
                throw new IntegrationException(404, "reservation_not_found", "Reservation not found in this tenant.");
            String status = (String) row[1];
            if (!"Reserved".equals(status)) return new Ack(status); // Released/Rejected: nothing is held
            try (var ps = connection.prepareStatement(
                    "SELECT lot_id, quantity FROM inventory_reservation_lines WHERE reservation_id=?::uuid ORDER BY lot_id")) {
                ps.setString(1, id.toString());
                var held = new ArrayList<int[]>();
                try (var rs = ps.executeQuery()) { while (rs.next()) held.add(new int[] {rs.getInt(1), rs.getInt(2)}); }
                for (var h : held) update("UPDATE inventory_lots SET reserved=reserved-? WHERE lot_id=?", h[1], h[0]);
            }
            update("UPDATE inventory_reservations SET status='Released', updated_at=CURRENT_TIMESTAMP WHERE reservation_id=?::uuid", id.toString());
            return new Ack("Released");
        });
    }

    public Ack cancelOrder(String erpOrderId, Compensation c) {
        require(c != null, "Body is required.");
        requireText(c.tenantId(), "tenantId");
        final int orderId;
        try { orderId = Integer.parseInt(erpOrderId); }
        catch (NumberFormatException e) { throw new IntegrationException(400, "invalid_order_id", "ERP order id must be numeric."); }
        return idempotent(c.tenantId(), c.idempotencyKey(), "cancel-order", fingerprint(c), Ack.class, () -> {
            Integer mapped = scalarInt("SELECT order_id FROM integration_orders WHERE tenant_id=? AND source_system=? AND order_id=?",
                    c.tenantId(), SOURCE_SYSTEM, orderId);
            if (mapped == null) throw new IntegrationException(404, "order_not_found", "ERP order not found in this tenant.");
            String status = scalarString("SELECT status FROM orders WHERE order_id=? FOR UPDATE", orderId);
            if ("Cancelled".equals(status)) return new Ack("Cancelled");
            if (!"Pending".equals(status))
                throw new IntegrationException(409, "order_not_cancellable", "ERP order is " + status + " and cannot be cancelled.");
            if (scalarString("SELECT reservation_id::text FROM inventory_reservations WHERE order_id=? AND status='Reserved'", orderId) != null)
                throw new IntegrationException(409, "reservation_active", "Release the inventory reservation before cancelling the order.");
            update("UPDATE orders SET status='Cancelled' WHERE order_id=?", orderId);
            return new Ack("Cancelled");
        });
    }

    // ---------------------------------------------------------------- idempotency envelope

    @FunctionalInterface
    private interface Body<T> { T run() throws SQLException; }

    private <T> T idempotent(String tenant, String key, String command, String fingerprint, Class<T> type, Body<T> body) {
        if (key == null || key.isBlank() || key.length() > 200)
            throw new IntegrationException(400, "idempotency_key_required", "idempotencyKey is required (max 200 characters).");
        boolean oldAuto = true;
        try {
            oldAuto = connection.getAutoCommit();
            connection.setAutoCommit(false);
            try (var lock = connection.prepareStatement("SELECT pg_advisory_xact_lock(hashtext(?))")) {
                lock.setString(1, tenant + ":" + key);
                lock.execute();
            }
            var prior = queryOne("SELECT command, request_fingerprint, response FROM integration_commands WHERE tenant_id=? AND idempotency_key=?", tenant, key);
            if (prior != null) {
                if (!command.equals(prior[0]) || !fingerprint.equals(((String) prior[1]).trim()))
                    throw new IntegrationException(409, "idempotency_conflict", "idempotencyKey was already used for a different request.");
                connection.rollback();
                return mapper.readValue((String) prior[2], type);
            }
            T result = body.run();
            update("INSERT INTO integration_commands(tenant_id,idempotency_key,command,request_fingerprint,response) VALUES (?,?,?,?,?)",
                    tenant, key, command, fingerprint, mapper.writeValueAsString(result));
            connection.commit();
            return result;
        } catch (IntegrationException e) {
            rollbackQuietly();
            throw e;
        } catch (Exception e) {
            rollbackQuietly();
            throw new IllegalStateException("Integration command failed: " + command, e);
        } finally {
            try { connection.setAutoCommit(oldAuto); } catch (SQLException ignored) { }
        }
    }

    // ---------------------------------------------------------------- helpers

    private int resolveOrder(String tenant, String crmOrderId, String erpOrderId) throws SQLException {
        Integer mapped = scalarInt("SELECT order_id FROM integration_orders WHERE tenant_id=? AND source_system=? AND source_order_id=?",
                tenant, SOURCE_SYSTEM, crmOrderId);
        if (mapped == null || !String.valueOf(mapped).equals(erpOrderId))
            throw new IntegrationException(404, "order_not_found", "ERP order not found for this CRM order in this tenant.");
        return mapped;
    }

    private String orderStatus(int orderId) throws SQLException {
        return scalarString("SELECT status FROM orders WHERE order_id=?", orderId);
    }

    private static List<Line> validLines(List<Line> lines) {
        require(lines != null && !lines.isEmpty(), "At least one line is required.");
        require(lines.size() <= 100, "At most 100 lines are allowed.");
        for (var l : lines) {
            require(l != null && l.productId() != null && !l.productId().isBlank(), "Line productId (SKU) is required.");
            require(l.quantity() != null && l.quantity().signum() > 0, "Line quantity must be positive.");
        }
        return lines;
    }

    /** SKU -> whole-unit quantity. ERP quantities are integers, so fractional quantities are a contract error. */
    private static Map<String, Integer> wholeQuantitiesBySku(List<Line> lines) {
        var out = new LinkedHashMap<String, Integer>();
        for (var l : lines) {
            BigDecimal q = l.quantity();
            if (q.stripTrailingZeros().scale() > 0)
                throw new IntegrationException(422, "fractional_quantity", "ERP supports whole-unit quantities only (SKU " + l.productId().trim() + ").");
            if (q.compareTo(BigDecimal.valueOf(Integer.MAX_VALUE)) > 0)
                throw new IntegrationException(422, "quantity_too_large", "Quantity is too large.");
            out.merge(l.productId().trim(), q.intValueExact(), Math::addExact);
        }
        return out;
    }

    private String fingerprint(Object command) {
        try {
            var bytes = MessageDigest.getInstance("SHA-256").digest(mapper.writeValueAsBytes(command));
            return HexFormat.of().formatHex(bytes);
        } catch (Exception e) {
            throw new IllegalStateException("Unable to fingerprint command", e);
        }
    }

    private static void require(boolean ok, String message) {
        if (!ok) throw new IntegrationException(400, "invalid_request", message);
    }

    private static void requireText(String v, String name) {
        require(v != null && !v.isBlank(), name + " is required.");
    }

    private void rollbackQuietly() {
        try { connection.rollback(); } catch (SQLException ignored) { }
    }

    private Object[] queryOne(String sql, Object... args) throws SQLException {
        try (PreparedStatement ps = prepare(sql, args); ResultSet rs = ps.executeQuery()) {
            if (!rs.next()) return null;
            int n = rs.getMetaData().getColumnCount();
            var row = new Object[n];
            for (int i = 0; i < n; i++) row[i] = rs.getObject(i + 1);
            return row;
        }
    }

    private Integer scalarInt(String sql, Object... args) throws SQLException {
        var row = queryOne(sql, args);
        return row == null || row[0] == null ? null : ((Number) row[0]).intValue();
    }

    private String scalarString(String sql, Object... args) throws SQLException {
        var row = queryOne(sql, args);
        return row == null || row[0] == null ? null : row[0].toString();
    }

    private void update(String sql, Object... args) throws SQLException {
        try (PreparedStatement ps = prepare(sql, args)) { ps.executeUpdate(); }
    }

    private PreparedStatement prepare(String sql, Object... args) throws SQLException {
        var ps = connection.prepareStatement(sql);
        for (int i = 0; i < args.length; i++) ps.setObject(i + 1, args[i]);
        return ps;
    }
}
