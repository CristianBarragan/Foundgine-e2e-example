package com.enterprise.orchestration;

import com.fasterxml.jackson.databind.DeserializationFeature;
import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;

/** Loads the shared golden payloads in contracts/examples: the same files the C# tests deserialize. */
final class ContractFixtures {
    /** Floats are read as BigDecimal so 200.00 and 200.0 compare equal and nothing is lost to double rounding. */
    static final ObjectMapper MAPPER = new ObjectMapper().enable(DeserializationFeature.USE_BIG_DECIMAL_FOR_FLOATS);

    private ContractFixtures() {}

    static Path examples() {
        for (Path dir = Path.of("").toAbsolutePath(); dir != null; dir = dir.getParent()) {
            Path candidate = dir.resolve("contracts").resolve("examples");
            if (Files.isDirectory(candidate)) return candidate;
        }
        throw new IllegalStateException("contracts/examples not found above " + Path.of("").toAbsolutePath());
    }

    static byte[] bytes(String name) {
        try { return Files.readAllBytes(examples().resolve(name)); }
        catch (IOException e) { throw new IllegalStateException("Cannot read fixture " + name, e); }
    }

    static JsonNode json(String name) {
        try { return MAPPER.readTree(bytes(name)); }
        catch (IOException e) { throw new IllegalStateException("Cannot parse fixture " + name, e); }
    }

    /** Structural equality where numbers compare by value (200.00 == 200.0), objects ignore key order. */
    static boolean equivalent(JsonNode expected, JsonNode actual) {
        if (expected.isNumber() && actual.isNumber()) return expected.decimalValue().compareTo(actual.decimalValue()) == 0;
        if (expected.isObject() && actual.isObject()) {
            if (expected.size() != actual.size()) return false;
            var names = expected.fieldNames();
            while (names.hasNext()) {
                var name = names.next();
                if (!actual.has(name) || !equivalent(expected.get(name), actual.get(name))) return false;
            }
            return true;
        }
        if (expected.isArray() && actual.isArray()) {
            if (expected.size() != actual.size()) return false;
            for (int i = 0; i < expected.size(); i++) if (!equivalent(expected.get(i), actual.get(i))) return false;
            return true;
        }
        return expected.equals(actual);
    }
}
