package com.foundgine.samples.supplychain.advanced.integration;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;

import java.math.BigInteger;
import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.charset.StandardCharsets;
import java.security.KeyFactory;
import java.security.PublicKey;
import java.security.Signature;
import java.security.spec.RSAPublicKeySpec;
import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.util.Base64;
import java.util.HashSet;
import java.util.Map;
import java.util.Set;
import java.util.function.Function;

/**
 * Minimal Keycloak/OIDC access-token verifier (RS256 only) built on the JDK. It checks signature,
 * exp/nbf, issuer, audience and a required realm role. It is deliberately strict: any other
 * algorithm (including "none"/HS256) is rejected, so a forged or downgraded token cannot pass.
 */
public final class JwtVerifier {
    public record Config(String issuer, String audience, String requiredRole, Duration clockSkew) {}

    public record Principal(String subject, Set<String> roles) {}

    public static final class AuthException extends RuntimeException {
        private final int status;

        public AuthException(int status, String message) {
            super(message);
            this.status = status;
        }

        public int status() {
            return status;
        }
    }

    private final Config config;
    private final Function<String, PublicKey> keyResolver;
    private final Clock clock;
    private final ObjectMapper mapper = new ObjectMapper();

    public JwtVerifier(Config config, Function<String, PublicKey> keyResolver, Clock clock) {
        this.config = config;
        this.keyResolver = keyResolver;
        this.clock = clock;
    }

    public static JwtVerifier withJwks(Config config, URI jwksUri) {
        return new JwtVerifier(config, new JwksKeyResolver(jwksUri), Clock.systemUTC());
    }

    public Principal verify(String authorizationHeader) {
        if (authorizationHeader == null || !authorizationHeader.regionMatches(true, 0, "Bearer ", 0, 7))
            throw new AuthException(401, "Bearer token required.");
        String token = authorizationHeader.substring(7).trim();
        String[] parts = token.split("\\.", -1);
        if (parts.length != 3) throw new AuthException(401, "Malformed token.");
        try {
            var b64 = Base64.getUrlDecoder();
            JsonNode header = mapper.readTree(b64.decode(parts[0]));
            if (!"RS256".equals(header.path("alg").asText())) throw new AuthException(401, "Unsupported token algorithm.");
            PublicKey key = keyResolver.apply(header.path("kid").asText(""));
            if (key == null) throw new AuthException(401, "Unknown signing key.");

            Signature sig = Signature.getInstance("SHA256withRSA");
            sig.initVerify(key);
            sig.update((parts[0] + "." + parts[1]).getBytes(StandardCharsets.US_ASCII));
            if (!sig.verify(b64.decode(parts[2]))) throw new AuthException(401, "Invalid token signature.");

            JsonNode claims = mapper.readTree(b64.decode(parts[1]));
            Instant now = clock.instant();
            if (!claims.hasNonNull("exp")) throw new AuthException(401, "Token has no expiry.");
            if (now.isAfter(Instant.ofEpochSecond(claims.get("exp").asLong()).plus(config.clockSkew())))
                throw new AuthException(401, "Token expired.");
            if (claims.hasNonNull("nbf") && now.plus(config.clockSkew()).isBefore(Instant.ofEpochSecond(claims.get("nbf").asLong())))
                throw new AuthException(401, "Token not yet valid.");
            if (!config.issuer().equals(claims.path("iss").asText())) throw new AuthException(401, "Wrong issuer.");
            if (!hasAudience(claims.path("aud"), config.audience())) throw new AuthException(401, "Wrong audience.");

            Set<String> roles = new HashSet<>();
            for (JsonNode r : claims.path("realm_access").path("roles")) roles.add(r.asText());
            if (!roles.contains(config.requiredRole())) throw new AuthException(403, "Missing required role.");
            return new Principal(claims.path("sub").asText(""), Set.copyOf(roles));
        } catch (AuthException e) {
            throw e;
        } catch (Exception e) {
            throw new AuthException(401, "Invalid token.");
        }
    }

    private static boolean hasAudience(JsonNode aud, String expected) {
        if (aud.isTextual()) return expected.equals(aud.asText());
        for (JsonNode a : aud) if (expected.equals(a.asText())) return true;
        return false;
    }

    /** Fetches and caches the realm JWKS; refetches (rate-limited) when an unknown kid appears. */
    static final class JwksKeyResolver implements Function<String, PublicKey> {
        private static final Duration MAX_AGE = Duration.ofMinutes(10);
        private static final Duration MIN_REFETCH = Duration.ofSeconds(30);
        private final URI uri;
        private final HttpClient http = HttpClient.newBuilder().connectTimeout(Duration.ofSeconds(5)).build();
        private final ObjectMapper mapper = new ObjectMapper();
        private volatile Map<String, PublicKey> keys = Map.of();
        private volatile Instant fetchedAt = Instant.EPOCH;

        JwksKeyResolver(URI uri) {
            this.uri = uri;
        }

        @Override
        public PublicKey apply(String kid) {
            Instant now = Instant.now();
            boolean stale = fetchedAt.plus(MAX_AGE).isBefore(now);
            PublicKey known = keys.get(kid);
            if (known != null && !stale) return known;
            if (known == null || stale) {
                if (fetchedAt.plus(MIN_REFETCH).isBefore(now)) refresh(now);
            }
            return keys.get(kid);
        }

        private synchronized void refresh(Instant now) {
            try {
                var response = http.send(
                        HttpRequest.newBuilder(uri).timeout(Duration.ofSeconds(5)).GET().build(),
                        HttpResponse.BodyHandlers.ofString());
                if (response.statusCode() != 200) return;
                var fresh = new java.util.HashMap<String, PublicKey>();
                var factory = KeyFactory.getInstance("RSA");
                for (JsonNode k : mapper.readTree(response.body()).path("keys")) {
                    if (!"RSA".equals(k.path("kty").asText()) || !k.hasNonNull("kid")) continue;
                    if (k.hasNonNull("use") && !"sig".equals(k.get("use").asText())) continue;
                    var dec = Base64.getUrlDecoder();
                    var spec = new RSAPublicKeySpec(
                            new BigInteger(1, dec.decode(k.path("n").asText())),
                            new BigInteger(1, dec.decode(k.path("e").asText())));
                    fresh.put(k.get("kid").asText(), factory.generatePublic(spec));
                }
                if (!fresh.isEmpty()) keys = Map.copyOf(fresh);
            } catch (Exception ignored) {
                // Keep serving the previous key set; fetchedAt still advances so we do not hammer Keycloak.
            } finally {
                fetchedAt = now;
            }
        }
    }
}
