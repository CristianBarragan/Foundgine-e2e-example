package com.enterprise.erp.returns;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.util.List;
import java.util.Map;
import org.junit.jupiter.api.Test;

class ReturnPlannerTest {
    static ReturnRequest.Line line(String sku, int q) { return new ReturnRequest.Line(sku, q); }

    static ReturnRequest ok() {
        return new ReturnRequest(1, "acme", 5, 7, 42, "ERP-9001", "SO-5001", "corr", "damaged", List.of(line("A", 2)));
    }

    @Test void validRequestHasNoErrors() { assertTrue(ReturnPlanner.validate(ok()).isEmpty()); }

    @Test void collectsStructuralProblems() {
        var r = new ReturnRequest(2, "", 0, 7, 42, "", "", "", "x".repeat(501), List.of(line("bad sku!", 0), line("bad sku!", 1)));
        assertEquals(10, ReturnPlanner.validate(r).size());
    }

    @Test void nullAndEmptyAreRejected() {
        assertEquals(1, ReturnPlanner.validate(null).size());
        var r = new ReturnRequest(1, "acme", 5, 7, 42, "ERP-9001", "SO-5001", "corr", "", List.of());
        assertTrue(ReturnPlanner.validate(r).contains("At least one line is required."));
    }

    @Test void duplicateSkuLinesAreRejected() {
        var r = new ReturnRequest(1, "acme", 5, 7, 42, "ERP-9001", "SO-5001", "corr", "", List.of(line("A", 1), line("A", 1)));
        assertEquals(1, ReturnPlanner.validate(r).size());
    }

    @Test void authorizesWithinTheReturnableQuantity() {
        assertTrue(ReturnPlanner.rejection(Map.of("A", 5), Map.of("A", 3), List.of(line("A", 2))).isEmpty());
    }

    @Test void rejectsMoreThanRemainsReturnable() {
        var r = ReturnPlanner.rejection(Map.of("A", 5), Map.of("A", 3), List.of(line("A", 3)));
        assertTrue(r.isPresent());
        assertTrue(r.get().contains("only 2 of 5"));
    }

    @Test void rejectsASkuNotOnTheOrder() {
        assertTrue(ReturnPlanner.rejection(Map.of("A", 5), Map.of(), List.of(line("Z", 1))).get().contains("not on the order"));
    }
}
