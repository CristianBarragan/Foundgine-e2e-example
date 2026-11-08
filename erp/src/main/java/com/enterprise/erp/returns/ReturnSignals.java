package com.enterprise.erp.returns;

/**
 * Port to the running ReturnProcessingWorkflow. The workflow id is always derived from the tenant and the CRM return id
 * here (never taken from a caller), so a signal can only reach a workflow of the caller's own tenant.
 */
public interface ReturnSignals {
    /**
     * @param receivedBy who confirmed receipt: the authenticated subject, never caller-supplied text
     * @throws ReturnWorkflowNotRunningException when no running workflow exists for the return (finished, expired or never started)
     */
    void goodsReceived(String tenantId, int crmReturnId, String receivedBy);

    /** Signal and workflow-id names shared with ReturnProcessingWorkflow (Java) and FulfilmentIntegrationNames (CRM). */
    static String workflowId(String tenantId, int crmReturnId) { return "ReturnProcessing:" + tenantId + ":" + crmReturnId; }

    String GOODS_RECEIVED_SIGNAL = "goodsReceived";
}
