package com.enterprise.erp.returns;

import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.TreeMap;
import java.util.regex.Pattern;

/** Pure return rules (no framework, no database). */
public final class ReturnPlanner {
    private static final Pattern SKU = Pattern.compile("^[A-Za-z0-9._-]{1,64}$");

    private ReturnPlanner() {}

    /** Structural validation. A failure here cannot be fixed by retrying (the workflow rejects the return). */
    public static List<String> validate(ReturnRequest in) {
        var errors = new ArrayList<String>();
        if (in == null) return List.of("Return request is required.");
        if (in.schemaVersion() != 1) errors.add("Unsupported schemaVersion " + in.schemaVersion() + ".");
        if (blank(in.tenantId()) || in.tenantId().length() > 64) errors.add("tenantId is required (max 64).");
        if (in.returnId() <= 0) errors.add("returnId must be positive.");
        if (blank(in.erpCustomerId()) || in.erpCustomerId().length() > 64) errors.add("erpCustomerId is required (max 64).");
        if (blank(in.erpOrderId()) || in.erpOrderId().length() > 64) errors.add("erpOrderId is required (max 64).");
        if (blank(in.correlationId()) || in.correlationId().length() > 64) errors.add("correlationId is required (max 64).");
        if (in.reason() != null && in.reason().length() > 500) errors.add("reason is too long (max 500).");
        if (in.lines() == null || in.lines().isEmpty()) { errors.add("At least one line is required."); return errors; }
        int n = 0;
        var seen = new java.util.HashSet<String>();
        for (var l : in.lines()) {
            n++;
            if (l == null) { errors.add("Line " + n + " is missing."); continue; }
            if (l.sku() == null || !SKU.matcher(l.sku()).matches()) errors.add("Line " + n + ": sku is required.");
            else if (!seen.add(l.sku())) errors.add("Line " + n + ": sku " + l.sku() + " appears twice.");
            if (l.quantity() <= 0) errors.add("Line " + n + ": quantity must be positive.");
        }
        return errors;
    }

    /** Quantity per SKU, sorted (stable lock order). */
    public static Map<String, Integer> quantities(List<ReturnRequest.Line> lines) {
        var m = new TreeMap<String, Integer>();
        for (var l : lines) m.merge(l.sku(), l.quantity(), Integer::sum);
        return m;
    }

    /**
     * Business decision: empty = authorize, otherwise the rejection reason.
     * ordered = quantity per SKU on the order; alreadyReturned = quantity per SKU on other Authorized/Completed returns.
     */
    public static Optional<String> rejection(Map<String, Integer> ordered, Map<String, Integer> alreadyReturned,
                                             List<ReturnRequest.Line> lines) {
        for (var l : lines) {
            int orderedQty = ordered.getOrDefault(l.sku(), 0);
            if (orderedQty == 0) return Optional.of("SKU " + l.sku() + " is not on the order.");
            int remaining = orderedQty - alreadyReturned.getOrDefault(l.sku(), 0);
            if (l.quantity() > remaining)
                return Optional.of("Cannot return " + l.quantity() + " of " + l.sku() + ": only " + remaining + " of " + orderedQty + " remain returnable.");
        }
        return Optional.empty();
    }

    private static boolean blank(String s) { return s == null || s.isBlank(); }
}
