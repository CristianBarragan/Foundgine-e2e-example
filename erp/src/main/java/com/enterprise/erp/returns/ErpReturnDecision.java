package com.enterprise.erp.returns;

/**
 * decision is "Authorized" or "Rejected" (a business outcome, not a failure). erpReturnId is set for both (the ERP keeps a
 * record of rejected returns so a replay answers the same). created = false on a replay.
 */
public record ErpReturnDecision(String decision, String erpReturnId, String note, boolean created) {}
