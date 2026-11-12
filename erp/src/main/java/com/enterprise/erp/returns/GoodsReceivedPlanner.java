package com.enterprise.erp.returns;

/** Pure rule: what a goods-received request means for a return in a given ERP status (no framework, no database). */
public final class GoodsReceivedPlanner {
    public enum Plan {
        /** Authorized: tell the workflow the goods arrived. */
        SIGNAL,
        /** Already Completed: nothing to do, the request is a harmless repeat. */
        ALREADY_COMPLETED,
        /** No such return for this tenant (another tenant's return looks the same). */
        NOT_FOUND,
        /** Rejected (or any other state): goods cannot be received against it. */
        NOT_RECEIVABLE
    }

    private GoodsReceivedPlanner() {}

    /** @param status the ERP return status for (tenant, CRM return id), or null when there is no such return */
    public static Plan decide(String status) {
        if (status == null) return Plan.NOT_FOUND;
        return switch (status) {
            case "Authorized" -> Plan.SIGNAL;
            case "Completed" -> Plan.ALREADY_COMPLETED;
            default -> Plan.NOT_RECEIVABLE;
        };
    }
}
