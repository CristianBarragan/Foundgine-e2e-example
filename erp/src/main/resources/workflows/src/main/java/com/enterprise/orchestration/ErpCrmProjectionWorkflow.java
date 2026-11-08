package com.enterprise.orchestration;

import io.temporal.workflow.WorkflowInterface;
import io.temporal.workflow.WorkflowMethod;
import java.util.Map;

/** Applies one ERP-owned lifecycle fact to the CRM projection; one workflow per stable ERP event id. */
@WorkflowInterface
public interface ErpCrmProjectionWorkflow {
    @WorkflowMethod(name = "ErpCrmProjectionWorkflow")
    void apply(ErpCrmProjectionInput input);

    record ErpCrmProjectionInput(String eventType, Map<String, Object> payload) {}
}
