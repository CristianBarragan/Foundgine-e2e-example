package com.enterprise.erp;

import com.enterprise.erp.customer.ErpCustomerRepository;
import com.enterprise.erp.fulfilment.ErpInvoiceRepository;
import com.enterprise.erp.fulfilment.ErpPaymentRepository;
import com.enterprise.erp.fulfilment.ErpShipmentRepository;
import com.enterprise.erp.integration.ErpOutboxEventRepository;
import com.enterprise.erp.inventory.ErpInventoryRepository;
import com.enterprise.erp.inventory.ErpProductRepository;
import com.enterprise.erp.order.SalesOrderLineRepository;
import com.enterprise.erp.order.SalesOrderRepository;
import com.enterprise.erp.order.StockReservationRepository;
import com.enterprise.erp.returns.ErpReturnLineRepository;
import com.enterprise.erp.returns.ErpReturnRepository;
import org.springframework.test.context.bean.override.mockito.MockitoBean;

/**
 * For the Spring Boot tests that start the whole application without a database (DataSource, Flyway and the transaction
 * manager are excluded in their properties). With no DataSource Spring Data creates no repositories either, so the
 * services that need them could not be wired; every repository is replaced by a mock instead. A new repository
 * interface goes here too, or those tests fail with "No qualifying bean".
 */
public abstract class NoDatabaseTestBase {
    @MockitoBean ErpCustomerRepository customerRepository;
    @MockitoBean ErpInventoryRepository inventoryRepository;
    @MockitoBean ErpProductRepository productRepository;
    @MockitoBean SalesOrderRepository salesOrderRepository;
    @MockitoBean SalesOrderLineRepository salesOrderLineRepository;
    @MockitoBean StockReservationRepository stockReservationRepository;
    @MockitoBean ErpShipmentRepository shipmentRepository;
    @MockitoBean ErpInvoiceRepository invoiceRepository;
    @MockitoBean ErpPaymentRepository paymentRepository;
    @MockitoBean ErpOutboxEventRepository outboxEventRepository;
    @MockitoBean ErpReturnRepository returnRepository;
    @MockitoBean ErpReturnLineRepository returnLineRepository;
}
