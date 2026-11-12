package com.enterprise.erp.order;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.math.BigDecimal;
import java.util.List;
import java.util.Map;
import org.junit.jupiter.api.Test;

class OrderPlannerTest {
    static OrderRequest.Line line(String sku, int qty, String price) { return new OrderRequest.Line(sku, "", qty, new BigDecimal(price)); }

    static OrderRequest ok() {
        return new OrderRequest(1, "acme", 7, 3, 42, "ERP-9001", "corr", new BigDecimal("250.00"),
                List.of(line("A", 2, "100.00"), line("B", 1, "50.00")));
    }

    @Test void validRequestHasNoErrors() { assertTrue(OrderPlanner.validate(ok()).isEmpty()); }

    @Test void totalMustEqualTheLines() {
        var r = new OrderRequest(1, "acme", 7, 3, 42, "ERP-9001", "corr", new BigDecimal("999.00"), ok().lines());
        assertEquals(1, OrderPlanner.validate(r).size());
    }

    @Test void collectsStructuralProblems() {
        var r = new OrderRequest(2, "", 0, 0, 0, "", "", null, List.of(line("bad sku!", 0, "-1")));
        assertEquals(9, OrderPlanner.validate(r).size());
    }

    @Test void emptyLinesAndNullAreRejected() {
        assertEquals(1, OrderPlanner.validate(null).size());
        var r = new OrderRequest(1, "acme", 7, 3, 42, "ERP-9001", "corr", BigDecimal.ZERO, List.of());
        assertTrue(OrderPlanner.validate(r).contains("At least one line is required."));
    }

    @Test void reservationsSumRepeatedSkusInSkuOrder() {
        var res = OrderPlanner.reservations(List.of(line("B", 1, "1"), line("A", 2, "1"), line("B", 4, "1")));
        assertEquals(List.of("A", "B"), List.copyOf(res.keySet()));
        assertEquals(5, res.get("B"));
    }

    @Test void pricingRejectsUnknownSkusAndPricesBelowTheFloor() {
        var errors = OrderPlanner.checkPricing(List.of(line("A", 1, "79.99"), line("B", 1, "50.00"), line("Z", 1, "1")),
                Map.of("A", new BigDecimal("80.00"), "B", new BigDecimal("50.00")));
        assertEquals(2, errors.size());   // A under floor, Z unknown; B exactly at the floor is fine
    }
}
