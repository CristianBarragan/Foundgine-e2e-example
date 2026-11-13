package com.enterprise.orchestration;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.fasterxml.jackson.databind.JsonNode;
import com.google.protobuf.ByteString;
import io.temporal.api.common.v1.Payload;
import io.temporal.common.converter.DefaultDataConverter;
import java.math.BigDecimal;
import java.util.Map;
import org.junit.jupiter.api.Test;

/**
 * The C# worker and the Java worker never share a binary, only JSON. These tests push the shared golden payloads
 * through Temporal's own default data converter, i.e. the code path that really runs when a C# workflow starts a Java
 * child or a Java relay signals a C# parent. The C# side deserializes the very same files (Crm.Api.Tests / Crm.Worker.Tests).
 */
class CrossLanguageContractTest {
    private static final DefaultDataConverter CONVERTER = DefaultDataConverter.newDefaultInstance();

    private static Payload jsonPayload(byte[] json) {
        return Payload.newBuilder()
                .putMetadata("encoding", ByteString.copyFromUtf8("json/plain"))
                .setData(ByteString.copyFrom(json)).build();
    }

    @Test void theQuotePassedByTheCSharpParentDeserializesIntoTheJavaWorkflowInput() throws Exception {
        JsonNode start = ContractFixtures.json("customer-to-cash-start.v1.json");
        byte[] quoteJson = ContractFixtures.MAPPER.writeValueAsBytes(start.get("quote"));

        var quote = CONVERTER.fromPayload(jsonPayload(quoteJson), Contracts.QuoteAccepted.class, Contracts.QuoteAccepted.class);

        assertEquals(1, quote.schemaVersion());
        assertEquals("acme", quote.tenantId());
        assertEquals(7, quote.quoteId());
        assertEquals(3, quote.opportunityId());
        assertEquals(42, quote.crmCustomerId());
        assertEquals("ERP-9001", quote.erpCustomerId());
        assertEquals("corr-quote-7", quote.correlationId());
        assertEquals(0, new BigDecimal("200.00").compareTo(quote.total()));
        assertEquals(1, quote.lines().size());
        assertEquals("SKU-1", quote.lines().get(0).sku());
        assertEquals(2, quote.lines().get(0).quantity());
        assertEquals(0, new BigDecimal("100.00").compareTo(quote.lines().get(0).unitPrice()));
    }

    @Test void theJavaResultHasExactlyTheShapeTheCSharpParentReads() throws Exception {
        for (String fixture : new String[] {"quote-to-order-result.v1.json", "quote-to-order-result-failed.v1.json"}) {
            JsonNode expected = ContractFixtures.json(fixture);
            var result = new Contracts.QuoteToOrderResult(expected.get("status").asText(),
                    expected.get("erpOrderId").asText(), expected.get("reason").asText());

            Payload payload = CONVERTER.toPayload(result).orElseThrow();
            assertEquals("json/plain", payload.getMetadataOrThrow("encoding").toStringUtf8());
            JsonNode actual = ContractFixtures.MAPPER.readTree(payload.getData().toByteArray());

            assertTrue(ContractFixtures.equivalent(expected, actual), fixture + " expected " + expected + " but was " + actual);
        }
    }

    @Test void theCSharpResultDeserializesIntoTheJavaRecord() {
        var result = CONVERTER.fromPayload(jsonPayload(ContractFixtures.bytes("quote-to-order-result-failed.v1.json")),
                Contracts.QuoteToOrderResult.class, Contracts.QuoteToOrderResult.class);
        assertEquals("Failed", result.status());
        assertEquals("", result.erpOrderId());
        assertEquals("Insufficient stock for SKU-1 (requested 2).", result.reason());
    }

    @Test void everyErpEventSurvivesTheProjectionWorkflowInputUnchanged() throws Exception {
        for (String type : new String[] {"shipment", "invoice", "payment"}) {
            String fixture = "erp-event-" + type + ".v1.json";
            // The relay reads the outbox JSON into a Map and starts the workflow with it, exactly like this.
            @SuppressWarnings("unchecked")
            Map<String, Object> payload = ContractFixtures.MAPPER.readValue(ContractFixtures.bytes(fixture), Map.class);
            var input = new ErpCrmProjectionWorkflow.ErpCrmProjectionInput(type, payload);

            Payload wire = CONVERTER.toPayload(input).orElseThrow();
            var received = CONVERTER.fromPayload(wire, ErpCrmProjectionWorkflow.ErpCrmProjectionInput.class,
                    ErpCrmProjectionWorkflow.ErpCrmProjectionInput.class);

            assertEquals(type, received.eventType());
            JsonNode actual = ContractFixtures.MAPPER.valueToTree(received.payload());
            assertTrue(ContractFixtures.equivalent(ContractFixtures.json(fixture), actual),
                    fixture + " changed on the way through the workflow input: " + actual);
        }
    }

    @Test void aSignalPayloadFromTheRelayCarriesTheFieldNamesTheCSharpSignalsExpect() throws Exception {
        JsonNode invoice = ContractFixtures.json("erp-event-invoice.v1.json");
        // These are the property names pinned by RecordInvoiceInput's JsonPropertyName attributes.
        for (String name : new String[] {"tenantId", "erpOrderId", "erpInvoiceId", "number", "total", "status", "issuedAt", "dueAt", "version"})
            assertTrue(invoice.has(name), "invoice fixture is missing " + name);
        assertEquals(9, invoice.size());
    }
}
