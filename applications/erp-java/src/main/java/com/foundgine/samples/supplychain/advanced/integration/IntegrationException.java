package com.foundgine.samples.supplychain.advanced.integration;

/** A domain/contract failure with the HTTP status the integration endpoint should return. */
public final class IntegrationException extends RuntimeException {
    private final int status;
    private final String code;

    public IntegrationException(int status, String code, String message) {
        super(message);
        this.status = status;
        this.code = code;
    }

    public int status() {
        return status;
    }

    public String code() {
        return code;
    }
}
