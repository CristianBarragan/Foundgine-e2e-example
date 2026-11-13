package com.enterprise.erp.order;

import com.enterprise.erp.customer.ErpCustomerRepository;
import com.enterprise.erp.customer.ValidationFailedException;
import com.enterprise.erp.inventory.ErpInventoryRepository;
import com.enterprise.erp.inventory.ErpProduct;
import com.enterprise.erp.inventory.ErpProductRepository;
import java.math.BigDecimal;
import java.util.HashMap;
import java.util.List;
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
    private final SalesOrderRepository orders;
    private final SalesOrderLineRepository lines;
    private final StockReservationRepository reservations;
    private final ErpInventoryRepository inventory;
    private final ErpProductRepository products;
    private final ErpCustomerRepository customers;

    public ErpOrderService(SalesOrderRepository orders, SalesOrderLineRepository lines, StockReservationRepository reservations,
                           ErpInventoryRepository inventory, ErpProductRepository products, ErpCustomerRepository customers) {
        this.orders = orders; this.lines = lines; this.reservations = reservations;
        this.inventory = inventory; this.products = products; this.customers = customers;
    }

    @Transactional
    public ErpOrderResult createOrderWithReservation(OrderRequest in) {
        var errors = OrderPlanner.validate(in);
        if (!errors.isEmpty()) throw new ValidationFailedException(errors);

        var existing = findOrder(in.tenantId(), in.quoteId());
        if (existing != null) return replay(in, existing);

        requireActiveCustomer(in);
        requirePricing(in);

        String orderId = "SO-" + orders.nextOrderNumber();
        int inserted = orders.insertIfAbsent(orderId, in.tenantId(), in.quoteId(), in.erpCustomerId(), in.total(), in.correlationId());
        if (inserted == 0) return replay(in, findOrder(in.tenantId(), in.quoteId())); // lost a race with a concurrent identical request

        int lineNo = 0;
        for (var l : in.lines())
            lines.save(new SalesOrderLine(orderId, ++lineNo, in.tenantId(), l.sku(), l.quantity(), l.unitPrice()));

        for (var r : OrderPlanner.reservations(in.lines()).entrySet()) {   // sorted by SKU: consistent lock order
            int reserved = inventory.reserve(in.tenantId(), r.getKey(), r.getValue());
            if (reserved != 1) throw new InsufficientStockException(r.getKey(), r.getValue());
            reservations.save(new StockReservation(orderId, in.tenantId(), r.getKey(), r.getValue()));
        }

        orders.recordInbox(in.tenantId(), in.correlationId(), in.quoteId(), orderId);
        return new ErpOrderResult(orderId, true);
    }

    /** Compensation: cancel an Open order and release its reservation (stock already consumed by shipping stays consumed). Idempotent. */
    @Transactional
    public CancelOrderResult cancelOrder(CancelOrderRequest in) {
        if (in == null || blank(in.tenantId()) || blank(in.erpOrderId()) || in.quoteId() <= 0)
            throw new ValidationFailedException(List.of("tenantId, quoteId and erpOrderId are required."));

        var order = orders.lockForCancel(in.erpOrderId(), in.tenantId(), in.quoteId())
                .orElseThrow(() -> new ValidationFailedException(List.of("Order " + in.erpOrderId() + " not found for this quote.")));
        if ("Cancelled".equals(order.getStatus())) return new CancelOrderResult(in.erpOrderId(), false);

        for (var held : reservations.findByErpOrderIdAndReleasedFalseAndConsumedFalseOrderBySku(in.erpOrderId())) {
            inventory.release(in.tenantId(), held.getSku(), held.getQuantity());
            held.release();
        }
        String reason = in.reason() == null ? "" : in.reason().length() > 500 ? in.reason().substring(0, 500) : in.reason();
        order.cancel(reason);
        return new CancelOrderResult(in.erpOrderId(), true);
    }

    private record Existing(String erpOrderId, String status) {}

    private Existing findOrder(String tenant, int quote) {
        return orders.findByTenantIdAndCrmQuoteId(tenant, quote).map(o -> new Existing(o.getErpOrderId(), o.getStatus())).orElse(null);
    }

    private ErpOrderResult replay(OrderRequest in, Existing existing) {
        if (!"Open".equals(existing.status()))   // never hand the CRM a cancelled order as if it were live
            throw new ValidationFailedException(List.of("Order " + existing.erpOrderId() + " for this quote is " + existing.status() + "."));
        orders.recordInbox(in.tenantId(), in.correlationId(), in.quoteId(), existing.erpOrderId());
        return new ErpOrderResult(existing.erpOrderId(), false);
    }

    private void requireActiveCustomer(OrderRequest in) {
        if (!customers.existsByErpCustomerIdAndTenantIdAndCrmCustomerIdAndStatus(in.erpCustomerId(), in.tenantId(), in.crmCustomerId(), "Active"))
            throw new ValidationFailedException(List.of(
                "ERP customer " + in.erpCustomerId() + " is not an active customer of this tenant matching CRM customer " + in.crmCustomerId() + "."));
    }

    private void requirePricing(OrderRequest in) {
        var skus = in.lines().stream().map(OrderRequest.Line::sku).distinct().toList();
        var floors = new HashMap<String, BigDecimal>();
        for (ErpProduct p : products.findByTenantIdAndStatusAndSkuIn(in.tenantId(), "Active", skus)) floors.put(p.getSku(), p.getMinUnitPrice());
        var errors = OrderPlanner.checkPricing(in.lines(), floors);
        if (!errors.isEmpty()) throw new ValidationFailedException(errors);
    }

    private static boolean blank(String s) { return s == null || s.isBlank(); }
}
