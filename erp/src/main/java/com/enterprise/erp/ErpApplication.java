package com.enterprise.erp;

import java.util.Map;
import org.springframework.boot.SpringApplication;
import org.springframework.boot.autoconfigure.SpringBootApplication;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

@SpringBootApplication
public class ErpApplication {
    public static void main(String[] args) { SpringApplication.run(ErpApplication.class, args); }

    @RestController
    @RequestMapping("/api/v1")
    static class HealthController {
        @GetMapping("/health")
        Map<String, String> health() { return Map.of("service", "erp", "status", "ok"); }
    }
}
