package com.enterprise.orchestration;

import io.temporal.activity.ActivityOptions;
import io.temporal.common.RetryOptions;
import io.temporal.failure.ApplicationFailure;
import io.temporal.workflow.Workflow;
import java.time.Duration;

/** Durable projection relay. CRM's entity/version upserts make activity retries and duplicate events safe. */
public class ErpCrmProjectionWorkflowImpl implements ErpCrmProjectionWorkflow {
    @Override
    public void apply(ErpCrmProjectionInput input) {
        if (input == null || input.payload() == null || input.eventType() == null ||
                !input.eventType().matches("shipment|invoice|payment")) {
            throw ApplicationFailure.newNonRetryableFailure("Invalid ERP CRM projection event", "InvalidProjectionEvent");
        }
        var options = ActivityOptions.newBuilder()
                .setTaskQueue("crm")
                .setStartToCloseTimeout(Duration.ofSeconds(30))
                .setRetryOptions(RetryOptions.newBuilder()
                        .setInitialInterval(Duration.ofSeconds(1))
                        .setBackoffCoefficient(2.0)
                        .setMaximumInterval(Duration.ofMinutes(2))
                        .setMaximumAttempts(0)
                        .build())
                .build();
        var activities = Workflow.newActivityStub(CrmFulfilmentActivities.class, options);
        switch (input.eventType()) {
            case "shipment" -> activities.recordShipment(input.payload());
            case "invoice" -> activities.recordInvoice(input.payload());
            case "payment" -> activities.recordPayment(input.payload());
            default -> throw ApplicationFailure.newNonRetryableFailure("Unsupported ERP event", "InvalidProjectionEvent");
        }
    }
}
