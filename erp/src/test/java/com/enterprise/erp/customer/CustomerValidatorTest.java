package com.enterprise.erp.customer;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

import org.junit.jupiter.api.Test;

class CustomerValidatorTest {
    static CustomerSync ok() {
        return new CustomerSync(1, "acme", 42, 1, "corr-1", "Business", "Acme Ltd", "a@acme.test", "021 555 0100", "Active");
    }

    @Test void validCustomerHasNoErrors() { assertTrue(CustomerValidator.validate(ok()).isEmpty()); }

    @Test void emailAndPhoneAreOptional() {
        var c = new CustomerSync(1, "acme", 42, 1, "corr-1", "Individual", "Jo", "", null, "Inactive");
        assertTrue(CustomerValidator.validate(c).isEmpty());
    }

    @Test void collectsAllProblems() {
        var c = new CustomerSync(2, "", 0, 1, "", "Other", " ", "not-an-email", "x".repeat(51), "Gone");
        assertEquals(9, CustomerValidator.validate(c).size());
    }

    @Test void nullIsRejected() { assertEquals(1, CustomerValidator.validate(null).size()); }
}
