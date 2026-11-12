package com.enterprise.orchestration;

import io.temporal.client.WorkflowClient;
import io.temporal.client.WorkflowClientOptions;
import io.temporal.serviceclient.WorkflowServiceStubs;
import io.temporal.serviceclient.WorkflowServiceStubsOptions;
import io.temporal.worker.WorkerFactory;

/** Runs workflow definitions only (no activities): deployable independently of the CRM and ERP. */
public final class OrchestrationWorker {
    public static void main(String[] args) {
        var address = env("TEMPORAL_ADDRESS", "localhost:7233");
        var namespace = env("TEMPORAL_NAMESPACE", "enterprise");
        var service = WorkflowServiceStubs.newServiceStubs(WorkflowServiceStubsOptions.newBuilder().setTarget(address).build());
        var client = WorkflowClient.newInstance(service, WorkflowClientOptions.newBuilder().setNamespace(namespace).build());
        var factory = WorkerFactory.newInstance(client);
        factory.newWorker(Contracts.ORCHESTRATION_QUEUE).registerWorkflowImplementationTypes(CustomerOnboardingWorkflowImpl.class, QuoteToOrderWorkflowImpl.class,
                ReturnProcessingWorkflowImpl.class);
        factory.start();
        Runtime.getRuntime().addShutdownHook(new Thread(factory::shutdown));
        System.out.println("Orchestration worker started on " + address + " ns=" + namespace);
    }

    private static String env(String k, String d) { var v = System.getenv(k); return v == null || v.isBlank() ? d : v; }
}
