package com.enterprise.orchestration;

import io.temporal.activity.ActivityInterface;
import io.temporal.activity.ActivityMethod;

/** Client-side view of the ERP order activities (implemented in the ERP service). */
@ActivityInterface
public interface ErpOrderActivities {
    @ActivityMethod(name = Contracts.ERP_CREATE_ORDER)
    Contracts.ErpOrderResult createOrderWithReservation(Contracts.QuoteAccepted input);

    @ActivityMethod(name = Contracts.ERP_CANCEL_ORDER)
    Contracts.CancelOrderResult cancelOrder(Contracts.CancelOrderRequest input);
}
