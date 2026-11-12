package com.enterprise.erp.returns;

/**
 * Port for the operator path of a return whose goods-received wait expired. Like {@link ReturnSignals}, workflow ids are
 * always derived here from the tenant and the CRM return id, never taken from a caller.
 */
public interface ReturnResolutions {
    /** State of the original ReturnProcessing workflow. UNKNOWN: no such workflow (never started, or its history was purged). */
    enum WorkflowState { RUNNING, CLOSED, UNKNOWN }

    WorkflowState originalWorkflow(String tenantId, int crmReturnId);

    /**
     * Starts ReturnResolutionWorkflow: goods arrived after the wait expired, so the ERP puts the stock back and the CRM records Completed.
     * @param resolvedBy the authenticated subject, never caller-supplied text
     * @return true when started now; false when a resolution for this return is already running or finished
     */
    boolean resolveReceivedLate(String tenantId, int crmReturnId, String erpReturnId, String resolvedBy);

    static String workflowId(String tenantId, int crmReturnId) { return "ReturnResolution:" + tenantId + ":" + crmReturnId; }

    String WORKFLOW_NAME = "ReturnResolutionWorkflow";
}
