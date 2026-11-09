package com.foundgine.samples.supplychain.advanced.integration;

import java.math.BigDecimal;
import java.math.RoundingMode;
import java.time.LocalDate;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;

/**
 * Pure FIFO allocation of a required quantity across inventory lots. No I/O: the store locks the
 * lots, calls this, and then applies the plan. All-or-nothing per call.
 */
public final class ReservationPlanner {
    public record Lot(int lotId, BigDecimal available, LocalDate receivedOn) {}

    public record Allocation(int lotId, int quantity) {}

    public record Outcome(boolean satisfied, List<Allocation> allocations, int shortfall) {}

    private ReservationPlanner() {}

    public static Outcome plan(int needed, List<Lot> lots) {
        if (needed <= 0) throw new IllegalArgumentException("Quantity must be positive.");
        var ordered =
                lots.stream()
                        .sorted(
                                Comparator.comparing(Lot::receivedOn)
                                        .thenComparingInt(Lot::lotId))
                        .toList();
        var allocations = new ArrayList<Allocation>();
        int remaining = needed;
        for (var lot : ordered) {
            if (remaining == 0) break;
            int free = wholeUnits(lot.available());
            if (free <= 0) continue;
            int take = Math.min(free, remaining);
            allocations.add(new Allocation(lot.lotId(), take));
            remaining -= take;
        }
        return remaining == 0
                ? new Outcome(true, List.copyOf(allocations), 0)
                : new Outcome(false, List.of(), remaining);
    }

    private static int wholeUnits(BigDecimal available) {
        if (available == null || available.signum() <= 0) return 0;
        var whole = available.setScale(0, RoundingMode.DOWN);
        return whole.compareTo(BigDecimal.valueOf(Integer.MAX_VALUE)) > 0
                ? Integer.MAX_VALUE
                : whole.intValue();
    }
}
