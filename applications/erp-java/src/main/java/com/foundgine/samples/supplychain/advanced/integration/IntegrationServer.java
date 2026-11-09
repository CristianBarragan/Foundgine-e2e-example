package com.foundgine.samples.supplychain.advanced.integration;

import static com.foundgine.samples.supplychain.advanced.integration.IntegrationModels.*;

import com.fasterxml.jackson.databind.DeserializationFeature;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.foundgine.samples.supplychain.advanced.ambiguity.AmbiguityConnectionStrings;
import com.foundgine.samples.supplychain.advanced.postgres.PostgresSchema;
import com.sun.net.httpserver.HttpExchange;
import com.sun.net.httpserver.HttpServer;

import java.io.IOException;
import java.net.InetSocketAddress;
import java.net.URI;
import java.nio.charset.StandardCharsets;
import java.sql.DriverManager;
import java.time.Duration;
import java.util.Map;
import java.util.function.BiFunction;
import java.util.regex.Pattern;

/**
 * ERP-owned adapter endpoints called by the Temporal worker. Dependency-free HTTP (JDK HttpServer),
 * like the MCP server. Every route except /health requires a verified Keycloak bearer token carrying
 * the 'orchestration-service' realm role; tenant confinement is enforced by IntegrationStore.
 */
public final class IntegrationServer {
    public static final int DEFAULT_PORT = 8082;
    private static final int MAX_BODY_BYTES = 1_048_576;
    private static final Pattern RELEASE = Pattern.compile("^/api/integration/inventory/reservations/([^/]+)/release$");
    private static final Pattern CANCEL = Pattern.compile("^/api/integration/orders/([^/]+)/cancel$");

    /** Opens a fresh connection per request (no pooling in this sample). */
    @FunctionalInterface
    public interface ConnectionFactory {
        java.sql.Connection open() throws java.sql.SQLException;
    }

    private final ObjectMapper mapper =
            new ObjectMapper().configure(DeserializationFeature.FAIL_ON_UNKNOWN_PROPERTIES, false);
    private final JwtVerifier verifier;
    private final ConnectionFactory connections;
    private final HttpServer server;

    public IntegrationServer(JwtVerifier verifier, ConnectionFactory connections, String host, int port) throws IOException {
        this.verifier = verifier;
        this.connections = connections;
        this.server = HttpServer.create(new InetSocketAddress(host, port), 0);
        server.createContext("/health", ex -> send(ex, 200, Map.of("status", "ok", "component", "foundgine-erp-integration")));
        server.createContext("/api/integration/", this::handle);
    }

    public void start() { server.start(); }

    public void stop() { server.stop(0); }

    public int port() { return server.getAddress().getPort(); }

    private void handle(HttpExchange ex) throws IOException {
        try {
            if (!"POST".equalsIgnoreCase(ex.getRequestMethod())) { send(ex, 405, error("method_not_allowed", "POST only.")); return; }
            try {
                verifier.verify(ex.getRequestHeaders().getFirst("Authorization"));
            } catch (JwtVerifier.AuthException e) {
                send(ex, e.status(), error(e.status() == 403 ? "forbidden" : "unauthorized", e.getMessage()));
                return;
            }
            byte[] raw = ex.getRequestBody().readNBytes(MAX_BODY_BYTES + 1);
            if (raw.length > MAX_BODY_BYTES) { send(ex, 413, error("payload_too_large", "Body exceeds 1 MiB.")); return; }
            String path = ex.getRequestURI().getRawPath();

            Object result;
            if (path.equals("/api/integration/orders")) {
                result = run(raw, CreateOrder.class, (s, b) -> s.createOrder(b));
            } else if (path.equals("/api/integration/inventory/reservations")) {
                result = run(raw, Reserve.class, (s, b) -> s.reserveInventory(b));
            } else if (RELEASE.matcher(path).matches()) {
                String id = decode(RELEASE.matcher(path), path);
                result = run(raw, Compensation.class, (s, b) -> s.releaseReservation(id, b));
            } else if (CANCEL.matcher(path).matches()) {
                String id = decode(CANCEL.matcher(path), path);
                result = run(raw, Compensation.class, (s, b) -> s.cancelOrder(id, b));
            } else {
                send(ex, 404, error("not_found", "Unknown integration route."));
                return;
            }
            send(ex, 200, result);
        } catch (IntegrationException e) {
            send(ex, e.status(), error(e.code(), e.getMessage()));
        } catch (com.fasterxml.jackson.core.JsonProcessingException e) {
            send(ex, 400, error("invalid_json", "Request body is not valid for this route."));
        } catch (Exception e) {
            // Never leak internals; the cause is for server logs only.
            System.err.println("Integration request failed: " + e);
            send(ex, 500, error("internal_error", "Unexpected error."));
        }
    }

    private <B, R> R run(byte[] raw, Class<B> type, BiFunction<IntegrationStore, B, R> action) throws Exception {
        B body = mapper.readValue(raw, type);
        try (var connection = connections.open()) {
            return action.apply(new IntegrationStore(connection, mapper), body);
        }
    }

    private static String decode(java.util.regex.Matcher m, String path) {
        m.reset();
        m.matches();
        return java.net.URLDecoder.decode(m.group(1), StandardCharsets.UTF_8);
    }

    private static Map<String, String> error(String code, String message) {
        return Map.of("error", code, "message", message);
    }

    private void send(HttpExchange ex, int status, Object body) throws IOException {
        byte[] bytes = mapper.writeValueAsBytes(body);
        ex.getResponseHeaders().add("Content-Type", "application/json");
        ex.sendResponseHeaders(status, bytes.length);
        try (var os = ex.getResponseBody()) { os.write(bytes); }
    }

    public static void main(String[] args) throws Exception {
        String jdbcUrl = AmbiguityConnectionStrings.jdbcUrl("FOUNDGINE_POSTGRES_CONNECTION_STRING");
        if (jdbcUrl == null) throw new IllegalStateException("FOUNDGINE_POSTGRES_CONNECTION_STRING is required.");
        var config = new JwtVerifier.Config(
                required("ERP_JWT_ISSUER"), required("ERP_JWT_AUDIENCE"),
                System.getenv().getOrDefault("ERP_REQUIRED_ROLE", "orchestration-service"), Duration.ofSeconds(60));
        var verifier = JwtVerifier.withJwks(config, URI.create(required("ERP_JWKS_URI")));
        if ("true".equalsIgnoreCase(System.getenv("ERP_APPLY_SCHEMA"))) {
            // Demo/dev only: applies schema.sql, integration.sql AND seed.sql.
            try (var c = DriverManager.getConnection(jdbcUrl)) { PostgresSchema.apply(c); }
        }
        int port = Integer.parseInt(System.getenv().getOrDefault("ERP_HTTP_PORT", String.valueOf(DEFAULT_PORT)));
        var host = System.getenv().getOrDefault("ERP_HTTP_HOST", "0.0.0.0");
        var server = new IntegrationServer(verifier, () -> DriverManager.getConnection(jdbcUrl), host, port);
        server.start();
        System.out.println("Foundgine ERP integration endpoints listening on " + host + ":" + server.port());
    }

    private static String required(String name) {
        String v = System.getenv(name);
        if (v == null || v.isBlank()) throw new IllegalStateException(name + " is required.");
        return v;
    }
}
