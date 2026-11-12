package com.enterprise.erp.integration;

import com.enterprise.erp.order.CancelOrderRequest;
import com.enterprise.erp.order.CancelOrderResult;
import com.enterprise.erp.order.ErpOrderResult;
import com.enterprise.erp.order.OrderRequest;
import io.temporal.activity.ActivityInterface;
import io.temporal.activity.ActivityMethod;

/** ERP-owned order activities on the "erp" task queue. Names are the cross-language contract. */
@ActivityInterface
public interface ErpOrderActivities {
    String CREATE_ORDER_WITH_RESERVATION = "erp.create-order-with-reservation";
    String CANCEL_ORDER = "erp.cancel-order";

    /** Non-retryable failure types: "ValidationFailed" (customer, pricing, request) and "InsufficientStock". */
    @ActivityMethod(name = CREATE_ORDER_WITH_RESERVATION)
    ErpOrderResult createOrderWithReservation(OrderRequest input);

    /** Compensation. Idempotent; "ValidationFailed" (non-retryable) if the order does not belong to the tenant and quote. */
    @ActivityMethod(name = CANCEL_ORDER)
    CancelOrderResult cancelOrder(CancelOrderRequest input);
}
