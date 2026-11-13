
package com.enterprise.erp.integration;

import static org.junit.jupiter.api.Assertions.assertTrue;

import com.fasterxml.jackson.databind.DeserializationFeature;
import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.SerializationFeature;
import com.fasterxml.jackson.datatype.jsr310.JavaTimeModule;
import java.math.BigDecimal;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.Instant;
import org.junit.jupiter.api.Test;

import org.springframework.http.converter.json.Jackson2ObjectMapperBuilder;
import org.springframework.boot.test.context.SpringBootTest;

/**
 * The ERP outbox JSON must equal the golden files consumed by the C# signals
 * (contracts/examples).
 */
class ErpEventContractTest {

    // Match the event contract: Java time values must be ISO-8601 strings,
    // not numeric epoch timestamps.
    private static final ObjectMapper BOOT = Jackson2ObjectMapperBuilder.json()
            .modules(new JavaTimeModule())
            .featuresToDisable(SerializationFeature.WRITE_DATES_AS_TIMESTAMPS)
            .build();

    private static final ObjectMapper READER = new ObjectMapper()
            .enable(DeserializationFeature.USE_BIG_DECIMAL_FOR_FLOATS);

    private static JsonNode fixture(String name) throws Exception {
        for (Path dir = Path.of("").toAbsolutePath(); dir != null; dir = dir.getParent()) {
            Path file = dir.resolve("contracts/examples").resolve(name);

            if (Files.exists(file)) {
                return READER.readTree(Files.readAllBytes(file));
            }
        }

        throw new IllegalStateException("fixture not found: " + name);
    }

    /**
     * Numbers compare by value (200.00 == 200.0).
     * Objects ignore key order but must have identical key sets.
     */
    private static boolean same(JsonNode a, JsonNode b) {
        if (a.isNumber() && b.isNumber()) {
            return a.decimalValue().compareTo(b.decimalValue()) == 0;
        }

        if (a.isObject() && b.isObject()) {
            if (a.size() != b.size()) {
                return false;
            }

            var names = a.fieldNames();

            while (names.hasNext()) {
                var name = names.next();

                if (!b.has(name) || !same(a.get(name), b.get(name))) {
                    return false;
                }
            }

            return true;
        }

        return a.equals(b);
    }

    private static void assertMatches(String fixtureName, Object event) throws Exception {
        JsonNode expected = fixture(fixtureName);
        JsonNode actual = READER.readTree(BOOT.writeValueAsBytes(event));

        assertTrue(
                same(expected, actual),
                fixtureName + " drifted.\nexpected: " + expected + "\nactual:   " + actual);
    }

    @Test
    void shipmentEventMatchesGoldenFile() throws Exception {
        assertMatches(
                "erp-event-shipment.v1.json",
                new ErpTemporalEventOutbox.ShipmentEvent(
                        "acme",
                        "SO-5001",
                        "SHP-0001",
                        "Shipped",
                        "NZ Post",
                        "TRK-100",
                        2));
    }

    @Test
    void invoiceEventMatchesGoldenFile() throws Exception {
        assertMatches(
                "erp-event-invoice.v1.json",
                new ErpTemporalEventOutbox.InvoiceEvent(
                        "acme",
                        "SO-5001",
                        "INV-0001",
                        "2026-0001",
                        new BigDecimal("200.00"),
                        "Issued",
                        Instant.parse("2026-11-13T01:00:00Z"),
                        Instant.parse("2026-12-13T01:00:00Z"),
                        1));
    }

    @Test
    void paymentEventMatchesGoldenFile() throws Exception {
        assertMatches(
                "erp-event-payment.v1.json",
                new ErpTemporalEventOutbox.PaymentEvent(
                        "acme",
                        "SO-5001",
                        "INV-0001",
                        "PAY-0001",
                        new BigDecimal("200.00"),
                        "Reconciled",
                        2));
    }
}
