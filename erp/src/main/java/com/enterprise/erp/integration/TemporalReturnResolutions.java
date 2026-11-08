package com.enterprise.erp.integration;

import com.enterprise.erp.returns.ReturnResolutions;
import com.enterprise.erp.returns.ReturnSignals;
import io.grpc.Status;
import io.grpc.StatusRuntimeException;
import io.temporal.api.common.v1.WorkflowExecution;
import io.temporal.api.enums.v1.WorkflowExecutionStatus;
import io.temporal.api.enums.v1.WorkflowIdReusePolicy;
import io.temporal.api.workflowservice.v1.DescribeWorkflowExecutionRequest;
import io.temporal.client.WorkflowClient;
import io.temporal.client.WorkflowExecutionAlreadyStarted;
import io.temporal.client.WorkflowOptions;
import java.util.Map;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.stereotype.Component;

/** Describes the original return workflow and starts ReturnResolutionWorkflow through the ERP's Temporal client. Present only when temporal.enabled=true. */
@Component
@ConditionalOnProperty(name = "temporal.enabled", havingValue = "true")
class TemporalReturnResolutions implements ReturnResolutions {
    private final WorkflowClient client;
    private final String orchestrationQueue;

    TemporalReturnResolutions(WorkflowClient client, @Value("${temporal.orchestration-task-queue:orchestration}") String orchestrationQueue) {
        this.client = client;
        this.orchestrationQueue = orchestrationQueue;
    }

    @Override
    public WorkflowState originalWorkflow(String tenantId, int crmReturnId) {
        var request = DescribeWorkflowExecutionRequest.newBuilder().setNamespace(client.getOptions().getNamespace())
                .setExecution(WorkflowExecution.newBuilder().setWorkflowId(ReturnSignals.workflowId(tenantId, crmReturnId)).build()).build();
        try {
            var status = client.getWorkflowServiceStubs().blockingStub().describeWorkflowExecution(request).getWorkflowExecutionInfo().getStatus();
            return status == WorkflowExecutionStatus.WORKFLOW_EXECUTION_STATUS_RUNNING ? WorkflowState.RUNNING : WorkflowState.CLOSED;
        } catch (StatusRuntimeException e) {
            if (e.getStatus().getCode() == Status.Code.NOT_FOUND) return WorkflowState.UNKNOWN;
            throw e;
        }
    }

    @Override
    public boolean resolveReceivedLate(String tenantId, int crmReturnId, String erpReturnId, String resolvedBy) {
        // A failed earlier run may be started again; a running or completed one may not (that is the idempotency).
        var options = WorkflowOptions.newBuilder().setWorkflowId(ReturnResolutions.workflowId(tenantId, crmReturnId))
                .setTaskQueue(orchestrationQueue)
                .setWorkflowIdReusePolicy(WorkflowIdReusePolicy.WORKFLOW_ID_REUSE_POLICY_ALLOW_DUPLICATE_FAILED_ONLY).build();
        var stub = client.newUntypedWorkflowStub(WORKFLOW_NAME, options);
        try {
            // same JSON shape as Contracts.ResolveExpiredReturn
            stub.start(Map.of("tenantId", tenantId, "returnId", crmReturnId, "erpReturnId", erpReturnId, "resolvedBy", resolvedBy));
            return true;
        } catch (WorkflowExecutionAlreadyStarted e) {
            return false;
        }
    }
}
