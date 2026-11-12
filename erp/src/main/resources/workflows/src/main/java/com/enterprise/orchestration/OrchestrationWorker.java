package com.enterprise.orchestration;

import io.temporal.client.WorkflowClient;
import io.temporal.client.WorkflowClientOptions;
import io.temporal.serviceclient.WorkflowServiceStubs;
import io.temporal.serviceclient.WorkflowServiceStubsOptions;
import io.temporal.worker.WorkerFactory;

/** Hosts deterministic workflow definitions only; CRM and ERP activities run in their owning services. */
public final class OrchestrationWorker {
    private OrchestrationWorker() {}

    public static void main(String[] args) {
        var address = env("TEMPORAL_ADDRESS", "localhost:7233");
        var namespace = env("TEMPORAL_NAMESPACE", "enterprise");
        var queue = env("TEMPORAL_ORCHESTRATION_TASK_QUEUE", Contracts.ORCHESTRATION_QUEUE);

        var service = WorkflowServiceStubs.newServiceStubs(
                WorkflowServiceStubsOptions.newBuilder().setTarget(address).build());
        var client = WorkflowClient.newInstance(service,
                WorkflowClientOptions.newBuilder().setNamespace(namespace).build());
        var factory = WorkerFactory.newInstance(client);
        factory.newWorker(queue).registerWorkflowImplementationTypes(
                CustomerOnboardingWorkflowImpl.class,
                QuoteToOrderWorkflowImpl.class,
                ReturnProcessingWorkflowImpl.class,
                ReturnResolutionWorkflowImpl.class,
                ErpCrmProjectionWorkflowImpl.class);

        Runtime.getRuntime().addShutdownHook(new Thread(() -> {
            factory.shutdown();
            service.shutdown();
        }, "temporal-orchestration-shutdown"));

        factory.start();
        System.out.printf("Orchestration worker started: address=%s namespace=%s queue=%s%n",
                address, namespace, queue);
    }

    private static String env(String key, String fallback) {
        var value = System.getenv(key);
        return value == null || value.isBlank() ? fallback : value;
    }
}
