package com.enterprise.erp;

import com.enterprise.erp.customer.ErpCustomerRepository;
import com.enterprise.erp.customer.ErpCustomerService;
import com.enterprise.erp.fulfilment.ErpFulfilmentService;
import com.enterprise.erp.fulfilment.ErpInvoiceRepository;
import com.enterprise.erp.fulfilment.ErpPaymentRepository;
import com.enterprise.erp.fulfilment.ErpShipmentRepository;
import com.enterprise.erp.inventory.ErpInventoryRepository;
import com.enterprise.erp.inventory.ErpInventoryService;
import com.enterprise.erp.inventory.ErpProductRepository;
import com.enterprise.erp.integration.ErpOutboxEventRepository;
import com.enterprise.erp.integration.ErpTemporalEventOutbox;
import com.enterprise.erp.order.ErpOrderService;
import com.enterprise.erp.order.SalesOrderLineRepository;
import com.enterprise.erp.order.SalesOrderRepository;
import com.enterprise.erp.order.StockReservationRepository;
import com.enterprise.erp.returns.ErpReturnLineRepository;
import com.enterprise.erp.returns.ErpReturnRepository;
import com.enterprise.erp.returns.ErpReturnService;
import tools.jackson.databind.json.JsonMapper;
import jakarta.persistence.EntityManagerFactory;
import java.util.Properties;
import org.flywaydb.core.Flyway;
import org.springframework.data.jpa.repository.support.JpaRepositoryFactory;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.jdbc.datasource.DriverManagerDataSource;
import org.springframework.orm.jpa.JpaTransactionManager;
import org.springframework.orm.jpa.LocalContainerEntityManagerFactoryBean;
import org.springframework.orm.jpa.SharedEntityManagerCreator;
import org.springframework.orm.jpa.vendor.HibernateJpaVendorAdapter;
import org.springframework.transaction.support.TransactionTemplate;

/**
 * Wires the ERP's JPA repositories and services by hand for the PostgreSQL integration tests (ERP_TEST_JDBC), without
 * starting Spring Boot. One JpaTransactionManager: the services' repository calls and the tests' own {@link #jdbc()}
 * fixture/assert statements share its transaction when inside one, so a {@link #tx()} call has the same all-or-nothing
 * behaviour as @Transactional in the running app. The repositories are not proxied here, so every call that writes
 * (including the outbox relay's claim/settle methods) must run inside {@link #tx()}.
 */
public final class ErpTestSupport {
    private final JdbcClient jdbc;
    private final TransactionTemplate tx;
    private final ErpCustomerRepository customers;
    private final ErpInventoryRepository inventory;
    private final ErpProductRepository products;
    private final SalesOrderRepository orders;
    private final SalesOrderLineRepository lines;
    private final StockReservationRepository reservations;
    private final ErpShipmentRepository shipments;
    private final ErpInvoiceRepository invoices;
    private final ErpPaymentRepository payments;
    private final ErpOutboxEventRepository events;
    private final ErpReturnRepository returns;
    private final ErpReturnLineRepository returnLines;

    private ErpTestSupport(String url) {
        var ds = new DriverManagerDataSource(url);
        Flyway.configure().dataSource(ds).load().migrate();   // Flyway owns the schema; Hibernate only validates it
        var emfBean = new LocalContainerEntityManagerFactoryBean();
        emfBean.setDataSource(ds);
        emfBean.setPackagesToScan("com.enterprise.erp");
        emfBean.setJpaVendorAdapter(new HibernateJpaVendorAdapter());
        var props = new Properties();
        props.put("hibernate.hbm2ddl.auto", "validate");
        props.put("hibernate.jdbc.time_zone", "UTC");
        emfBean.setJpaProperties(props);
        emfBean.afterPropertiesSet();
        EntityManagerFactory emf = emfBean.getObject();

        var shared = SharedEntityManagerCreator.createSharedEntityManager(emf);
        var factory = new JpaRepositoryFactory(shared);
        customers = factory.getRepository(ErpCustomerRepository.class);
        inventory = factory.getRepository(ErpInventoryRepository.class);
        products = factory.getRepository(ErpProductRepository.class);
        orders = factory.getRepository(SalesOrderRepository.class);
        lines = factory.getRepository(SalesOrderLineRepository.class);
        reservations = factory.getRepository(StockReservationRepository.class);
        shipments = factory.getRepository(ErpShipmentRepository.class);
        invoices = factory.getRepository(ErpInvoiceRepository.class);
        payments = factory.getRepository(ErpPaymentRepository.class);
        events = factory.getRepository(ErpOutboxEventRepository.class);
        returns = factory.getRepository(ErpReturnRepository.class);
        returnLines = factory.getRepository(ErpReturnLineRepository.class);
        jdbc = JdbcClient.create(ds);
        tx = new TransactionTemplate(new JpaTransactionManager(emf));
    }

    public static ErpTestSupport create() { return new ErpTestSupport(System.getenv("ERP_TEST_JDBC")); }

    public JdbcClient jdbc() { return jdbc; }
    public TransactionTemplate tx() { return tx; }
    public ErpCustomerService customerService() { return new ErpCustomerService(customers); }
    public ErpInventoryService inventoryService() { return new ErpInventoryService(inventory, products); }
    public ErpOrderService orderService() { return new ErpOrderService(orders, lines, reservations, inventory, products, customers); }
    public ErpTemporalEventOutbox outbox() { return new ErpTemporalEventOutbox(events, orders, JsonMapper.builder().findAndAddModules().build()); }
    public ErpFulfilmentService fulfilmentService() {
        return new ErpFulfilmentService(shipments, invoices, payments, orders, reservations, inventory, outbox());
    }
    public ErpReturnService returnService() { return new ErpReturnService(returns, returnLines, orders, lines, inventory); }
}
