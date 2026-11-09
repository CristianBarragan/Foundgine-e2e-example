package com.foundgine.samples.supplychain.advanced.integration;

import static org.junit.jupiter.api.Assertions.*;

import com.foundgine.samples.supplychain.advanced.integration.ReservationPlanner.Allocation;
import com.foundgine.samples.supplychain.advanced.integration.ReservationPlanner.Lot;

import java.math.BigDecimal;
import java.time.LocalDate;
import java.util.List;
import org.junit.jupiter.api.Test;

class ReservationPlannerTest {
    private static Lot lot(int id, String available, String received) {
        return new Lot(id, new BigDecimal(available), LocalDate.parse(received));
    }

    @Test
    void allocatesOldestLotFirstAndSplitsAcrossLots() {
        var outcome = ReservationPlanner.plan(
                12, List.of(lot(2, "10", "2026-08-02"), lot(1, "5", "2026-08-01"), lot(3, "50", "2026-08-03")));
        assertTrue(outcome.satisfied());
        assertEquals(List.of(new Allocation(1, 5), new Allocation(2, 7)), outcome.allocations());
        assertEquals(0, outcome.shortfall());
    }

    @Test
    void tiesOnReceiptDateBreakByLotId() {
        var outcome = ReservationPlanner.plan(3, List.of(lot(9, "10", "2026-08-01"), lot(4, "10", "2026-08-01")));
        assertEquals(List.of(new Allocation(4, 3)), outcome.allocations());
    }

    @Test
    void shortfallReturnsNoAllocationsAndTheMissingQuantity() {
        var outcome = ReservationPlanner.plan(20, List.of(lot(1, "5", "2026-08-01"), lot(2, "10.00", "2026-08-02")));
        assertFalse(outcome.satisfied());
        assertTrue(outcome.allocations().isEmpty());
        assertEquals(5, outcome.shortfall());
    }

    @Test
    void ignoresLotsWithNoWholeUnitAvailable() {
        var outcome = ReservationPlanner.plan(2, List.of(lot(1, "0.99", "2026-08-01"), lot(2, "-3", "2026-08-01"), lot(3, "2.75", "2026-08-02")));
        assertEquals(List.of(new Allocation(3, 2)), outcome.allocations());
    }

    @Test
    void rejectsNonPositiveQuantity() {
        assertThrows(IllegalArgumentException.class, () -> ReservationPlanner.plan(0, List.of()));
    }
}
