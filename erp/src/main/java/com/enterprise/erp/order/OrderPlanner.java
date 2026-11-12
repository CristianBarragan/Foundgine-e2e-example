package com.enterprise.erp.order;

import java.math.BigDecimal;
import java.math.RoundingMode;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.TreeMap;
import java.util.regex.Pattern;

/** Pure order rules (no framework, no database): request validation, pricing floor, reservation totals. */
public final class OrderPlanner {
    private static final Pattern SKU = Pattern.compile("^[A-Za-z0-9._-]{1,64}$");

    private OrderPlanner() {}

    /** Structural validation of the incoming request. Collects every problem. */
    public static List<String> validate(OrderRequest in) {
        var errors = new ArrayList<String>();
        if (in == null) return List.of("Order request is required.");
        if (in.schemaVersion() != 1) errors.add("Unsupported schemaVersion " + in.schemaVersion() + ".");
        if (blank(in.tenantId()) || in.tenantId().length() > 64) errors.add("tenantId is required (max 64).");
        if (in.quoteId() <= 0) errors.add("quoteId must be positive.");
        if (blank(in.erpCustomerId()) || in.erpCustomerId().length() > 64) errors.add("erpCustomerId is required (max 64).");
        if (blank(in.correlationId()) || in.correlationId().length() > 64) errors.add("correlationId is required (max 64).");
        if (in.total() == null || in.total().signum() < 0) errors.add("total is required and cannot be negative.");
        if (in.lines() == null || in.lines().isEmpty()) {
            errors.add("At least one line is required.");
            return errors;
        }
        int n = 0;
        for (var l : in.lines()) {
            n++;
            if (l == null) { errors.add("Line " + n + " is missing."); continue; }
            if (l.sku() == null || !SKU.matcher(l.sku()).matches()) errors.add("Line " + n + ": sku is required (letters, digits, . _ -, max 64).");
            if (l.quantity() <= 0) errors.add("Line " + n + ": quantity must be positive.");
            if (l.unitPrice() == null || l.unitPrice().signum() < 0) errors.add("Line " + n + ": unitPrice is required and cannot be negative.");
        }
        if (errors.isEmpty() && in.total().compareTo(linesTotal(in.lines())) != 0)
            errors.add("total " + in.total() + " does not equal the sum of the lines " + linesTotal(in.lines()) + ".");
        return errors;
    }

    /** Sum of quantity x unit price at 2 decimals, the way the CRM computes a quote total. */
    public static BigDecimal linesTotal(List<OrderRequest.Line> lines) {
        var sum = BigDecimal.ZERO;
        for (var l : lines) sum = sum.add(l.unitPrice().multiply(BigDecimal.valueOf(l.quantity())));
        return sum.setScale(2, RoundingMode.HALF_UP);
    }

    /** Stock to reserve per SKU. Several lines of one SKU are summed; sorted so locks are always taken in the same order. */
    public static Map<String, Integer> reservations(List<OrderRequest.Line> lines) {
        var bySku = new TreeMap<String, Integer>();
        for (var l : lines) bySku.merge(l.sku(), l.quantity(), Integer::sum);
        return bySku;
    }

    /** Pricing validation against the catalogue: every SKU must be an active product and no line may be under its floor. */
    public static List<String> checkPricing(List<OrderRequest.Line> lines, Map<String, BigDecimal> floorBySku) {
        var errors = new ArrayList<String>();
        for (var l : lines) {
            var floor = floorBySku.get(l.sku());
            if (floor == null) { errors.add("Unknown or discontinued product " + l.sku() + "."); continue; }
            if (l.unitPrice().compareTo(floor) < 0)
                errors.add("Price " + l.unitPrice() + " for " + l.sku() + " is below the minimum " + floor + ".");
        }
        return errors;
    }

    private static boolean blank(String s) { return s == null || s.isBlank(); }
}
