package com.enterprise.erp.integration;

import com.fasterxml.jackson.core.type.TypeReference;
import com.fasterxml.jackson.databind.ObjectMapper;
import io.temporal.client.WorkflowClient;
import io.temporal.client.WorkflowExecutionAlreadyStarted;
import io.temporal.client.WorkflowNotFoundException;
import io.temporal.client.WorkflowOptions;
import io.temporal.api.enums.v1.WorkflowIdReusePolicy;
import java.util.List;
import java.util.Map;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.scheduling.annotation.Scheduled;
import org.springframework.stereotype.Component;

/** Leased at-least-once relay. Duplicate signals are safe because the C# workflow applies entity versions monotonically. */
@Component
@ConditionalOnProperty(name = "temporal.enabled", havingValue = "true")
public class TemporalFulfilmentEventPublisher {
    private final JdbcClient jdbc;
    private final WorkflowClient temporal;
    private final ObjectMapper mapper;
    private final String orchestrationQueue;
    public TemporalFulfilmentEventPublisher(JdbcClient jdbc, WorkflowClient temporal, ObjectMapper mapper,
            @Value("${temporal.orchestration-task-queue:orchestration}") String orchestrationQueue) {
        this.jdbc = jdbc; this.temporal = temporal; this.mapper = mapper; this.orchestrationQueue = orchestrationQueue;
    }

    @Scheduled(fixedDelayString = "${temporal.outbox-poll-ms:1000}")
    public void publishBatch() {
        for (var row : claimBatch()) {
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
                jdbc.sql("UPDATE erp_temporal_event_outbox SET published_at=now(), locked_until=NULL, last_error=NULL WHERE event_id=:id")
                    .param("id", row.eventId()).update();
            } catch (Exception e) {
                String message = e.getMessage() == null ? e.getClass().getSimpleName() : e.getMessage();
                if (message.length() > 1000) message = message.substring(0, 1000);
                jdbc.sql("""
                    UPDATE erp_temporal_event_outbox
                    SET locked_until=NULL,
                        available_at=now() + (LEAST(300, POWER(2, LEAST(attempts, 8))::integer) * interval '1 second'),
                        last_error=:error
                    WHERE event_id=:id
                    """).param("error", message).param("id", row.eventId()).update();
            }
        }
    }

    private List<OutboxRow> claimBatch() {
        return jdbc.sql("""
            UPDATE erp_temporal_event_outbox
            SET locked_until=now() + interval '30 seconds', attempts=attempts+1
            WHERE event_id IN (
                SELECT event_id FROM erp_temporal_event_outbox
                WHERE published_at IS NULL AND available_at <= now()
                  AND (locked_until IS NULL OR locked_until < now())
                ORDER BY created_at FOR UPDATE SKIP LOCKED LIMIT 50
            )
            RETURNING event_id, tenant_id, crm_quote_id, event_type, payload::text
            """).query((rs, n) -> new OutboxRow(rs.getObject("event_id", java.util.UUID.class),
                rs.getString("tenant_id"), rs.getInt("crm_quote_id"), rs.getString("event_type"), rs.getString("payload"))).list();
    }
    private record OutboxRow(java.util.UUID eventId, String tenantId, int quoteId, String eventType, String payload) {}
}
