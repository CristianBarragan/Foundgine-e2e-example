package com.enterprise.erp.customer;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;

/** ERP customer. Rows are created by ErpCustomerRepository.insertIfAbsent (the ERP mints the id from erp_customer_seq). */
@Entity
@Table(name = "erp_customers")
public class ErpCustomer {
    @Id @Column(name = "erp_customer_id") private String erpCustomerId;
    @Column(name = "tenant_id") private String tenantId;
    @Column(name = "crm_customer_id") private int crmCustomerId;
    @Column(name = "kind") private String kind;
    @Column(name = "name") private String name;
    @Column(name = "email") private String email;
    @Column(name = "phone") private String phone;
    @Column(name = "status") private String status;

    protected ErpCustomer() {}

    public String getErpCustomerId() { return erpCustomerId; }
    public String getTenantId() { return tenantId; }
    public int getCrmCustomerId() { return crmCustomerId; }
    public String getStatus() { return status; }
}
