package com.enterprise.erp;

import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.autoconfigure.web.servlet.AutoConfigureMockMvc;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.test.context.bean.override.mockito.MockitoBean;
import org.springframework.test.web.servlet.MockMvc;

// The health check needs no database: exclude the datasource/Flyway auto-configuration and stub the JdbcClient.
@SpringBootTest(properties = {
        "spring.autoconfigure.exclude=org.springframework.boot.autoconfigure.jdbc.DataSourceAutoConfiguration,"
                + "org.springframework.boot.autoconfigure.flyway.FlywayAutoConfiguration,"
                + "org.springframework.boot.autoconfigure.jdbc.DataSourceTransactionManagerAutoConfiguration"})
@AutoConfigureMockMvc
class HealthTest {
    @Autowired MockMvc mvc;
    @MockitoBean org.springframework.jdbc.core.simple.JdbcClient jdbcClient;

    @Test
    void healthIsOk() throws Exception { mvc.perform(get("/api/v1/health")).andExpect(status().isOk()); }
}
