using Foundgine.Core.Semantic.Configuration;
using Foundgine.Providers.EntityFrameworkCore;

namespace Crm.Api.Domain;

/// <summary>
/// Single registration point for the Foundgine side of the CRM: semantic relationships, persistence names, identity
/// entity maps and connections. EF-specific storage (lengths, constraints, indexes, foreign keys, the EF-only
/// entities) is in <see cref="CrmStorage"/>; both feed one <c>FoundgineDbContext</c>.
/// </summary>
public sealed class CrmModel : FoundgineModel
{
    public override void Configure(FoundgineModelBuilder model)
    {
        // Semantic models (relationships are declared once, on the navigation).
        model.Configure<CustomerSemantics>();
        model.Configure<OpportunitySemantics>();
        model.Configure<QuoteSemantics>();
        model.Configure<ReturnRequestSemantics>();
        model.Configure<Plain<Contact>>();
        model.Configure<Plain<QuoteLine>>();
        model.Configure<Plain<CustomerIntegrationMapping>>();
        model.Configure<Plain<ReturnRequestLine>>();

        // Persistence: table, key, column names.
        model.Configure<CustomerTable>();
        model.Configure<ContactTable>();
        model.Configure<OpportunityTable>();
        model.Configure<QuoteTable>();
        model.Configure<QuoteLineTable>();
        model.Configure<MappingTable>();
        model.Configure<ReturnRequestTable>();
        model.Configure<ReturnRequestLineTable>();

        // Domain class == persistence class.
        model.Configure<IdentityEntityMap<Customer>>();
        model.Configure<IdentityEntityMap<Contact>>();
        model.Configure<IdentityEntityMap<Opportunity>>();
        model.Configure<IdentityEntityMap<Quote>>();
        model.Configure<IdentityEntityMap<QuoteLine>>();
        model.Configure<IdentityEntityMap<CustomerIntegrationMapping>>();
        model.Configure<IdentityEntityMap<ReturnRequest>>();
        model.Configure<IdentityEntityMap<ReturnRequestLine>>();

        model.Configure<CrmConnections>();
    }
}

internal sealed class Plain<T> : FoundgineModelConfiguration<T>
{
    public override void Configure(FoundgineModelBuilder<T> model)
    {
    }
}

internal sealed class CustomerSemantics : FoundgineModelConfiguration<Customer>
{
    public override void Configure(FoundgineModelBuilder<Customer> model)
    {
        model.HasRelationship(x => x.Contacts).To<Contact>();
        model.HasRelationship(x => x.Opportunities).To<Opportunity>();
        model.HasRelationship(x => x.Quotes).To<Quote>();
        model.HasRelationship(x => x.Returns).To<ReturnRequest>();
        model.HasRelationship(x => x.IntegrationMappings).To<CustomerIntegrationMapping>();
    }
}

internal sealed class OpportunitySemantics : FoundgineModelConfiguration<Opportunity>
{
    public override void Configure(FoundgineModelBuilder<Opportunity> model) =>
        model.HasRelationship(x => x.Quotes).To<Quote>();
}

internal sealed class QuoteSemantics : FoundgineModelConfiguration<Quote>
{
    public override void Configure(FoundgineModelBuilder<Quote> model)
    {
        model.HasRelationship(x => x.Lines).To<QuoteLine>();
        model.HasRelationship(x => x.Returns).To<ReturnRequest>();
    }
}

internal sealed class ReturnRequestSemantics : FoundgineModelConfiguration<ReturnRequest>
{
    public override void Configure(FoundgineModelBuilder<ReturnRequest> model) =>
        model.HasRelationship(x => x.Lines).To<ReturnRequestLine>();
}

internal sealed class CrmConnections : FoundgineConnectionConfiguration
{
    public override void Configure(FoundgineConnectionBuilder connections)
    {
        connections.HasRelationship<Customer, Contact>()
            .Using<Customer, Contact>().HasForeignKey(c => c.CustomerId).References(c => c.Id);

        connections.HasRelationship<Customer, Opportunity>()
            .Using<Customer, Opportunity>().HasForeignKey(o => o.CustomerId).References(c => c.Id);

        connections.HasRelationship<Customer, Quote>()
            .Using<Customer, Quote>().HasForeignKey(q => q.CustomerId).References(c => c.Id);

        connections.HasRelationship<Customer, ReturnRequest>()
            .Using<Customer, ReturnRequest>().HasForeignKey(r => r.CustomerId).References(c => c.Id);

        connections.HasRelationship<Customer, CustomerIntegrationMapping>()
            .Using<Customer, CustomerIntegrationMapping>().HasForeignKey(m => m.CrmCustomerId).References(c => c.Id);

        connections.HasRelationship<Opportunity, Quote>()
            .Using<Opportunity, Quote>().HasForeignKey(q => q.OpportunityId).References(o => o.Id);

        connections.HasRelationship<Quote, QuoteLine>()
            .Using<Quote, QuoteLine>().HasForeignKey(l => l.QuoteId).References(q => q.Id);

        connections.HasRelationship<Quote, ReturnRequest>()
            .Using<Quote, ReturnRequest>().HasForeignKey(r => r.QuoteId).References(q => q.Id);

        connections.HasRelationship<ReturnRequest, ReturnRequestLine>()
            .Using<ReturnRequest, ReturnRequestLine>().HasForeignKey(l => l.ReturnId).References(r => r.Id);
    }
}

internal sealed class CustomerTable : FoundginePersistenceConfiguration<Customer>
{
    public override void Configure(FoundginePersistenceBuilder<Customer> model)
    {
        model.ToTable("customers").HasKey(x => x.Id);
        model.Property(x => x.Id).HasColumnName("customer_id");
        model.Property(x => x.TenantId).HasColumnName("tenant_id");
        model.Property(x => x.Kind).HasColumnName("kind");
        model.Property(x => x.Name).HasColumnName("name");
        model.Property(x => x.Email).HasColumnName("email");
        model.Property(x => x.Phone).HasColumnName("phone");
        model.Property(x => x.Status).HasColumnName("status");
        model.Property(x => x.CreatedAt).HasColumnName("created_at");
        model.Property(x => x.CreatedBy).HasColumnName("created_by");
        model.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        model.Property(x => x.UpdatedBy).HasColumnName("updated_by");
        model.HasIndex(x => x.TenantId);
    }
}

internal sealed class ContactTable : FoundginePersistenceConfiguration<Contact>
{
    public override void Configure(FoundginePersistenceBuilder<Contact> model)
    {
        model.ToTable("contacts").HasKey(x => x.Id);
        model.Property(x => x.Id).HasColumnName("contact_id");
        model.Property(x => x.TenantId).HasColumnName("tenant_id");
        model.Property(x => x.CustomerId).HasColumnName("customer_id");
        model.Property(x => x.FirstName).HasColumnName("first_name");
        model.Property(x => x.LastName).HasColumnName("last_name");
        model.Property(x => x.Email).HasColumnName("email");
        model.Property(x => x.Phone).HasColumnName("phone");
        model.Property(x => x.IsPrimary).HasColumnName("is_primary");
        model.Property(x => x.CreatedAt).HasColumnName("created_at");
        model.Property(x => x.CreatedBy).HasColumnName("created_by");
        model.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        model.Property(x => x.UpdatedBy).HasColumnName("updated_by");
        model.HasIndex(x => x.CustomerId);
    }
}

internal sealed class OpportunityTable : FoundginePersistenceConfiguration<Opportunity>
{
    public override void Configure(FoundginePersistenceBuilder<Opportunity> model)
    {
        model.ToTable("opportunities").HasKey(x => x.Id);
        model.Property(x => x.Id).HasColumnName("opportunity_id");
        model.Property(x => x.TenantId).HasColumnName("tenant_id");
        model.Property(x => x.CustomerId).HasColumnName("customer_id");
        model.Property(x => x.Name).HasColumnName("name");
        model.Property(x => x.Stage).HasColumnName("stage");
        model.Property(x => x.Amount).HasColumnName("amount");
        model.Property(x => x.CreatedAt).HasColumnName("created_at");
        model.Property(x => x.CreatedBy).HasColumnName("created_by");
        model.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        model.Property(x => x.UpdatedBy).HasColumnName("updated_by");
        model.HasIndex(x => x.CustomerId);
    }
}

internal sealed class QuoteTable : FoundginePersistenceConfiguration<Quote>
{
    public override void Configure(FoundginePersistenceBuilder<Quote> model)
    {
        model.ToTable("quotes").HasKey(x => x.Id);
        model.Property(x => x.Id).HasColumnName("quote_id");
        model.Property(x => x.TenantId).HasColumnName("tenant_id");
        model.Property(x => x.OpportunityId).HasColumnName("opportunity_id");
        model.Property(x => x.CustomerId).HasColumnName("customer_id");
        model.Property(x => x.Status).HasColumnName("status");
        model.Property(x => x.Total).HasColumnName("total");
        model.Property(x => x.SubmittedBy).HasColumnName("submitted_by");
        model.Property(x => x.DecidedBy).HasColumnName("decided_by");
        model.Property(x => x.ErpOrderId).HasColumnName("erp_order_id");
        model.Property(x => x.OrderFailureReason).HasColumnName("order_failure_reason");
        model.Property(x => x.CreatedAt).HasColumnName("created_at");
        model.Property(x => x.CreatedBy).HasColumnName("created_by");
        model.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        model.Property(x => x.UpdatedBy).HasColumnName("updated_by");
        model.HasIndex(x => x.CustomerId);
    }
}

internal sealed class QuoteLineTable : FoundginePersistenceConfiguration<QuoteLine>
{
    public override void Configure(FoundginePersistenceBuilder<QuoteLine> model)
    {
        model.ToTable("quote_lines").HasKey(x => x.Id);
        model.Property(x => x.Id).HasColumnName("quote_line_id");
        model.Property(x => x.TenantId).HasColumnName("tenant_id");
        model.Property(x => x.QuoteId).HasColumnName("quote_id");
        model.Property(x => x.Sku).HasColumnName("sku");
        model.Property(x => x.Description).HasColumnName("description");
        model.Property(x => x.Quantity).HasColumnName("quantity");
        model.Property(x => x.UnitPrice).HasColumnName("unit_price");
        model.HasIndex(x => x.QuoteId);
    }
}

internal sealed class MappingTable : FoundginePersistenceConfiguration<CustomerIntegrationMapping>
{
    public override void Configure(FoundginePersistenceBuilder<CustomerIntegrationMapping> model)
    {
        model.ToTable("customer_integration_mappings").HasKey(x => x.CrmCustomerId);
        model.Property(x => x.CrmCustomerId).HasColumnName("crm_customer_id");
        model.Property(x => x.TenantId).HasColumnName("tenant_id");
        model.Property(x => x.ErpCustomerId).HasColumnName("erp_customer_id");
        model.Property(x => x.Status).HasColumnName("status");
        model.Property(x => x.Attempt).HasColumnName("attempt");
        model.Property(x => x.LastError).HasColumnName("last_error");
        model.Property(x => x.CreatedAt).HasColumnName("created_at");
        model.Property(x => x.UpdatedAt).HasColumnName("updated_at");
    }
}

internal sealed class ReturnRequestTable : FoundginePersistenceConfiguration<ReturnRequest>
{
    public override void Configure(FoundginePersistenceBuilder<ReturnRequest> model)
    {
        model.ToTable("return_requests").HasKey(x => x.Id);
        model.Property(x => x.Id).HasColumnName("return_id");
        model.Property(x => x.TenantId).HasColumnName("tenant_id");
        model.Property(x => x.QuoteId).HasColumnName("quote_id");
        model.Property(x => x.CustomerId).HasColumnName("customer_id");
        model.Property(x => x.Status).HasColumnName("status");
        model.Property(x => x.Reason).HasColumnName("reason");
        model.Property(x => x.ErpReturnId).HasColumnName("erp_return_id");
        model.Property(x => x.DecisionNote).HasColumnName("decision_note");
        model.Property(x => x.RequestedBy).HasColumnName("requested_by");
        model.Property(x => x.CreatedAt).HasColumnName("created_at");
        model.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        model.HasIndex(x => x.QuoteId);
    }
}

internal sealed class ReturnRequestLineTable : FoundginePersistenceConfiguration<ReturnRequestLine>
{
    public override void Configure(FoundginePersistenceBuilder<ReturnRequestLine> model)
    {
        model.ToTable("return_request_lines").HasKey(x => x.Id);
        model.Property(x => x.Id).HasColumnName("return_line_id");
        model.Property(x => x.TenantId).HasColumnName("tenant_id");
        model.Property(x => x.ReturnId).HasColumnName("return_id");
        model.Property(x => x.Sku).HasColumnName("sku");
        model.Property(x => x.Quantity).HasColumnName("quantity");
        model.HasIndex(x => x.ReturnId);
    }
}
