package com.enterprise.erp.customer;

import java.util.List;

/** Business validation failed; retrying the same input cannot succeed. */
public class ValidationFailedException extends RuntimeException {
    private final List<String> errors;

    public ValidationFailedException(List<String> errors) {
        super(String.join("; ", errors));
        this.errors = List.copyOf(errors);
    }

    public List<String> errors() { return errors; }
}
