package com.enterprise.erp.returns;

import java.util.function.Supplier;

/** Pure rule: what an operator's "goods arrived late" request means for a return, given its ERP status and its original workflow. */
public final class ExpiredReturnPlanner {
    public enum Plan {
        /** Authorized and the original workflow is no longer waiting: start the resolution. */
        RESOLVE,
        /** The original workflow still waits for goods: use goods-received, not the operator path. */
        STILL_WAITING,
        /** Already Completed: nothing to do. */
        ALREADY_COMPLETED,
        /** No such return for this tenant (another tenant's return looks the same). */
        NOT_FOUND,
        /** Rejected (or any other state): there is nothing to resolve. */
        NOT_RESOLVABLE
    }

    private ExpiredReturnPlanner() {}

    /**
     * @param status   the ERP return status for (tenant, CRM return id), or null when there is no such return
     * @param original asked only for an Authorized return, so Temporal is not called for the other answers
     */
    public static Plan decide(String status, Supplier<ReturnResolutions.WorkflowState> original) {
        if (status == null) return Plan.NOT_FOUND;
        return switch (status) {
            case "Completed" -> Plan.ALREADY_COMPLETED;
            case "Authorized" -> original.get() == ReturnResolutions.WorkflowState.RUNNING ? Plan.STILL_WAITING : Plan.RESOLVE;
            default -> Plan.NOT_RESOLVABLE;
        };
    }
}
