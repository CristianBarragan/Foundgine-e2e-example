package com.enterprise.orchestration;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;

import io.temporal.client.WorkflowException;
import io.temporal.client.WorkflowOptions;
import io.temporal.failure.ApplicationFailure;
import io.temporal.testing.TestWorkflowEnvironment;
import java.util.ArrayList;
import java.util.Collections;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.concurrent.atomic.AtomicInteger;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

/** Duplicate, out-of-order and CRM-outage delivery of ERP events through the projection workflow. */
class ErpProjectionDeliveryTest {
    /** Mirrors the CRM rule: a write applies only when strictly newer than what the CRM already holds. */
    static class Crm implements CrmFulfilmentActivities {
        final Map<String, Integer> versions = Collections.synchronizedMap(new HashMap<>());
        final List<String> calls = Collections.synchronizedList(new ArrayList<>());
        final AtomicInteger failuresLeft = new AtomicInteger();
        volatile String failureType; // null => transient (retryable) outage

        private void apply(String kind, Map<String, Object> in, String idKey) {
            calls.add(kind);
            if (failuresLeft.getAndUpdate(n -> n > 0 ? n - 1 : 0) > 0) {
                if (failureType == null) throw new RuntimeException("CRM unavailable");
                throw ApplicationFailure.newFailure("bad status", failureType);
            }
            versions.merge(kind + ":" + in.get(idKey), ((Number) in.get("version")).intValue(), Math::max);
        }
        public void recordShipment(Map<String, Object> in) { apply("shipment", in, "erpShipmentId"); }
        public void recordInvoice(Map<String, Object> in) { apply("invoice", in, "erpInvoiceId"); }
        public void recordPayment(Map<String, Object> in) { apply("payment", in, "erpPaymentId"); }
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

    private ErpCrmProjectionWorkflow stub(String id) {
        return env.getWorkflowClient().newWorkflowStub(ErpCrmProjectionWorkflow.class,
                WorkflowOptions.newBuilder().setTaskQueue(Contracts.ORCHESTRATION_QUEUE).setWorkflowId(id).build());
    }

    private static ErpCrmProjectionWorkflow.ErpCrmProjectionInput shipment(int version) {
        return new ErpCrmProjectionWorkflow.ErpCrmProjectionInput("shipment",
                Map.of("erpShipmentId", "S-1", "version", version));
    }

    @Test void duplicateDeliveryIsAbsorbed() {
        stub("evt-1").apply(shipment(1));
        stub("evt-1-redelivery").apply(shipment(1));
        assertEquals(1, crm.versions.get("shipment:S-1"));
    }

    @Test void outOfOrderDeliveryNeverRegressesTheProjection() {
        stub("evt-v2").apply(shipment(2));
        stub("evt-v1").apply(shipment(1)); // older fact arrives late
        assertEquals(2, crm.versions.get("shipment:S-1"));
    }

    @Test void crmOutageIsRetriedUntilItRecovers() {
        crm.failuresLeft.set(3);
        stub("evt-outage").apply(shipment(1));
        assertEquals(4, crm.calls.size());
        assertEquals(1, crm.versions.get("shipment:S-1"));
    }

    @Test void validationFailureIsNotRetried() {
        crm.failuresLeft.set(100);
        crm.failureType = "CrmValidationException";
        assertThrows(WorkflowException.class, () -> stub("evt-invalid").apply(shipment(1)));
        assertEquals(1, crm.calls.size());
        assertEquals(null, crm.versions.get("shipment:S-1"));
    }
}
