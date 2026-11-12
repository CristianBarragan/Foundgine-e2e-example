package com.enterprise.erp.integration;

import io.temporal.client.WorkflowClient;
import io.temporal.client.WorkflowClientOptions;
import io.temporal.serviceclient.WorkflowServiceStubs;
import io.temporal.serviceclient.WorkflowServiceStubsOptions;
import io.temporal.worker.WorkerFactory;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

/** Hosts the ERP activities in the ERP process, like the CRM does. Off unless temporal.enabled=true. */
@Configuration
@ConditionalOnProperty(name = "temporal.enabled", havingValue = "true")
class TemporalWorkerConfig {
    @Bean(destroyMethod = "shutdown")
    WorkflowServiceStubs temporalService(@Value("${temporal.address}") String address) {
        return WorkflowServiceStubs.newServiceStubs(WorkflowServiceStubsOptions.newBuilder().setTarget(address).build());
    }

    @Bean(initMethod = "start", destroyMethod = "shutdown")
    WorkerFactory temporalWorkerFactory(WorkflowServiceStubs service, ErpCustomerActivitiesImpl customerActivities,
                                        ErpOrderActivitiesImpl orderActivities, ErpReturnActivitiesImpl returnActivities,
                                        @Value("${temporal.namespace}") String namespace,
                                        @Value("${temporal.erp-task-queue}") String queue) {
        var client = WorkflowClient.newInstance(service, WorkflowClientOptions.newBuilder().setNamespace(namespace).build());
        var factory = WorkerFactory.newInstance(client);
        factory.newWorker(queue).registerActivitiesImplementations(customerActivities, orderActivities, returnActivities);
        return factory;
    }
}
