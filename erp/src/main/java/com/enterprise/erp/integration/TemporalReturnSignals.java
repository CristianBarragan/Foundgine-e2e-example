package com.enterprise.erp.integration;

import com.enterprise.erp.returns.ReturnSignals;
import com.enterprise.erp.returns.ReturnWorkflowNotRunningException;
import io.temporal.client.WorkflowClient;
import io.temporal.client.WorkflowNotFoundException;
import java.util.Map;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.stereotype.Component;

/** Sends signals to ReturnProcessingWorkflow through the ERP's Temporal client. Present only when temporal.enabled=true. */
@Component
@ConditionalOnProperty(name = "temporal.enabled", havingValue = "true")
class TemporalReturnSignals implements ReturnSignals {
    private final WorkflowClient client;

    TemporalReturnSignals(WorkflowClient client) { this.client = client; }

    @Override
    public void goodsReceived(String tenantId, int crmReturnId, String receivedBy) {
        var workflowId = ReturnSignals.workflowId(tenantId, crmReturnId);
        // No run id: the signal goes to the current run. A closed or unknown workflow is reported as not found.
        var stub = client.newUntypedWorkflowStub(workflowId);
        try {
            stub.signal(GOODS_RECEIVED_SIGNAL, Map.of("receivedBy", receivedBy)); // same JSON shape as Contracts.GoodsReceived
        } catch (WorkflowNotFoundException e) {
            throw new ReturnWorkflowNotRunningException(workflowId, e);
        }
    }
}
