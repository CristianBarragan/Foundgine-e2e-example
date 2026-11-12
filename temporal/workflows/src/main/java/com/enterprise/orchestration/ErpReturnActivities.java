package com.enterprise.orchestration;

import io.temporal.activity.ActivityInterface;
import io.temporal.activity.ActivityMethod;

/** Client-side view of the ERP return activities (implemented in the ERP service). */
@ActivityInterface
public interface ErpReturnActivities {
    @ActivityMethod(name = Contracts.ERP_AUTHORIZE_RETURN)
    Contracts.ErpReturnDecision authorizeReturn(Contracts.ReturnRequested input);

    @ActivityMethod(name = Contracts.ERP_COMPLETE_RETURN)
    Contracts.CompleteReturnResult completeReturn(Contracts.CompleteReturnRequest input);
}
