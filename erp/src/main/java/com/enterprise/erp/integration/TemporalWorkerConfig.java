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
import org.springframework.scheduling.annotation.EnableScheduling;

/** Hosts the ERP activities in the ERP process, like the CRM does. Off unless temporal.enabled=true. */
@Configuration
@EnableScheduling
@ConditionalOnProperty(name = "temporal.enabled", havingValue = "true")
class TemporalWorkerConfig {
    @Bean(destroyMethod = "shutdown")
    WorkflowServiceStubs temporalService(@Value("${temporal.address}") String address) {
        return WorkflowServiceStubs.newServiceStubs(WorkflowServiceStubsOptions.newBuilder().setTarget(address).build());
    }

    /** Shared by the worker and by the REST side (signals to running workflows). */
    @Bean
    WorkflowClient temporalClient(WorkflowServiceStubs service, @Value("${temporal.namespace}") String namespace) {
        return WorkflowClient.newInstance(service, WorkflowClientOptions.newBuilder().setNamespace(namespace).build());
    }

    @Bean(initMethod = "start", destroyMethod = "shutdown")
    WorkerFactory temporalWorkerFactory(WorkflowClient client, ErpCustomerActivitiesImpl customerActivities,
                                        ErpOrderActivitiesImpl orderActivities, ErpReturnActivitiesImpl returnActivities,
                                        FulfilmentBotActivitiesImpl botActivities,
                                        @Value("${temporal.erp-task-queue}") String queue) {
        var factory = WorkerFactory.newInstance(client);
        factory.newWorker(queue).registerActivitiesImplementations(customerActivities, orderActivities, returnActivities, botActivities);
        return factory;
    }
}
