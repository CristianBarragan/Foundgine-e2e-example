package com.enterprise.erp.integration;

import tools.jackson.core.type.TypeReference;
import tools.jackson.databind.ObjectMapper;
import io.temporal.client.WorkflowClient;
import io.temporal.client.WorkflowExecutionAlreadyStarted;
import io.temporal.client.WorkflowNotFoundException;
import io.temporal.client.WorkflowOptions;
import io.temporal.api.enums.v1.WorkflowIdReusePolicy;
import java.util.Map;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.scheduling.annotation.Scheduled;
import org.springframework.stereotype.Component;

/** Leased at-least-once relay. Duplicate signals are safe because the C# workflow applies entity versions monotonically. */
@Component
@ConditionalOnProperty(name = "temporal.enabled", havingValue = "true")
public class TemporalFulfilmentEventPublisher {
    private final ErpTemporalEventOutbox outbox;
    private final WorkflowClient temporal;
    private final ObjectMapper mapper;
    private final String orchestrationQueue;
    public TemporalFulfilmentEventPublisher(ErpTemporalEventOutbox outbox, WorkflowClient temporal, ObjectMapper mapper,
            @Value("${temporal.orchestration-task-queue:orchestration}") String orchestrationQueue) {
        this.outbox = outbox; this.temporal = temporal; this.mapper = mapper; this.orchestrationQueue = orchestrationQueue;
    }

    @Scheduled(fixedDelayString = "${temporal.outbox-poll-ms:1000}")
    public void publishBatch() {
        for (var row : outbox.claimBatch()) {
            try {
                Map<String, Object> payload = mapper.readValue(row.payload(), new TypeReference<>() {});
                String projectionId = "ErpCrmProjection:" + row.tenantId() + ":" + row.eventId();
                var projectionOptions = WorkflowOptions.newBuilder()
                    .setWorkflowId(projectionId)
                    .setTaskQueue(orchestrationQueue)
                    .setWorkflowIdReusePolicy(WorkflowIdReusePolicy.WORKFLOW_ID_REUSE_POLICY_REJECT_DUPLICATE)
                    .build();
                try {
                    temporal.newUntypedWorkflowStub("ErpCrmProjectionWorkflow", projectionOptions)
                        .start(Map.of("eventType", row.eventType(), "payload", payload));
                } catch (WorkflowExecutionAlreadyStarted alreadyStarted) {
                    // A prior publisher instance started the same stable event workflow; continue to parent signalling.
                }
                String parentId = "CustomerToCash:" + row.tenantId() + ":" + row.quoteId();
                try {
                    temporal.newUntypedWorkflowStub(parentId).signal(row.eventType(), payload);
                } catch (WorkflowNotFoundException closedOrNotStarted) {
                    // Projection workflow above is authoritative for CRM updates, including events after parent completion.
                }
                outbox.markPublished(row.eventId());
            } catch (Exception e) {
                String message = e.getMessage() == null ? e.getClass().getSimpleName() : e.getMessage();
                if (message.length() > 1000) message = message.substring(0, 1000);
                outbox.markFailed(row.eventId(), message);
            }
        }
    }
}
