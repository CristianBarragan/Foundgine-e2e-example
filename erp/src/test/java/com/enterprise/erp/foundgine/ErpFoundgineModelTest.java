package com.enterprise.erp.foundgine;

import static org.assertj.core.api.Assertions.assertThat;

import com.foundgine.core.semantic.metadata.EntityMetadata;
import com.foundgine.core.semantic.metadata.MetadataRegistry;
import java.util.ArrayList;
import java.util.List;
import org.junit.jupiter.api.Test;

/** The fluent model compiles into one registry: six entities and their relationships. No database needed. */
class ErpFoundgineModelTest {
    @Test
    void buildsRegistryWithAllSingleKeyEntities() {
        MetadataRegistry registry = new ErpFoundgineModel().build();

        List<String> names = new ArrayList<>();
        for (EntityMetadata entity : registry.entities()) names.add(entity.name());

        assertThat(names).containsExactlyInAnyOrder("Customer", "SalesOrder", "Return", "Shipment", "Invoice", "Payment");
    }

    @Test
    void declaresEachRelationship() {
        MetadataRegistry registry = new ErpFoundgineModel().build();

        int relationships = 0;
        for (var ignored : registry.relationships()) relationships++;

        // customer->orders, order->{shipments,invoices,payments,returns,customer}, return/shipment/invoice/payment->order,
        // invoice->payments, payment->invoice
        assertThat(relationships).isEqualTo(12);
    }
}
