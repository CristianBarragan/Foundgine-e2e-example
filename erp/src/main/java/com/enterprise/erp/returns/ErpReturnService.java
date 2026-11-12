package com.enterprise.erp.returns;

import com.enterprise.erp.customer.ValidationFailedException;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

/**
 * ERP side of customer returns. Authorize is idempotent on (tenant, CRM return id): a replay answers with the stored
 * decision. The order row is locked while the returnable quantity is computed, so concurrent returns cannot exceed what
 * was ordered. Completion puts the stock back exactly once (status change Authorized -> Completed gates the stock update).
 * Not done: credit note / financial adjustment.
 */
@Service
public class ErpReturnService {
    private final JdbcClient jdbc;

    public ErpReturnService(JdbcClient jdbc) { this.jdbc = jdbc; }

    @Transactional
    public ErpReturnDecision authorize(ReturnRequest in) {
        var errors = ReturnPlanner.validate(in);
        if (!errors.isEmpty()) throw new ValidationFailedException(errors);

        var existing = jdbc.sql("SELECT erp_return_id, status, note FROM erp_returns WHERE tenant_id = :t AND crm_return_id = :r")
                .param("t", in.tenantId()).param("r", in.returnId())
                .query((rs, i) -> new ErpReturnDecision(statusToDecision(rs.getString("status")), rs.getString("erp_return_id"), rs.getString("note"), false))
                .optional();
        if (existing.isPresent()) return existing.get();

        // Lock the order for the whole decision: serialises concurrent returns against the same order.
        var order = jdbc.sql("SELECT erp_customer_id, status FROM sales_orders WHERE tenant_id = :t AND erp_order_id = :o FOR UPDATE")
                .param("t", in.tenantId()).param("o", in.erpOrderId())
                .query((rs, i) -> new String[] {rs.getString("erp_customer_id"), rs.getString("status")}).optional();

        String rejection = null;
        if (order.isEmpty()) rejection = "Order " + in.erpOrderId() + " not found.";
        else if (!order.get()[0].equals(in.erpCustomerId())) rejection = "Order " + in.erpOrderId() + " does not belong to this customer.";
        else if (!"Open".equals(order.get()[1])) rejection = "Order " + in.erpOrderId() + " is " + order.get()[1] + ".";
        else rejection = ReturnPlanner.rejection(orderedQuantities(in), alreadyReturned(in), in.lines()).orElse(null);

        // A rejected return still needs the order FK; an unknown order cannot be stored, so answer without persisting.
        if (order.isEmpty()) return new ErpReturnDecision("Rejected", "", rejection, true);

        String id = "RET-" + jdbc.sql("SELECT nextval('erp_return_seq')").query(Long.class).single();
        String status = rejection == null ? "Authorized" : "Rejected";
        String note = rejection == null ? "" : rejection;
        int inserted = jdbc.sql("""
                INSERT INTO erp_returns (erp_return_id, tenant_id, crm_return_id, erp_order_id, status, reason, note, correlation_id)
                VALUES (:id, :t, :r, :o, :s, :reason, :note, :corr) ON CONFLICT (tenant_id, crm_return_id) DO NOTHING""")
                .param("id", id).param("t", in.tenantId()).param("r", in.returnId()).param("o", in.erpOrderId()).param("s", status)
                .param("reason", in.reason() == null ? "" : in.reason()).param("note", note).param("corr", in.correlationId()).update();
        if (inserted == 0) throw new IllegalStateException("Concurrent return insert despite the order lock; retry.");
        for (var q : ReturnPlanner.quantities(in.lines()).entrySet())
            jdbc.sql("INSERT INTO erp_return_lines (erp_return_id, sku, quantity) VALUES (:id, :sku, :q)")
                    .param("id", id).param("sku", q.getKey()).param("q", q.getValue()).update();
        return new ErpReturnDecision(statusToDecision(status), id, note, true);
    }

    /** Goods received: add the returned quantities back to on_hand, once. */
    @Transactional
    public CompleteReturnResult complete(CompleteReturnRequest in) {
        if (in == null || blank(in.tenantId()) || blank(in.erpReturnId()))
            throw new ValidationFailedException(List.of("tenantId and erpReturnId are required."));
        var status = jdbc.sql("SELECT status FROM erp_returns WHERE tenant_id = :t AND erp_return_id = :id FOR UPDATE")
                .param("t", in.tenantId()).param("id", in.erpReturnId()).query(String.class).optional();
        if (status.isEmpty()) throw new ValidationFailedException(List.of("Return " + in.erpReturnId() + " not found."));
        if ("Completed".equals(status.get())) return new CompleteReturnResult(in.erpReturnId(), false);
        if (!"Authorized".equals(status.get())) throw new ValidationFailedException(List.of("Return " + in.erpReturnId() + " is " + status.get() + "."));

        var lines = jdbc.sql("SELECT sku, quantity FROM erp_return_lines WHERE erp_return_id = :id ORDER BY sku")
                .param("id", in.erpReturnId()).query((rs, i) -> new Object[] {rs.getString("sku"), rs.getInt("quantity")}).list();
        for (var l : lines) {
            int n = jdbc.sql("UPDATE erp_inventory SET on_hand = on_hand + :q WHERE tenant_id = :t AND sku = :sku")
                    .param("q", l[1]).param("t", in.tenantId()).param("sku", l[0]).update();
            if (n != 1) throw new ValidationFailedException(List.of("No inventory record for " + l[0] + "."));
        }
        jdbc.sql("UPDATE erp_returns SET status = 'Completed', updated_at = now() WHERE erp_return_id = :id")
                .param("id", in.erpReturnId()).update();
        return new CompleteReturnResult(in.erpReturnId(), true);
    }

    private Map<String, Integer> orderedQuantities(ReturnRequest in) {
        var m = new HashMap<String, Integer>();
        jdbc.sql("SELECT sku, sum(quantity) AS q FROM sales_order_lines WHERE erp_order_id = :o AND tenant_id = :t GROUP BY sku")
                .param("o", in.erpOrderId()).param("t", in.tenantId())
                .query((rs, i) -> { m.put(rs.getString("sku"), rs.getInt("q")); return 0; }).list();
        return m;
    }

    private Map<String, Integer> alreadyReturned(ReturnRequest in) {
        var m = new HashMap<String, Integer>();
        jdbc.sql("""
                SELECT l.sku, sum(l.quantity) AS q FROM erp_return_lines l JOIN erp_returns r ON r.erp_return_id = l.erp_return_id
                WHERE r.tenant_id = :t AND r.erp_order_id = :o AND r.status IN ('Authorized', 'Completed') GROUP BY l.sku""")
                .param("o", in.erpOrderId()).param("t", in.tenantId())
                .query((rs, i) -> { m.put(rs.getString("sku"), rs.getInt("q")); return 0; }).list();
        return m;
    }

    private static String statusToDecision(String status) { return "Rejected".equals(status) ? "Rejected" : "Authorized"; }

    private static boolean blank(String s) { return s == null || s.isBlank(); }
}
