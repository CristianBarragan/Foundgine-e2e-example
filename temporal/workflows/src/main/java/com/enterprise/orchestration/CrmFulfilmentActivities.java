package com.enterprise.orchestration;

import io.temporal.activity.ActivityInterface;
import io.temporal.activity.ActivityMethod;
import java.util.Map;

/** CRM activity contract. The CRM .NET worker deserializes the same camelCase JSON payload into its typed DTO. */
@ActivityInterface
public interface CrmFulfilmentActivities {
    @ActivityMethod(name = "crm.record-shipment")
    void recordShipment(Map<String, Object> input);

    @ActivityMethod(name = "crm.record-invoice")
    void recordInvoice(Map<String, Object> input);

    @ActivityMethod(name = "crm.record-payment")
    void recordPayment(Map<String, Object> input);
}
