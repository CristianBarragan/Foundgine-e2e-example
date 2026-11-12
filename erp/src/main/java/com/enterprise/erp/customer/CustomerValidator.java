package com.enterprise.erp.customer;

import java.util.ArrayList;
import java.util.List;
import java.util.regex.Pattern;

/** Pure validation of an incoming customer. No framework dependencies. */
public final class CustomerValidator {
    private static final Pattern EMAIL = Pattern.compile("^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$");

    private CustomerValidator() {}

    public static List<String> validate(CustomerSync in) {
        var errors = new ArrayList<String>();
        if (in == null) return List.of("Customer is required.");
        if (in.schemaVersion() != 1) errors.add("Unsupported schemaVersion " + in.schemaVersion() + ".");
        if (blank(in.tenantId()) || in.tenantId().length() > 64) errors.add("tenantId is required (max 64).");
        if (in.crmCustomerId() <= 0) errors.add("crmCustomerId must be positive.");
        if (blank(in.correlationId()) || in.correlationId().length() > 64) errors.add("correlationId is required (max 64).");
        if (!"Business".equals(in.kind()) && !"Individual".equals(in.kind())) errors.add("kind must be Business or Individual.");
        if (blank(in.name()) || in.name().length() > 200) errors.add("name is required (max 200).");
        if (!blank(in.email()) && (in.email().length() > 320 || !EMAIL.matcher(in.email()).matches())) errors.add("email is not valid.");
        if (in.phone() != null && in.phone().length() > 50) errors.add("phone is too long (max 50).");
        if (!"Active".equals(in.status()) && !"Inactive".equals(in.status())) errors.add("status must be Active or Inactive.");
        return errors;
    }

    private static boolean blank(String s) { return s == null || s.isBlank(); }
}
