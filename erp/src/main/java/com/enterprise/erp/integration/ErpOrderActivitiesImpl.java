package com.enterprise.erp.integration;

import com.enterprise.erp.customer.ValidationFailedException;
import com.enterprise.erp.order.CancelOrderRequest;
import com.enterprise.erp.order.CancelOrderResult;
import com.enterprise.erp.order.ErpOrderResult;
import com.enterprise.erp.order.ErpOrderService;
import com.enterprise.erp.order.InsufficientStockException;
import com.enterprise.erp.order.OrderRequest;
import io.temporal.failure.ApplicationFailure;
import org.springframework.stereotype.Component;

@Component
public class ErpOrderActivitiesImpl implements ErpOrderActivities {
    public static final String VALIDATION_FAILED = "ValidationFailed";
    public static final String INSUFFICIENT_STOCK = "InsufficientStock";
    private final ErpOrderService service;

    public ErpOrderActivitiesImpl(ErpOrderService service) { this.service = service; }

    @Override
    public ErpOrderResult createOrderWithReservation(OrderRequest input) {
        try {
            return service.createOrderWithReservation(input);
        } catch (ValidationFailedException e) {
            throw ApplicationFailure.newNonRetryableFailure(e.getMessage(), VALIDATION_FAILED);
        } catch (InsufficientStockException e) {
            throw ApplicationFailure.newNonRetryableFailure(e.getMessage(), INSUFFICIENT_STOCK);
        }
    }

    @Override
    public CancelOrderResult cancelOrder(CancelOrderRequest input) {
        try {
            return service.cancelOrder(input);
        } catch (ValidationFailedException e) {
            throw ApplicationFailure.newNonRetryableFailure(e.getMessage(), VALIDATION_FAILED);
        }
    }
}
