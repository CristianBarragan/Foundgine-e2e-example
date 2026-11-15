package com.enterprise.erp.foundgine;

import com.enterprise.erp.customer.ErpCustomer;
import com.enterprise.erp.fulfilment.ErpInvoice;
import com.enterprise.erp.fulfilment.ErpPayment;
import com.enterprise.erp.fulfilment.ErpShipment;
import com.enterprise.erp.order.SalesOrder;
import com.enterprise.erp.returns.ErpReturn;
import com.foundgine.core.semantic.configuration.FoundgineEntityMap;
import com.foundgine.core.semantic.configuration.FoundgineEntityMapBuilder;

/** Maps every semantic member onto the persistence member that holds it (the JPA classes double as both). */
final class ErpEntityMaps {
    private ErpEntityMaps() {}

    static final class CustomerMap extends FoundgineEntityMap<ErpCustomer, ErpCustomer> {
        @Override
        public void configure(FoundgineEntityMapBuilder<ErpCustomer, ErpCustomer> map) {
            map.property(ErpCustomer::getErpCustomerId).to(ErpCustomer::getErpCustomerId);
            map.property(ErpCustomer::getTenantId).to(ErpCustomer::getTenantId);
            map.property(ErpCustomer::getCrmCustomerId).to(ErpCustomer::getCrmCustomerId);
            map.property(ErpCustomer::getKind).to(ErpCustomer::getKind);
            map.property(ErpCustomer::getName).to(ErpCustomer::getName);
            map.property(ErpCustomer::getEmail).to(ErpCustomer::getEmail);
            map.property(ErpCustomer::getPhone).to(ErpCustomer::getPhone);
            map.property(ErpCustomer::getStatus).to(ErpCustomer::getStatus);
        }
    }

    static final class OrderMap extends FoundgineEntityMap<SalesOrder, SalesOrder> {
        @Override
        public void configure(FoundgineEntityMapBuilder<SalesOrder, SalesOrder> map) {
            map.property(SalesOrder::getErpOrderId).to(SalesOrder::getErpOrderId);
            map.property(SalesOrder::getTenantId).to(SalesOrder::getTenantId);
            map.property(SalesOrder::getCrmQuoteId).to(SalesOrder::getCrmQuoteId);
            map.property(SalesOrder::getErpCustomerId).to(SalesOrder::getErpCustomerId);
            map.property(SalesOrder::getTotal).to(SalesOrder::getTotal);
            map.property(SalesOrder::getStatus).to(SalesOrder::getStatus);
            map.property(SalesOrder::getCancelReason).to(SalesOrder::getCancelReason);
            map.property(SalesOrder::getCorrelationId).to(SalesOrder::getCorrelationId);
            map.property(SalesOrder::getUpdatedAt).to(SalesOrder::getUpdatedAt);
        }
    }

    static final class ReturnMap extends FoundgineEntityMap<ErpReturn, ErpReturn> {
        @Override
        public void configure(FoundgineEntityMapBuilder<ErpReturn, ErpReturn> map) {
            map.property(ErpReturn::getErpReturnId).to(ErpReturn::getErpReturnId);
            map.property(ErpReturn::getTenantId).to(ErpReturn::getTenantId);
            map.property(ErpReturn::getCrmReturnId).to(ErpReturn::getCrmReturnId);
            map.property(ErpReturn::getErpOrderId).to(ErpReturn::getErpOrderId);
            map.property(ErpReturn::getStatus).to(ErpReturn::getStatus);
            map.property(ErpReturn::getReason).to(ErpReturn::getReason);
            map.property(ErpReturn::getNote).to(ErpReturn::getNote);
            map.property(ErpReturn::getCorrelationId).to(ErpReturn::getCorrelationId);
            map.property(ErpReturn::getUpdatedAt).to(ErpReturn::getUpdatedAt);
        }
    }

    static final class ShipmentMap extends FoundgineEntityMap<ErpShipment, ErpShipment> {
        @Override
        public void configure(FoundgineEntityMapBuilder<ErpShipment, ErpShipment> map) {
            map.property(ErpShipment::getErpShipmentId).to(ErpShipment::getErpShipmentId);
            map.property(ErpShipment::getTenantId).to(ErpShipment::getTenantId);
            map.property(ErpShipment::getErpOrderId).to(ErpShipment::getErpOrderId);
            map.property(ErpShipment::getStatus).to(ErpShipment::getStatus);
            map.property(ErpShipment::getCarrier).to(ErpShipment::getCarrier);
            map.property(ErpShipment::getTrackingNumber).to(ErpShipment::getTrackingNumber);
            map.property(ErpShipment::getIdempotencyKey).to(ErpShipment::getIdempotencyKey);
            map.property(ErpShipment::getVersion).to(ErpShipment::getVersion);
            map.property(ErpShipment::getUpdatedAt).to(ErpShipment::getUpdatedAt);
        }
    }

    static final class InvoiceMap extends FoundgineEntityMap<ErpInvoice, ErpInvoice> {
        @Override
        public void configure(FoundgineEntityMapBuilder<ErpInvoice, ErpInvoice> map) {
            map.property(ErpInvoice::getErpInvoiceId).to(ErpInvoice::getErpInvoiceId);
            map.property(ErpInvoice::getTenantId).to(ErpInvoice::getTenantId);
            map.property(ErpInvoice::getErpOrderId).to(ErpInvoice::getErpOrderId);
            map.property(ErpInvoice::getInvoiceNumber).to(ErpInvoice::getInvoiceNumber);
            map.property(ErpInvoice::getTotal).to(ErpInvoice::getTotal);
            map.property(ErpInvoice::getStatus).to(ErpInvoice::getStatus);
            map.property(ErpInvoice::getIssuedAt).to(ErpInvoice::getIssuedAt);
            map.property(ErpInvoice::getDueAt).to(ErpInvoice::getDueAt);
            map.property(ErpInvoice::getVersion).to(ErpInvoice::getVersion);
            map.property(ErpInvoice::getUpdatedAt).to(ErpInvoice::getUpdatedAt);
        }
    }

    static final class PaymentMap extends FoundgineEntityMap<ErpPayment, ErpPayment> {
        @Override
        public void configure(FoundgineEntityMapBuilder<ErpPayment, ErpPayment> map) {
            map.property(ErpPayment::getErpPaymentId).to(ErpPayment::getErpPaymentId);
            map.property(ErpPayment::getTenantId).to(ErpPayment::getTenantId);
            map.property(ErpPayment::getErpOrderId).to(ErpPayment::getErpOrderId);
            map.property(ErpPayment::getErpInvoiceId).to(ErpPayment::getErpInvoiceId);
            map.property(ErpPayment::getAmount).to(ErpPayment::getAmount);
            map.property(ErpPayment::getStatus).to(ErpPayment::getStatus);
            map.property(ErpPayment::getIdempotencyKey).to(ErpPayment::getIdempotencyKey);
            map.property(ErpPayment::getVersion).to(ErpPayment::getVersion);
            map.property(ErpPayment::getUpdatedAt).to(ErpPayment::getUpdatedAt);
        }
    }

}
