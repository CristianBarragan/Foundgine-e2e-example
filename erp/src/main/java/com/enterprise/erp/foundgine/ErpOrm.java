package com.enterprise.erp.foundgine;

import com.enterprise.erp.customer.ErpCustomer;
import com.enterprise.erp.fulfilment.ErpInvoice;
import com.enterprise.erp.fulfilment.ErpPayment;
import com.enterprise.erp.fulfilment.ErpShipment;
import com.enterprise.erp.order.SalesOrder;
import com.enterprise.erp.returns.ErpReturn;
import com.enterprise.erp.security.ErpCaller;
import com.foundgine.providers.storage.orm.jpa.FoundgineOrm;
import java.util.Map;

/** Fluent Foundgine JPA configuration for the six single-key ERP entities. */
public final class ErpOrm {
    static final Map<String, Class<?>> ENTITY_TYPES = Map.of(
            "Customer", ErpCustomer.class,
            "SalesOrder", SalesOrder.class,
            "Return", ErpReturn.class,
            "Shipment", ErpShipment.class,
            "Invoice", ErpInvoice.class,
            "Payment", ErpPayment.class);

    private ErpOrm() {}

    public static FoundgineOrm.Builder<ErpCaller> configure() {
        return FoundgineOrm.<ErpCaller>configure()
                .metadata(new ErpFoundgineModel().build())
                .entities(ENTITY_TYPES)
                .attributeNaming(FoundgineOrm.AttributeNaming.CAMEL_CASE)
                // The ERP currently runs on the JVM and does not invoke the Foundgine AOT processor.
                .allowReflection();
    }

    public static FoundgineOrm<ErpCaller> build() {
        return configure().authorization(new ErpAuthorization()).build();
    }
}
