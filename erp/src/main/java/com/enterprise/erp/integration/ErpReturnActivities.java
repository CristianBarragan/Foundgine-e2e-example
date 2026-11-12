package com.enterprise.erp.integration;

import com.enterprise.erp.returns.CompleteReturnRequest;
import com.enterprise.erp.returns.CompleteReturnResult;
import com.enterprise.erp.returns.ErpReturnDecision;
import com.enterprise.erp.returns.ReturnRequest;
import io.temporal.activity.ActivityInterface;
import io.temporal.activity.ActivityMethod;

/** ERP-owned return activities on the "erp" task queue. Names are the cross-language contract. */
@ActivityInterface
public interface ErpReturnActivities {
    String AUTHORIZE_RETURN = "erp.authorize-return";
    String COMPLETE_RETURN = "erp.complete-return";

    /** Business refusals come back as decision "Rejected"; only a malformed request fails (non-retryable "ValidationFailed"). */
    @ActivityMethod(name = AUTHORIZE_RETURN)
    ErpReturnDecision authorizeReturn(ReturnRequest input);

    /** Adds the returned stock back once; idempotent. "ValidationFailed" (non-retryable) if the return is unknown or Rejected. */
    @ActivityMethod(name = COMPLETE_RETURN)
    CompleteReturnResult completeReturn(CompleteReturnRequest input);
}
