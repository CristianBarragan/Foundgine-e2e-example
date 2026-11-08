package com.enterprise.erp.security;

import org.springframework.http.HttpStatus;
import org.springframework.web.bind.annotation.ResponseStatus;

@ResponseStatus(HttpStatus.FORBIDDEN)
public class TenantRequiredException extends RuntimeException {
    public TenantRequiredException() { super("Token has no tenant."); }
}
