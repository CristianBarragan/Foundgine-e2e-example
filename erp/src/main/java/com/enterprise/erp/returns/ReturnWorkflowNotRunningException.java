package com.enterprise.erp.returns;

public class ReturnWorkflowNotRunningException extends RuntimeException {
    public ReturnWorkflowNotRunningException(String workflowId, Throwable cause) {
        super("No running workflow " + workflowId + ".", cause);
    }
}
