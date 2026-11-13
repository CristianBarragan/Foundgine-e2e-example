package com.enterprise.erp.returns;

import com.enterprise.erp.customer.ValidationFailedException;
import com.enterprise.erp.inventory.ErpInventoryRepository;
import com.enterprise.erp.order.SalesOrderLineRepository;
import com.enterprise.erp.order.SalesOrderRepository;
import com.enterprise.erp.order.SkuQuantity;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
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
    private final ErpReturnRepository returns;
    private final ErpReturnLineRepository returnLines;
    private final SalesOrderRepository orders;
    private final SalesOrderLineRepository orderLines;
    private final ErpInventoryRepository inventory;

    public ErpReturnService(ErpReturnRepository returns, ErpReturnLineRepository returnLines, SalesOrderRepository orders,
                            SalesOrderLineRepository orderLines, ErpInventoryRepository inventory) {
        this.returns = returns; this.returnLines = returnLines; this.orders = orders;
        this.orderLines = orderLines; this.inventory = inventory;
    }

    @Transactional
    public ErpReturnDecision authorize(ReturnRequest in) {
        var errors = ReturnPlanner.validate(in);
        if (!errors.isEmpty()) throw new ValidationFailedException(errors);

        var existing = returns.findByTenantIdAndCrmReturnId(in.tenantId(), in.returnId());
        if (existing.isPresent()) {
            var stored = existing.get();
            return new ErpReturnDecision(statusToDecision(stored.getStatus()), stored.getErpReturnId(), stored.getNote(), false);
        }

        // Lock the order for the whole decision: serialises concurrent returns against the same order.
        var order = orders.lockByTenantAndId(in.tenantId(), in.erpOrderId());

        String rejection;
        if (order.isEmpty()) rejection = "Order " + in.erpOrderId() + " not found.";
        else if (!order.get().getErpCustomerId().equals(in.erpCustomerId())) rejection = "Order " + in.erpOrderId() + " does not belong to this customer.";
        else if (!"Open".equals(order.get().getStatus())) rejection = "Order " + in.erpOrderId() + " is " + order.get().getStatus() + ".";
        else rejection = ReturnPlanner.rejection(
                toMap(orderLines.orderedQuantities(in.tenantId(), in.erpOrderId())),
                toMap(returns.returnedQuantities(in.tenantId(), in.erpOrderId())),
                in.lines()).orElse(null);

        // A rejected return still needs the order FK; an unknown order cannot be stored, so answer without persisting.
        if (order.isEmpty()) return new ErpReturnDecision("Rejected", "", rejection, true);

        String id = "RET-" + returns.nextReturnNumber();
        String status = rejection == null ? "Authorized" : "Rejected";
        String note = rejection == null ? "" : rejection;
        int inserted = returns.insertIfAbsent(id, in.tenantId(), in.returnId(), in.erpOrderId(), status,
                in.reason() == null ? "" : in.reason(), note, in.correlationId());
        if (inserted == 0) throw new IllegalStateException("Concurrent return insert despite the order lock; retry.");
        for (var q : ReturnPlanner.quantities(in.lines()).entrySet())
            returnLines.save(new ErpReturnLine(id, q.getKey(), q.getValue()));
        return new ErpReturnDecision(statusToDecision(status), id, note, true);
    }

    /** Goods received: add the returned quantities back to on_hand, once. */
    @Transactional
    public CompleteReturnResult complete(CompleteReturnRequest in) {
        if (in == null || blank(in.tenantId()) || blank(in.erpReturnId()))
            throw new ValidationFailedException(List.of("tenantId and erpReturnId are required."));
        var ret = returns.lockByTenantAndId(in.tenantId(), in.erpReturnId())
                .orElseThrow(() -> new ValidationFailedException(List.of("Return " + in.erpReturnId() + " not found.")));
        if ("Completed".equals(ret.getStatus())) return new CompleteReturnResult(in.erpReturnId(), false);
        if (!"Authorized".equals(ret.getStatus())) throw new ValidationFailedException(List.of("Return " + in.erpReturnId() + " is " + ret.getStatus() + "."));

        for (var line : returnLines.findByErpReturnIdOrderBySku(in.erpReturnId())) {
            if (inventory.restock(in.tenantId(), line.getSku(), line.getQuantity()) != 1)
                throw new ValidationFailedException(List.of("No inventory record for " + line.getSku() + "."));
        }
        ret.complete();
        return new CompleteReturnResult(in.erpReturnId(), true);
    }

    private static Map<String, Integer> toMap(List<SkuQuantity> rows) {
        var m = new HashMap<String, Integer>();
        for (var r : rows) m.put(r.sku(), Math.toIntExact(r.quantity()));
        return m;
    }

    private static String statusToDecision(String status) { return "Rejected".equals(status) ? "Rejected" : "Authorized"; }

    private static boolean blank(String s) { return s == null || s.isBlank(); }
}
