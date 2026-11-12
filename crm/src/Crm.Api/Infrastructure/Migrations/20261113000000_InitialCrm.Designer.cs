using System;
using Foundgine.Providers.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Crm.Api.Infrastructure.Migrations;

[DbContext(typeof(FoundgineDbContext))]
[Migration("20261113000000_InitialCrm")]
partial class InitialCrm
{
    /// <inheritdoc />
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "10.0.0")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("Crm.Api.Domain.Customer", b =>
            {
                b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("integer").HasColumnName("customer_id");
                NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));
                b.Property<string>("TenantId").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("tenant_id");
                b.Property<string>("Kind").IsRequired().HasMaxLength(16).HasColumnType("character varying(16)").HasColumnName("kind");
                b.Property<string>("Name").IsRequired().HasMaxLength(200).HasColumnType("character varying(200)").HasColumnName("name");
                b.Property<string>("Email").IsRequired().HasMaxLength(320).HasColumnType("character varying(320)").HasColumnName("email");
                b.Property<string>("Phone").IsRequired().HasMaxLength(50).HasColumnType("character varying(50)").HasColumnName("phone");
                b.Property<string>("Status").IsRequired().HasMaxLength(16).HasColumnType("character varying(16)").HasColumnName("status");
                b.Property<DateTime>("CreatedAt").HasColumnType("timestamp with time zone").HasColumnName("created_at");
                b.Property<string>("CreatedBy").IsRequired().HasMaxLength(128).HasColumnType("character varying(128)").HasColumnName("created_by");
                b.Property<DateTime>("UpdatedAt").HasColumnType("timestamp with time zone").HasColumnName("updated_at");
                b.Property<string>("UpdatedBy").IsRequired().HasMaxLength(128).HasColumnType("character varying(128)").HasColumnName("updated_by");
                b.HasKey("Id").HasName("customers_pkey");
                b.HasIndex(new[] { "TenantId", "Name" }, "ix_customers_tenant_name");
                b.ToTable("customers", t =>
                {
                    t.HasCheckConstraint("customers_kind_check", "kind IN ('Business', 'Individual')");
                    t.HasCheckConstraint("customers_status_check", "status IN ('Active', 'Inactive')");
                });
            });

            modelBuilder.Entity("Crm.Api.Domain.Contact", b =>
            {
                b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("integer").HasColumnName("contact_id");
                NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));
                b.Property<string>("TenantId").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("tenant_id");
                b.Property<int>("CustomerId").HasColumnType("integer").HasColumnName("customer_id");
                b.Property<string>("FirstName").IsRequired().HasMaxLength(100).HasColumnType("character varying(100)").HasColumnName("first_name");
                b.Property<string>("LastName").IsRequired().HasMaxLength(100).HasColumnType("character varying(100)").HasColumnName("last_name");
                b.Property<string>("Email").IsRequired().HasMaxLength(320).HasColumnType("character varying(320)").HasColumnName("email");
                b.Property<string>("Phone").IsRequired().HasMaxLength(50).HasColumnType("character varying(50)").HasColumnName("phone");
                b.Property<bool>("IsPrimary").HasColumnType("boolean").HasColumnName("is_primary");
                b.Property<DateTime>("CreatedAt").HasColumnType("timestamp with time zone").HasColumnName("created_at");
                b.Property<string>("CreatedBy").IsRequired().HasMaxLength(128).HasColumnType("character varying(128)").HasColumnName("created_by");
                b.Property<DateTime>("UpdatedAt").HasColumnType("timestamp with time zone").HasColumnName("updated_at");
                b.Property<string>("UpdatedBy").IsRequired().HasMaxLength(128).HasColumnType("character varying(128)").HasColumnName("updated_by");
                b.HasKey("Id").HasName("contacts_pkey");
                b.HasIndex(new[] { "TenantId", "CustomerId" }, "ix_contacts_tenant_customer");
                b.ToTable("contacts");
            });

            modelBuilder.Entity("Crm.Api.Domain.CustomerIntegrationMapping", b =>
            {
                b.Property<int>("CrmCustomerId").ValueGeneratedNever().HasColumnType("integer").HasColumnName("crm_customer_id");
                b.Property<string>("TenantId").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("tenant_id");
                b.Property<string>("ErpCustomerId").HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("erp_customer_id");
                b.Property<string>("Status").IsRequired().HasMaxLength(16).HasColumnType("character varying(16)").HasColumnName("status");
                b.Property<int>("Attempt").HasColumnType("integer").HasColumnName("attempt");
                b.Property<string>("LastError").HasColumnType("text").HasColumnName("last_error");
                b.Property<DateTime>("CreatedAt").HasColumnType("timestamp with time zone").HasColumnName("created_at");
                b.Property<DateTime>("UpdatedAt").HasColumnType("timestamp with time zone").HasColumnName("updated_at");
                b.HasKey("CrmCustomerId").HasName("customer_integration_mappings_pkey");
                b.HasIndex(new[] { "TenantId", "ErpCustomerId" }, "ux_mappings_tenant_erp").IsUnique().HasFilter("erp_customer_id IS NOT NULL");
                b.ToTable("customer_integration_mappings", t =>
                {
                    t.HasCheckConstraint("customer_integration_mappings_status_check", "status IN ('Pending', 'Active', 'Failed')");
                });
            });

            modelBuilder.Entity("Crm.Api.Domain.Opportunity", b =>
            {
                b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("integer").HasColumnName("opportunity_id");
                NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));
                b.Property<string>("TenantId").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("tenant_id");
                b.Property<int>("CustomerId").HasColumnType("integer").HasColumnName("customer_id");
                b.Property<string>("Name").IsRequired().HasMaxLength(200).HasColumnType("character varying(200)").HasColumnName("name");
                b.Property<string>("Stage").IsRequired().HasMaxLength(20).HasColumnType("character varying(20)").HasColumnName("stage");
                b.Property<decimal>("Amount").HasPrecision(14, 2).HasColumnType("numeric(14,2)").HasColumnName("amount");
                b.Property<DateTime>("CreatedAt").HasColumnType("timestamp with time zone").HasColumnName("created_at");
                b.Property<string>("CreatedBy").IsRequired().HasMaxLength(128).HasColumnType("character varying(128)").HasColumnName("created_by");
                b.Property<DateTime>("UpdatedAt").HasColumnType("timestamp with time zone").HasColumnName("updated_at");
                b.Property<string>("UpdatedBy").IsRequired().HasMaxLength(128).HasColumnType("character varying(128)").HasColumnName("updated_by");
                b.HasKey("Id").HasName("opportunities_pkey");
                b.HasIndex(new[] { "TenantId", "CustomerId" }, "ix_opportunities_tenant_customer");
                b.ToTable("opportunities", t =>
                {
                    t.HasCheckConstraint("opportunities_stage_check", "stage IN ('Prospecting','Qualification','Proposal','Negotiation','ClosedWon','ClosedLost')");
                });
            });

            modelBuilder.Entity("Crm.Api.Domain.Quote", b =>
            {
                b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("integer").HasColumnName("quote_id");
                NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));
                b.Property<string>("TenantId").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("tenant_id");
                b.Property<int>("OpportunityId").HasColumnType("integer").HasColumnName("opportunity_id");
                b.Property<int>("CustomerId").HasColumnType("integer").HasColumnName("customer_id");
                b.Property<string>("Status").IsRequired().HasMaxLength(20).HasColumnType("character varying(20)").HasColumnName("status");
                b.Property<decimal>("Total").HasPrecision(14, 2).HasColumnType("numeric(14,2)").HasColumnName("total");
                b.Property<string>("SubmittedBy").IsRequired().HasMaxLength(128).HasColumnType("character varying(128)").HasColumnName("submitted_by");
                b.Property<string>("DecidedBy").IsRequired().HasMaxLength(128).HasColumnType("character varying(128)").HasColumnName("decided_by");
                b.Property<string>("ErpOrderId").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("erp_order_id");
                b.Property<string>("OrderFailureReason").IsRequired().HasMaxLength(500).HasColumnType("character varying(500)").HasColumnName("order_failure_reason");
                b.Property<DateTime>("CreatedAt").HasColumnType("timestamp with time zone").HasColumnName("created_at");
                b.Property<string>("CreatedBy").IsRequired().HasMaxLength(128).HasColumnType("character varying(128)").HasColumnName("created_by");
                b.Property<DateTime>("UpdatedAt").HasColumnType("timestamp with time zone").HasColumnName("updated_at");
                b.Property<string>("UpdatedBy").IsRequired().HasMaxLength(128).HasColumnType("character varying(128)").HasColumnName("updated_by");
                b.HasKey("Id").HasName("quotes_pkey");
                b.HasIndex(new[] { "TenantId", "CustomerId" }, "ix_quotes_tenant_customer");
                b.HasIndex(new[] { "TenantId", "ErpOrderId" }, "ux_quotes_tenant_erp_order").IsUnique().HasFilter("erp_order_id <> ''");
                b.ToTable("quotes", t =>
                {
                    t.HasCheckConstraint("quotes_status_check", "status IN ('Draft','PendingApproval','Approved','Rejected','Accepted','Ordered','OrderFailed')");
                });
            });

            modelBuilder.Entity("Crm.Api.Domain.QuoteLine", b =>
            {
                b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("integer").HasColumnName("quote_line_id");
                NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));
                b.Property<string>("TenantId").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("tenant_id");
                b.Property<int>("QuoteId").HasColumnType("integer").HasColumnName("quote_id");
                b.Property<string>("Sku").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("sku");
                b.Property<string>("Description").IsRequired().HasMaxLength(300).HasColumnType("character varying(300)").HasColumnName("description");
                b.Property<int>("Quantity").HasColumnType("integer").HasColumnName("quantity");
                b.Property<decimal>("UnitPrice").HasPrecision(14, 2).HasColumnType("numeric(14,2)").HasColumnName("unit_price");
                b.HasKey("Id").HasName("quote_lines_pkey");
                b.HasIndex(new[] { "TenantId", "QuoteId" }, "ix_quote_lines_quote");
                b.ToTable("quote_lines", t =>
                {
                    t.HasCheckConstraint("quote_lines_quantity_check", "quantity > 0");
                    t.HasCheckConstraint("quote_lines_unit_price_check", "unit_price >= 0");
                });
            });

            modelBuilder.Entity("Crm.Api.Domain.ReturnRequest", b =>
            {
                b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("integer").HasColumnName("return_id");
                NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));
                b.Property<string>("TenantId").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("tenant_id");
                b.Property<int>("QuoteId").HasColumnType("integer").HasColumnName("quote_id");
                b.Property<int>("CustomerId").HasColumnType("integer").HasColumnName("customer_id");
                b.Property<string>("Status").IsRequired().HasMaxLength(20).HasColumnType("character varying(20)").HasColumnName("status");
                b.Property<string>("Reason").IsRequired().HasMaxLength(500).HasColumnType("character varying(500)").HasColumnName("reason");
                b.Property<string>("ErpReturnId").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("erp_return_id");
                b.Property<string>("DecisionNote").IsRequired().HasMaxLength(500).HasColumnType("character varying(500)").HasColumnName("decision_note");
                b.Property<string>("RequestedBy").IsRequired().HasMaxLength(128).HasColumnType("character varying(128)").HasColumnName("requested_by");
                b.Property<DateTime>("CreatedAt").HasColumnType("timestamp with time zone").HasColumnName("created_at");
                b.Property<DateTime>("UpdatedAt").HasColumnType("timestamp with time zone").HasColumnName("updated_at");
                b.HasKey("Id").HasName("return_requests_pkey");
                b.HasIndex(new[] { "TenantId", "QuoteId" }, "ix_returns_quote");
                b.HasIndex(new[] { "TenantId", "ErpReturnId" }, "ux_returns_tenant_erp").IsUnique().HasFilter("erp_return_id <> ''");
                b.ToTable("return_requests", t =>
                {
                    t.HasCheckConstraint("return_requests_status_check", "status IN ('Requested','Authorized','Rejected','Completed')");
                });
            });

            modelBuilder.Entity("Crm.Api.Domain.ReturnRequestLine", b =>
            {
                b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("integer").HasColumnName("return_line_id");
                NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));
                b.Property<string>("TenantId").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("tenant_id");
                b.Property<int>("ReturnId").HasColumnType("integer").HasColumnName("return_id");
                b.Property<string>("Sku").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("sku");
                b.Property<int>("Quantity").HasColumnType("integer").HasColumnName("quantity");
                b.HasKey("Id").HasName("return_request_lines_pkey");
                b.HasIndex(new[] { "TenantId", "ReturnId" }, "ix_return_lines_return");
                b.ToTable("return_request_lines", t =>
                {
                    t.HasCheckConstraint("return_request_lines_quantity_check", "quantity > 0");
                });
            });

            modelBuilder.Entity("Crm.Api.Domain.Shipment", b =>
            {
                b.Property<string>("TenantId").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("tenant_id");
                b.Property<string>("ErpShipmentId").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("erp_shipment_id");
                b.Property<int>("QuoteId").HasColumnType("integer").HasColumnName("quote_id");
                b.Property<string>("ErpOrderId").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("erp_order_id");
                b.Property<string>("Status").IsRequired().HasMaxLength(20).HasColumnType("character varying(20)").HasColumnName("status");
                b.Property<string>("Carrier").IsRequired().HasMaxLength(100).HasColumnType("character varying(100)").HasColumnName("carrier");
                b.Property<string>("TrackingNumber").IsRequired().HasMaxLength(100).HasColumnType("character varying(100)").HasColumnName("tracking_number");
                b.Property<int>("Version").HasColumnType("integer").HasColumnName("version");
                b.Property<DateTime>("UpdatedAt").HasColumnType("timestamp with time zone").HasColumnName("updated_at");
                b.HasKey("TenantId", "ErpShipmentId").HasName("shipments_pkey");
                b.HasIndex(new[] { "TenantId", "QuoteId" }, "ix_shipments_quote");
                b.ToTable("shipments", t =>
                {
                    t.HasCheckConstraint("shipments_status_check", "status IN ('Packed','Shipped','InTransit','Delivered','Cancelled')");
                    t.HasCheckConstraint("shipments_version_check", "version > 0");
                });
            });

            modelBuilder.Entity("Crm.Api.Domain.Invoice", b =>
            {
                b.Property<string>("TenantId").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("tenant_id");
                b.Property<string>("ErpInvoiceId").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("erp_invoice_id");
                b.Property<int>("QuoteId").HasColumnType("integer").HasColumnName("quote_id");
                b.Property<int>("CustomerId").HasColumnType("integer").HasColumnName("customer_id");
                b.Property<string>("ErpOrderId").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("erp_order_id");
                b.Property<string>("Number").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("number");
                b.Property<decimal>("Total").HasPrecision(14, 2).HasColumnType("numeric(14,2)").HasColumnName("total");
                b.Property<string>("Status").IsRequired().HasMaxLength(20).HasColumnType("character varying(20)").HasColumnName("status");
                b.Property<DateTime>("IssuedAt").HasColumnType("timestamp with time zone").HasColumnName("issued_at");
                b.Property<DateTime?>("DueAt").HasColumnType("timestamp with time zone").HasColumnName("due_at");
                b.Property<int>("Version").HasColumnType("integer").HasColumnName("version");
                b.Property<DateTime>("UpdatedAt").HasColumnType("timestamp with time zone").HasColumnName("updated_at");
                b.HasKey("TenantId", "ErpInvoiceId").HasName("invoices_pkey");
                b.HasIndex(new[] { "TenantId", "QuoteId" }, "ix_invoices_quote");
                b.HasIndex(new[] { "TenantId", "CustomerId" }, "ix_invoices_customer");
                b.ToTable("invoices", t =>
                {
                    t.HasCheckConstraint("invoices_total_check", "total >= 0");
                    t.HasCheckConstraint("invoices_status_check", "status IN ('Issued','PartiallyPaid','Paid','Overdue','Void')");
                    t.HasCheckConstraint("invoices_version_check", "version > 0");
                });
            });

            modelBuilder.Entity("Crm.Api.Domain.Payment", b =>
            {
                b.Property<string>("TenantId").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("tenant_id");
                b.Property<string>("ErpPaymentId").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("erp_payment_id");
                b.Property<string>("ErpInvoiceId").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("erp_invoice_id");
                b.Property<int>("QuoteId").HasColumnType("integer").HasColumnName("quote_id");
                b.Property<decimal>("Amount").HasPrecision(14, 2).HasColumnType("numeric(14,2)").HasColumnName("amount");
                b.Property<string>("Status").IsRequired().HasMaxLength(20).HasColumnType("character varying(20)").HasColumnName("status");
                b.Property<int>("Version").HasColumnType("integer").HasColumnName("version");
                b.Property<DateTime>("UpdatedAt").HasColumnType("timestamp with time zone").HasColumnName("updated_at");
                b.HasKey("TenantId", "ErpPaymentId").HasName("payments_pkey");
                b.HasIndex(new[] { "TenantId", "QuoteId" }, "ix_payments_quote");
                b.ToTable("payments", t =>
                {
                    t.HasCheckConstraint("payments_amount_check", "amount >= 0");
                    t.HasCheckConstraint("payments_status_check", "status IN ('Received','Reconciled','Reversed')");
                    t.HasCheckConstraint("payments_version_check", "version > 0");
                });
            });

            modelBuilder.Entity("Crm.Api.Domain.OutboxMessage", b =>
            {
                b.Property<Guid>("Id").ValueGeneratedNever().HasColumnType("uuid").HasColumnName("id");
                b.Property<string>("TenantId").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("tenant_id");
                b.Property<string>("Type").IsRequired().HasMaxLength(100).HasColumnType("character varying(100)").HasColumnName("type");
                b.Property<int>("AggregateId").HasColumnType("integer").HasColumnName("aggregate_id");
                b.Property<int>("Attempt").HasColumnType("integer").HasColumnName("attempt");
                b.Property<string>("Payload").IsRequired().HasColumnType("jsonb").HasColumnName("payload");
                b.Property<DateTime>("CreatedAt").HasColumnType("timestamp with time zone").HasColumnName("created_at");
                b.Property<DateTime?>("PublishedAt").HasColumnType("timestamp with time zone").HasColumnName("published_at");
                b.Property<int>("PublishAttempts").HasColumnType("integer").HasColumnName("publish_attempts");
                b.Property<DateTime>("NextAttemptAt").HasColumnType("timestamp with time zone").HasColumnName("next_attempt_at").HasDefaultValueSql("now()");
                b.Property<string>("LastError").HasColumnType("text").HasColumnName("last_error");
                b.HasKey("Id").HasName("outbox_messages_pkey");
                b.HasIndex(new[] { "NextAttemptAt" }, "ix_outbox_unpublished").HasFilter("published_at IS NULL");
                b.ToTable("outbox_messages");
            });

            modelBuilder.Entity("Crm.Api.Domain.AdminAuditEntry", b =>
            {
                b.Property<long>("Id").ValueGeneratedOnAdd().HasColumnType("bigint").HasColumnName("audit_id");
                NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<long>("Id"));
                b.Property<string>("TenantId").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("tenant_id");
                b.Property<string>("UserId").IsRequired().HasMaxLength(128).HasColumnType("character varying(128)").HasColumnName("user_id");
                b.Property<string>("Action").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("action");
                b.Property<string>("Target").IsRequired().HasMaxLength(128).HasColumnType("character varying(128)").HasColumnName("target");
                b.Property<string>("Outcome").IsRequired().HasMaxLength(16).HasColumnType("character varying(16)").HasColumnName("outcome");
                b.Property<string>("Detail").IsRequired().HasMaxLength(300).HasColumnType("character varying(300)").HasColumnName("detail");
                b.Property<string>("CorrelationId").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("correlation_id");
                b.Property<DateTime>("OccurredAt").HasColumnType("timestamp with time zone").HasColumnName("occurred_at");
                b.HasKey("Id").HasName("admin_audit_pkey");
                b.HasIndex(new[] { "TenantId", "OccurredAt" }, "ix_admin_audit_tenant_time").IsDescending(false, true);
                b.ToTable("admin_audit", t =>
                {
                    t.HasCheckConstraint("admin_audit_outcome_check", "outcome IN ('Succeeded','Rejected','Failed')");
                });
            });

            modelBuilder.Entity("Crm.Api.Domain.Contact", b =>
            {
                b.HasOne("Crm.Api.Domain.Customer", null).WithMany().HasForeignKey("CustomerId").HasConstraintName("contacts_customer_id_fkey").OnDelete(DeleteBehavior.NoAction).IsRequired();
            });

            modelBuilder.Entity("Crm.Api.Domain.CustomerIntegrationMapping", b =>
            {
                b.HasOne("Crm.Api.Domain.Customer", null).WithOne().HasForeignKey("CrmCustomerId").HasConstraintName("customer_integration_mappings_crm_customer_id_fkey").OnDelete(DeleteBehavior.NoAction).IsRequired();
            });

            modelBuilder.Entity("Crm.Api.Domain.Opportunity", b =>
            {
                b.HasOne("Crm.Api.Domain.Customer", null).WithMany().HasForeignKey("CustomerId").HasConstraintName("opportunities_customer_id_fkey").OnDelete(DeleteBehavior.NoAction).IsRequired();
            });

            modelBuilder.Entity("Crm.Api.Domain.Quote", b =>
            {
                b.HasOne("Crm.Api.Domain.Opportunity", null).WithMany().HasForeignKey("OpportunityId").HasConstraintName("quotes_opportunity_id_fkey").OnDelete(DeleteBehavior.NoAction).IsRequired();
            });

            modelBuilder.Entity("Crm.Api.Domain.Quote", b =>
            {
                b.HasOne("Crm.Api.Domain.Customer", null).WithMany().HasForeignKey("CustomerId").HasConstraintName("quotes_customer_id_fkey").OnDelete(DeleteBehavior.NoAction).IsRequired();
            });

            modelBuilder.Entity("Crm.Api.Domain.QuoteLine", b =>
            {
                b.HasOne("Crm.Api.Domain.Quote", null).WithMany().HasForeignKey("QuoteId").HasConstraintName("quote_lines_quote_id_fkey").OnDelete(DeleteBehavior.NoAction).IsRequired();
            });

            modelBuilder.Entity("Crm.Api.Domain.ReturnRequest", b =>
            {
                b.HasOne("Crm.Api.Domain.Quote", null).WithMany().HasForeignKey("QuoteId").HasConstraintName("return_requests_quote_id_fkey").OnDelete(DeleteBehavior.NoAction).IsRequired();
            });

            modelBuilder.Entity("Crm.Api.Domain.ReturnRequest", b =>
            {
                b.HasOne("Crm.Api.Domain.Customer", null).WithMany().HasForeignKey("CustomerId").HasConstraintName("return_requests_customer_id_fkey").OnDelete(DeleteBehavior.NoAction).IsRequired();
            });

            modelBuilder.Entity("Crm.Api.Domain.ReturnRequestLine", b =>
            {
                b.HasOne("Crm.Api.Domain.ReturnRequest", null).WithMany().HasForeignKey("ReturnId").HasConstraintName("return_request_lines_return_id_fkey").OnDelete(DeleteBehavior.NoAction).IsRequired();
            });

            modelBuilder.Entity("Crm.Api.Domain.Shipment", b =>
            {
                b.HasOne("Crm.Api.Domain.Quote", null).WithMany().HasForeignKey("QuoteId").HasConstraintName("shipments_quote_id_fkey").OnDelete(DeleteBehavior.NoAction).IsRequired();
            });

            modelBuilder.Entity("Crm.Api.Domain.Invoice", b =>
            {
                b.HasOne("Crm.Api.Domain.Quote", null).WithMany().HasForeignKey("QuoteId").HasConstraintName("invoices_quote_id_fkey").OnDelete(DeleteBehavior.NoAction).IsRequired();
            });

            modelBuilder.Entity("Crm.Api.Domain.Invoice", b =>
            {
                b.HasOne("Crm.Api.Domain.Customer", null).WithMany().HasForeignKey("CustomerId").HasConstraintName("invoices_customer_id_fkey").OnDelete(DeleteBehavior.NoAction).IsRequired();
            });

            modelBuilder.Entity("Crm.Api.Domain.Payment", b =>
            {
                b.HasOne("Crm.Api.Domain.Quote", null).WithMany().HasForeignKey("QuoteId").HasConstraintName("payments_quote_id_fkey").OnDelete(DeleteBehavior.NoAction).IsRequired();
            });

#pragma warning restore 612, 618
    }
}
