using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Crm.Api.Domain;

/// <summary>
/// EF Core storage details that Foundgine's persistence metadata does not carry: lengths, precision, check
/// constraints, indexes, foreign keys, plus the EF-only entities (composite-key ERP mirrors, outbox, audit).
/// Applied by the FoundgineDbContext after the metadata-derived model, so table/key/column names stay in
/// CrmModel.cs for Foundgine entities. Constraint and index names match the original SQL schema, so a database
/// created by the legacy scripts is baselined (not recreated) by the migrator.
/// </summary>
public static class CrmStorage
{
    public static void Apply(ModelBuilder mb)
    {
        mb.Entity<Customer>(e =>
        {
            e.ToTable("customers", t =>
            {
                t.HasCheckConstraint("customers_kind_check", "kind IN ('Business', 'Individual')");
                t.HasCheckConstraint("customers_status_check", "status IN ('Active', 'Inactive')");
            });
            e.HasKey(x => x.Id).HasName("customers_pkey");
            Tenant(e); Audit(e);
            e.Property(x => x.Kind).HasMaxLength(16);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Email).HasMaxLength(320);
            e.Property(x => x.Phone).HasMaxLength(50);
            e.Property(x => x.Status).HasMaxLength(16);
            e.HasIndex(x => new { x.TenantId, x.Name }).HasDatabaseName("ix_customers_tenant_name");
            // ux_customers_tenant_email is a functional index on lower(email); EF cannot model it, so the migration
            // creates it with raw SQL (see 20261113000000_InitialCrm).
        });

        mb.Entity<Contact>(e =>
        {
            e.ToTable("contacts");
            e.HasKey(x => x.Id).HasName("contacts_pkey");
            Tenant(e); Audit(e);
            e.Property(x => x.FirstName).HasMaxLength(100);
            e.Property(x => x.LastName).HasMaxLength(100);
            e.Property(x => x.Email).HasMaxLength(320);
            e.Property(x => x.Phone).HasMaxLength(50);
            e.HasIndex(x => new { x.TenantId, x.CustomerId }).HasDatabaseName("ix_contacts_tenant_customer");
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId)
                .HasConstraintName("contacts_customer_id_fkey").OnDelete(DeleteBehavior.NoAction);
        });

        mb.Entity<CustomerIntegrationMapping>(e =>
        {
            e.ToTable("customer_integration_mappings", t =>
                t.HasCheckConstraint("customer_integration_mappings_status_check", "status IN ('Pending', 'Active', 'Failed')"));
            e.HasKey(x => x.CrmCustomerId).HasName("customer_integration_mappings_pkey");
            e.Property(x => x.CrmCustomerId).ValueGeneratedNever();
            Tenant(e);
            e.Property(x => x.ErpCustomerId).HasMaxLength(64);
            e.Property(x => x.Status).HasMaxLength(16);
            e.HasIndex(x => new { x.TenantId, x.ErpCustomerId }).IsUnique()
                .HasFilter("erp_customer_id IS NOT NULL").HasDatabaseName("ux_mappings_tenant_erp");
            e.HasOne<Customer>().WithOne().HasForeignKey<CustomerIntegrationMapping>(x => x.CrmCustomerId)
                .HasConstraintName("customer_integration_mappings_crm_customer_id_fkey").OnDelete(DeleteBehavior.NoAction);
        });

        mb.Entity<Opportunity>(e =>
        {
            e.ToTable("opportunities", t => t.HasCheckConstraint("opportunities_stage_check",
                "stage IN ('Prospecting','Qualification','Proposal','Negotiation','ClosedWon','ClosedLost')"));
            e.HasKey(x => x.Id).HasName("opportunities_pkey");
            Tenant(e); Audit(e);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Stage).HasMaxLength(20);
            e.Property(x => x.Amount).HasPrecision(14, 2);
            e.HasIndex(x => new { x.TenantId, x.CustomerId }).HasDatabaseName("ix_opportunities_tenant_customer");
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId)
                .HasConstraintName("opportunities_customer_id_fkey").OnDelete(DeleteBehavior.NoAction);
        });

        mb.Entity<Quote>(e =>
        {
            e.ToTable("quotes", t => t.HasCheckConstraint("quotes_status_check",
                "status IN ('Draft','PendingApproval','Approved','Rejected','Accepted','Ordered','OrderFailed')"));
            e.HasKey(x => x.Id).HasName("quotes_pkey");
            Tenant(e); Audit(e);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.Total).HasPrecision(14, 2);
            e.Property(x => x.SubmittedBy).HasMaxLength(128);
            e.Property(x => x.DecidedBy).HasMaxLength(128);
            e.Property(x => x.ErpOrderId).HasMaxLength(64);
            e.Property(x => x.OrderFailureReason).HasMaxLength(500);
            e.HasIndex(x => new { x.TenantId, x.CustomerId }).HasDatabaseName("ix_quotes_tenant_customer");
            e.HasIndex(x => new { x.TenantId, x.ErpOrderId }).IsUnique()
                .HasFilter("erp_order_id <> ''").HasDatabaseName("ux_quotes_tenant_erp_order");
            e.HasOne<Opportunity>().WithMany().HasForeignKey(x => x.OpportunityId)
                .HasConstraintName("quotes_opportunity_id_fkey").OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId)
                .HasConstraintName("quotes_customer_id_fkey").OnDelete(DeleteBehavior.NoAction);
        });

        mb.Entity<QuoteLine>(e =>
        {
            e.ToTable("quote_lines", t =>
            {
                t.HasCheckConstraint("quote_lines_quantity_check", "quantity > 0");
                t.HasCheckConstraint("quote_lines_unit_price_check", "unit_price >= 0");
            });
            e.HasKey(x => x.Id).HasName("quote_lines_pkey");
            Tenant(e);
            e.Property(x => x.Sku).HasMaxLength(64);
            e.Property(x => x.Description).HasMaxLength(300);
            e.Property(x => x.UnitPrice).HasPrecision(14, 2);
            e.HasIndex(x => new { x.TenantId, x.QuoteId }).HasDatabaseName("ix_quote_lines_quote");
            e.HasOne<Quote>().WithMany().HasForeignKey(x => x.QuoteId)
                .HasConstraintName("quote_lines_quote_id_fkey").OnDelete(DeleteBehavior.NoAction);
        });

        mb.Entity<ReturnRequest>(e =>
        {
            e.ToTable("return_requests", t => t.HasCheckConstraint("return_requests_status_check",
                "status IN ('Requested','Authorized','Rejected','Completed')"));
            e.HasKey(x => x.Id).HasName("return_requests_pkey");
            Tenant(e);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.ErpReturnId).HasMaxLength(64);
            e.Property(x => x.DecisionNote).HasMaxLength(500);
            e.Property(x => x.RequestedBy).HasMaxLength(128);
            e.HasIndex(x => new { x.TenantId, x.QuoteId }).HasDatabaseName("ix_returns_quote");
            e.HasIndex(x => new { x.TenantId, x.ErpReturnId }).IsUnique()
                .HasFilter("erp_return_id <> ''").HasDatabaseName("ux_returns_tenant_erp");
            e.HasOne<Quote>().WithMany().HasForeignKey(x => x.QuoteId)
                .HasConstraintName("return_requests_quote_id_fkey").OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId)
                .HasConstraintName("return_requests_customer_id_fkey").OnDelete(DeleteBehavior.NoAction);
        });

        mb.Entity<ReturnRequestLine>(e =>
        {
            e.ToTable("return_request_lines", t => t.HasCheckConstraint("return_request_lines_quantity_check", "quantity > 0"));
            e.HasKey(x => x.Id).HasName("return_request_lines_pkey");
            Tenant(e);
            e.Property(x => x.Sku).HasMaxLength(64);
            e.HasIndex(x => new { x.TenantId, x.ReturnId }).HasDatabaseName("ix_return_lines_return");
            e.HasOne<ReturnRequest>().WithMany().HasForeignKey(x => x.ReturnId)
                .HasConstraintName("return_request_lines_return_id_fkey").OnDelete(DeleteBehavior.NoAction);
        });

        // ---- EF-only entities (not part of the Foundgine semantic model) -----------------------------------------

        mb.Entity<Shipment>(e =>
        {
            e.ToTable("shipments", t =>
            {
                t.HasCheckConstraint("shipments_status_check", "status IN ('Packed','Shipped','InTransit','Delivered','Cancelled')");
                t.HasCheckConstraint("shipments_version_check", "version > 0");
            });
            e.HasKey(x => new { x.TenantId, x.ErpShipmentId }).HasName("shipments_pkey");
            Tenant(e); Col(e, x => x.ErpShipmentId, "erp_shipment_id", 64);
            Col(e, x => x.QuoteId, "quote_id"); Col(e, x => x.ErpOrderId, "erp_order_id", 64);
            Col(e, x => x.Status, "status", 20); Col(e, x => x.Carrier, "carrier", 100);
            Col(e, x => x.TrackingNumber, "tracking_number", 100); Col(e, x => x.Version, "version");
            Col(e, x => x.UpdatedAt, "updated_at");
            e.HasIndex(x => new { x.TenantId, x.QuoteId }).HasDatabaseName("ix_shipments_quote");
            e.HasOne<Quote>().WithMany().HasForeignKey(x => x.QuoteId)
                .HasConstraintName("shipments_quote_id_fkey").OnDelete(DeleteBehavior.NoAction);
        });

        mb.Entity<Invoice>(e =>
        {
            e.ToTable("invoices", t =>
            {
                t.HasCheckConstraint("invoices_total_check", "total >= 0");
                t.HasCheckConstraint("invoices_status_check", "status IN ('Issued','PartiallyPaid','Paid','Overdue','Void')");
                t.HasCheckConstraint("invoices_version_check", "version > 0");
            });
            e.HasKey(x => new { x.TenantId, x.ErpInvoiceId }).HasName("invoices_pkey");
            Tenant(e); Col(e, x => x.ErpInvoiceId, "erp_invoice_id", 64);
            Col(e, x => x.QuoteId, "quote_id"); Col(e, x => x.CustomerId, "customer_id");
            Col(e, x => x.ErpOrderId, "erp_order_id", 64); Col(e, x => x.Number, "number", 64);
            Col(e, x => x.Total, "total"); e.Property(x => x.Total).HasPrecision(14, 2);
            Col(e, x => x.Status, "status", 20); Col(e, x => x.IssuedAt, "issued_at");
            Col(e, x => x.DueAt, "due_at"); Col(e, x => x.Version, "version"); Col(e, x => x.UpdatedAt, "updated_at");
            e.HasIndex(x => new { x.TenantId, x.QuoteId }).HasDatabaseName("ix_invoices_quote");
            e.HasIndex(x => new { x.TenantId, x.CustomerId }).HasDatabaseName("ix_invoices_customer");
            e.HasOne<Quote>().WithMany().HasForeignKey(x => x.QuoteId)
                .HasConstraintName("invoices_quote_id_fkey").OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId)
                .HasConstraintName("invoices_customer_id_fkey").OnDelete(DeleteBehavior.NoAction);
        });

        mb.Entity<Payment>(e =>
        {
            e.ToTable("payments", t =>
            {
                t.HasCheckConstraint("payments_amount_check", "amount >= 0");
                t.HasCheckConstraint("payments_status_check", "status IN ('Received','Reconciled','Reversed')");
                t.HasCheckConstraint("payments_version_check", "version > 0");
            });
            e.HasKey(x => new { x.TenantId, x.ErpPaymentId }).HasName("payments_pkey");
            Tenant(e); Col(e, x => x.ErpPaymentId, "erp_payment_id", 64);
            Col(e, x => x.ErpInvoiceId, "erp_invoice_id", 64); Col(e, x => x.QuoteId, "quote_id");
            Col(e, x => x.Amount, "amount"); e.Property(x => x.Amount).HasPrecision(14, 2);
            Col(e, x => x.Status, "status", 20); Col(e, x => x.Version, "version"); Col(e, x => x.UpdatedAt, "updated_at");
            e.HasIndex(x => new { x.TenantId, x.QuoteId }).HasDatabaseName("ix_payments_quote");
            e.HasOne<Quote>().WithMany().HasForeignKey(x => x.QuoteId)
                .HasConstraintName("payments_quote_id_fkey").OnDelete(DeleteBehavior.NoAction);
        });

        mb.Entity<OutboxMessage>(e =>
        {
            e.ToTable("outbox_messages");
            e.HasKey(x => x.Id).HasName("outbox_messages_pkey");
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            Tenant(e); Col(e, x => x.Type, "type", 100); Col(e, x => x.AggregateId, "aggregate_id");
            Col(e, x => x.Attempt, "attempt"); Col(e, x => x.Payload, "payload"); e.Property(x => x.Payload).HasColumnType("nvarchar(max)");
            Col(e, x => x.CreatedAt, "created_at"); Col(e, x => x.PublishedAt, "published_at");
            Col(e, x => x.PublishAttempts, "publish_attempts"); Col(e, x => x.NextAttemptAt, "next_attempt_at");
            e.Property(x => x.NextAttemptAt).HasDefaultValueSql("SYSUTCDATETIME()");
            Col(e, x => x.LastError, "last_error"); Col(e, x => x.Initiator, "initiator", 128);
            e.HasIndex(x => x.NextAttemptAt).HasFilter("published_at IS NULL").HasDatabaseName("ix_outbox_unpublished");
        });

        mb.Entity<AdminAuditEntry>(e =>
        {
            e.ToTable("admin_audit", t => t.HasCheckConstraint("admin_audit_outcome_check",
                "outcome IN ('Succeeded','Rejected','Failed')"));
            e.HasKey(x => x.Id).HasName("admin_audit_pkey");
            e.Property(x => x.Id).HasColumnName("audit_id");
            Tenant(e); Col(e, x => x.UserId, "user_id", 128); Col(e, x => x.Action, "action", 64);
            Col(e, x => x.Target, "target", 128); Col(e, x => x.Outcome, "outcome", 16);
            Col(e, x => x.Detail, "detail", 300); Col(e, x => x.CorrelationId, "correlation_id", 64);
            Col(e, x => x.OccurredAt, "occurred_at");
            e.HasIndex(x => new { x.TenantId, x.OccurredAt }).IsDescending(false, true).HasDatabaseName("ix_admin_audit_tenant_time");
        });
    }

    private static void Tenant<T>(EntityTypeBuilder<T> e) where T : class, ITenantOwned
    {
        var b = e.Property(x => x.TenantId).HasMaxLength(64);
        if (e.Metadata.ClrType.IsAssignableTo(typeof(IEfOnly))) b.HasColumnName("tenant_id");
    }

    private static void Audit<T>(EntityTypeBuilder<T> e) where T : class, ITenantOwned
    {
        // CreatedBy / UpdatedBy exist on every audited entity; resolved by name so the helper stays generic.
        e.Property("CreatedBy").HasMaxLength(128);
        e.Property("UpdatedBy").HasMaxLength(128);
    }

    private static void Col<T, TValue>(EntityTypeBuilder<T> e, System.Linq.Expressions.Expression<Func<T, TValue>> p,
        string column, int? maxLength = null) where T : class
    {
        var b = e.Property(p).HasColumnName(column);
        if (maxLength is { } n) b.HasMaxLength(n);
    }
}
