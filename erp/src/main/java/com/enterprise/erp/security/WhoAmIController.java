package com.enterprise.erp.security;

import java.util.Map;
import org.springframework.security.core.Authentication;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

/** Any authenticated caller: shows what the token grants (same shape as the CRM's /whoami). */
@RestController
@RequestMapping("/api/v1")
class WhoAmIController {
    @GetMapping("/whoami")
    Map<String, Object> whoami(Authentication authentication) {
        var caller = ErpCaller.from(authentication);
        return Map.of("subject", String.valueOf(caller.subject()), "tenant", String.valueOf(caller.tenantId()), "roles", caller.roles());
    }
}
