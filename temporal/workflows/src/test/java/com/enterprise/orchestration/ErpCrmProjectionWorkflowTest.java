package com.enterprise.orchestration;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;

import io.temporal.client.WorkflowOptions;
import io.temporal.testing.TestWorkflowEnvironment;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

class ErpCrmProjectionWorkflowTest {
    static class Crm implements CrmFulfilmentActivities {
        final List<String> applied = new ArrayList<>();
        public void recordShipment(Map<String, Object> input) { applied.add("shipment:" + input.get("erpShipmentId")); }
        public void recordInvoice(Map<String, Object> input) { applied.add("invoice:" + input.get("erpInvoiceId")); }
        public void recordPayment(Map<String, Object> input) { applied.add("payment:" + input.get("erpPaymentId")); }
    }

    TestWorkflowEnvironment env;
    Crm crm;

    @BeforeEach void setUp() {
        env = TestWorkflowEnvironment.newInstance();
        crm = new Crm();
        env.newWorker(Contracts.ORCHESTRATION_QUEUE).registerWorkflowImplementationTypes(ErpCrmProjectionWorkflowImpl.class);
        env.newWorker(Contracts.CRM_QUEUE).registerActivitiesImplementations(crm);
        env.start();
    }
    @AfterEach void tearDown() { env.close(); }

    @Test void dispatchesEachEventToItsMatchingCrmActivity() {
        var options = WorkflowOptions.newBuilder().setTaskQueue(Contracts.ORCHESTRATION_QUEUE).setWorkflowId("projection-shipment").build();
        var shipment = env.getWorkflowClient().newWorkflowStub(ErpCrmProjectionWorkflow.class, options);
        shipment.apply(new ErpCrmProjectionWorkflow.ErpCrmProjectionInput("shipment", Map.of("erpShipmentId", "S-1")));
        var invoice = env.getWorkflowClient().newWorkflowStub(ErpCrmProjectionWorkflow.class,
                WorkflowOptions.newBuilder().setTaskQueue(Contracts.ORCHESTRATION_QUEUE).setWorkflowId("projection-invoice").build());
        invoice.apply(new ErpCrmProjectionWorkflow.ErpCrmProjectionInput("invoice", Map.of("erpInvoiceId", "I-1")));
        var payment = env.getWorkflowClient().newWorkflowStub(ErpCrmProjectionWorkflow.class,
                WorkflowOptions.newBuilder().setTaskQueue(Contracts.ORCHESTRATION_QUEUE).setWorkflowId("projection-payment").build());
        payment.apply(new ErpCrmProjectionWorkflow.ErpCrmProjectionInput("payment", Map.of("erpPaymentId", "P-1")));
        assertEquals(List.of("shipment:S-1", "invoice:I-1", "payment:P-1"), crm.applied);
    }

    @Test void rejectsUnsupportedEventTypesWithoutCallingCrm() {
        var workflow = env.getWorkflowClient().newWorkflowStub(ErpCrmProjectionWorkflow.class,
                WorkflowOptions.newBuilder().setTaskQueue(Contracts.ORCHESTRATION_QUEUE).setWorkflowId("projection-invalid").build());
        assertThrows(Exception.class, () -> workflow.apply(
                new ErpCrmProjectionWorkflow.ErpCrmProjectionInput("unknown", Map.of())));
        assertEquals(0, crm.applied.size());
    }
}
