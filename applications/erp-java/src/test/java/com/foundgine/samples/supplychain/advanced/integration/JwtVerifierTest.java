package com.foundgine.samples.supplychain.advanced.integration;

import static org.junit.jupiter.api.Assertions.*;

import java.nio.charset.StandardCharsets;
import java.security.KeyPair;
import java.security.KeyPairGenerator;
import java.security.Signature;
import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.time.ZoneOffset;
import java.util.Base64;
import org.junit.jupiter.api.Test;

class JwtVerifierTest {
    private static final Instant NOW = Instant.parse("2026-11-07T00:00:00Z");
    private static final String ISS = "https://id.example/realms/foundgine";
    private static final String AUD = "foundgine-erp";
    private final KeyPair pair = newPair();
    private final KeyPair other = newPair();
    private final JwtVerifier verifier = new JwtVerifier(
            new JwtVerifier.Config(ISS, AUD, "orchestration-service", Duration.ofSeconds(60)),
            kid -> "k1".equals(kid) ? pair.getPublic() : null,
            Clock.fixed(NOW, ZoneOffset.UTC));

    private static KeyPair newPair() {
        try {
            var g = KeyPairGenerator.getInstance("RSA");
            g.initialize(2048);
            return g.generateKeyPair();
        } catch (Exception e) { throw new IllegalStateException(e); }
    }

    private static String b64(String s) { return Base64.getUrlEncoder().withoutPadding().encodeToString(s.getBytes(StandardCharsets.UTF_8)); }

    private static String token(KeyPair signer, String alg, String kid, String claims) {
        try {
            String head = b64("{\"alg\":\"" + alg + "\",\"kid\":\"" + kid + "\"}");
            String body = b64(claims);
            var sig = Signature.getInstance("SHA256withRSA");
            sig.initSign(signer.getPrivate());
            sig.update((head + "." + body).getBytes(StandardCharsets.US_ASCII));
            return head + "." + body + "." + Base64.getUrlEncoder().withoutPadding().encodeToString(sig.sign());
        } catch (Exception e) { throw new IllegalStateException(e); }
    }

    private static String claims(long exp, String aud, String role) {
        return "{\"sub\":\"svc\",\"iss\":\"" + ISS + "\",\"aud\":" + aud + ",\"exp\":" + exp
                + ",\"realm_access\":{\"roles\":[\"" + role + "\"]}}";
    }

    private static final long FUTURE = NOW.plusSeconds(600).getEpochSecond();

    private int statusOf(String bearer) {
        return assertThrows(JwtVerifier.AuthException.class, () -> verifier.verify(bearer)).status();
    }

    @Test
    void acceptsValidTokenWithStringOrArrayAudience() {
        assertEquals("svc", verifier.verify("Bearer " + token(pair, "RS256", "k1", claims(FUTURE, "\"" + AUD + "\"", "orchestration-service"))).subject());
        assertNotNull(verifier.verify("Bearer " + token(pair, "RS256", "k1", claims(FUTURE, "[\"x\",\"" + AUD + "\"]", "orchestration-service"))));
    }

    @Test
    void missingRoleIsForbiddenNotUnauthorized() {
        assertEquals(403, statusOf("Bearer " + token(pair, "RS256", "k1", claims(FUTURE, "\"" + AUD + "\"", "crm-readonly"))));
    }

    @Test
    void rejectsExpiredWrongAudienceBadSignatureUnknownKeyAndWrongAlgorithm() {
        long past = NOW.minusSeconds(3600).getEpochSecond();
        assertEquals(401, statusOf("Bearer " + token(pair, "RS256", "k1", claims(past, "\"" + AUD + "\"", "orchestration-service"))));
        assertEquals(401, statusOf("Bearer " + token(pair, "RS256", "k1", claims(FUTURE, "\"other\"", "orchestration-service"))));
        assertEquals(401, statusOf("Bearer " + token(other, "RS256", "k1", claims(FUTURE, "\"" + AUD + "\"", "orchestration-service"))));
        assertEquals(401, statusOf("Bearer " + token(pair, "RS256", "nope", claims(FUTURE, "\"" + AUD + "\"", "orchestration-service"))));
        assertEquals(401, statusOf("Bearer " + token(pair, "none", "k1", claims(FUTURE, "\"" + AUD + "\"", "orchestration-service"))));
        assertEquals(401, statusOf(null));
        assertEquals(401, statusOf("Bearer not-a-jwt"));
    }
}
