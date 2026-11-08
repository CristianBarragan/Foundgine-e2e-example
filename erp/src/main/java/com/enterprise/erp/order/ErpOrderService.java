package com.enterprise.erp.order;

import com.enterprise.erp.customer.ValidationFailedException;
import java.math.BigDecimal;
import java.util.HashMap;
import java.util.List;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

/**
 * ERP sales order + stock reservation as ONE synchronous, atomic transaction (Temporal only orchestrates around it).
 * Idempotent: (tenant, CRM quote) is unique, so a redelivered or retried request returns the existing order and
 * reserves nothing more. Any failure (validation, pricing, stock) rolls back the whole transaction: no order row, no
 * reservation. Stock is reserved with a conditional UPDATE, so concurrent orders can never over-reserve.
 */
@Service
public class ErpOrderService {
    private final JdbcClient jdbc;

    public ErpOrderService(JdbcClient jdbc) { this.jdbc = jdbc; }

    @Transactional
    public ErpOrderResult createOrderWithReservation(OrderRequest in) {
        var errors = OrderPlanner.validate(in);
        if (!errors.isEmpty()) throw new ValidationFailedException(errors);

        var existing = findOrder(in.tenantId(), in.quoteId());
        if (existing != null) return replay(in, existing);

        requireActiveCustomer(in);
        requirePricing(in);

        String orderId = "SO-" + jdbc.sql("SELECT nextval('erp_order_seq')").query(Long.class).single();
        int inserted = jdbc.sql("""
                INSERT INTO sales_orders (erp_order_id, tenant_id, crm_quote_id, erp_customer_id, total, status, correlation_id)
                VALUES (:id, :tenant, :quote, :customer, :total, 'Open', :corr)
                ON CONFLICT (tenant_id, crm_quote_id) DO NOTHING""")
                .param("id", orderId).param("tenant", in.tenantId()).param("quote", in.quoteId())
                .param("customer", in.erpCustomerId()).param("total", in.total()).param("corr", in.correlationId())
                .update();
        if (inserted == 0) return replay(in, findOrder(in.tenantId(), in.quoteId())); // lost a race with a concurrent identical request

        int lineNo = 0;
        for (var l : in.lines()) {
            jdbc.sql("""
                    INSERT INTO sales_order_lines (erp_order_id, line_no, tenant_id, sku, quantity, unit_price)
                    VALUES (:order, :no, :tenant, :sku, :qty, :price)""")
                    .param("order", orderId).param("no", ++lineNo).param("tenant", in.tenantId())
                    .param("sku", l.sku()).param("qty", l.quantity()).param("price", l.unitPrice()).update();
        }

        for (var r : OrderPlanner.reservations(in.lines()).entrySet()) {   // sorted by SKU: consistent lock order
            int reserved = jdbc.sql("""
                    UPDATE erp_inventory SET reserved = reserved + :qty
                    WHERE tenant_id = :tenant AND sku = :sku AND on_hand - reserved >= :qty""")
                    .param("qty", r.getValue()).param("tenant", in.tenantId()).param("sku", r.getKey()).update();
            if (reserved != 1) throw new InsufficientStockException(r.getKey(), r.getValue());
            jdbc.sql("INSERT INTO stock_reservations (erp_order_id, tenant_id, sku, quantity) VALUES (:order, :tenant, :sku, :qty)")
                    .param("order", orderId).param("tenant", in.tenantId()).param("sku", r.getKey()).param("qty", r.getValue()).update();
        }

        recordInbox(in, orderId);
        return new ErpOrderResult(orderId, true);
    }

    /** Compensation: cancel an Open order and release its reservation. Idempotent. */
    @Transactional
    public CancelOrderResult cancelOrder(CancelOrderRequest in) {
        if (in == null || blank(in.tenantId()) || blank(in.erpOrderId()) || in.quoteId() <= 0)
            throw new ValidationFailedException(List.of("tenantId, quoteId and erpOrderId are required."));

        var status = jdbc.sql("""
                SELECT status FROM sales_orders WHERE erp_order_id = :id AND tenant_id = :tenant AND crm_quote_id = :quote FOR UPDATE""")
                .param("id", in.erpOrderId()).param("tenant", in.tenantId()).param("quote", in.quoteId())
                .query(String.class).optional();
        if (status.isEmpty()) throw new ValidationFailedException(List.of("Order " + in.erpOrderId() + " not found for this quote."));
        if ("Cancelled".equals(status.get())) return new CancelOrderResult(in.erpOrderId(), false);

        var held = jdbc.sql("""
                SELECT sku, quantity FROM stock_reservations WHERE erp_order_id = :id AND released = false ORDER BY sku""")
                .param("id", in.erpOrderId())
                .query((rs, i) -> new Object[] {rs.getString("sku"), rs.getInt("quantity")}).list();
        for (var h : held) {
            jdbc.sql("UPDATE erp_inventory SET reserved = reserved - :qty WHERE tenant_id = :tenant AND sku = :sku")
                    .param("qty", h[1]).param("tenant", in.tenantId()).param("sku", h[0]).update();
            jdbc.sql("UPDATE stock_reservations SET released = true WHERE erp_order_id = :id AND sku = :sku")
                    .param("id", in.erpOrderId()).param("sku", h[0]).update();
        }
        String reason = in.reason() == null ? "" : in.reason().length() > 500 ? in.reason().substring(0, 500) : in.reason();
        jdbc.sql("UPDATE sales_orders SET status = 'Cancelled', cancel_reason = :reason, updated_at = now() WHERE erp_order_id = :id")
                .param("reason", reason).param("id", in.erpOrderId()).update();
        return new CancelOrderResult(in.erpOrderId(), true);
    }

    private record Existing(String erpOrderId, String status) {}

    private Existing findOrder(String tenant, int quote) {
        return jdbc.sql("SELECT erp_order_id, status FROM sales_orders WHERE tenant_id = :tenant AND crm_quote_id = :quote")
                .param("tenant", tenant).param("quote", quote)
                .query((rs, i) -> new Existing(rs.getString("erp_order_id"), rs.getString("status"))).optional().orElse(null);
    }

    private ErpOrderResult replay(OrderRequest in, Existing existing) {
        if (!"Open".equals(existing.status()))   // never hand the CRM a cancelled order as if it were live
            throw new ValidationFailedException(List.of("Order " + existing.erpOrderId() + " for this quote is " + existing.status() + "."));
        recordInbox(in, existing.erpOrderId());
        return new ErpOrderResult(existing.erpOrderId(), false);
    }

    private void recordInbox(OrderRequest in, String orderId) {
        jdbc.sql("""
                INSERT INTO order_inbox (tenant_id, correlation_id, crm_quote_id, erp_order_id)
                VALUES (:tenant, :corr, :quote, :order) ON CONFLICT (tenant_id, correlation_id) DO NOTHING""")
                .param("tenant", in.tenantId()).param("corr", in.correlationId()).param("quote", in.quoteId())
                .param("order", orderId).update();
    }

    private void requireActiveCustomer(OrderRequest in) {
        int n = jdbc.sql("""
                SELECT count(*) FROM erp_customers
                WHERE erp_customer_id = :erp AND tenant_id = :tenant AND crm_customer_id = :crm AND status = 'Active'""")
                .param("erp", in.erpCustomerId()).param("tenant", in.tenantId()).param("crm", in.crmCustomerId())
                .query(Integer.class).single();
        if (n != 1) throw new ValidationFailedException(List.of(
                "ERP customer " + in.erpCustomerId() + " is not an active customer of this tenant matching CRM customer " + in.crmCustomerId() + "."));
    }

    private void requirePricing(OrderRequest in) {
        var skus = in.lines().stream().map(OrderRequest.Line::sku).distinct().toList();
        var floors = new HashMap<String, BigDecimal>();
        jdbc.sql("SELECT sku, min_unit_price FROM erp_products WHERE tenant_id = :tenant AND status = 'Active' AND sku IN (:skus)")
                .param("tenant", in.tenantId()).param("skus", skus)
                .query((rs, i) -> { floors.put(rs.getString("sku"), rs.getBigDecimal("min_unit_price")); return 0; }).list();
        var errors = OrderPlanner.checkPricing(in.lines(), floors);
        if (!errors.isEmpty()) throw new ValidationFailedException(errors);
    }

    private static boolean blank(String s) { return s == null || s.isBlank(); }
}
