using Foundgine.Core.Semantic.Configuration;
using Foundgine.Providers.EntityFrameworkCore;

namespace Crm.Api.Domain;

/// <summary>Single registration point: semantic model, persistence model, identity maps and connections.</summary>
public sealed class CrmModel : FoundgineModel
{
    public override void Configure(FoundgineModelBuilder model)
    {
        model.Configure<CustomerSemantics>();
        model.Configure<ContactSemantics>();
        model.Configure<OpportunitySemantics>();
        model.Configure<QuoteSemantics>();
        model.Configure<Plain<QuoteLine>>();

        model.Configure<CustomerTable>();
        model.Configure<ContactTable>();
        model.Configure<OpportunityTable>();
        model.Configure<QuoteTable>();
        model.Configure<QuoteLineTable>();

        model.Configure<IdentityEntityMap<Customer>>();
        model.Configure<IdentityEntityMap<Contact>>();
        model.Configure<IdentityEntityMap<Opportunity>>();
        model.Configure<IdentityEntityMap<Quote>>();
        model.Configure<IdentityEntityMap<QuoteLine>>();

        model.Configure<CrmConnections>();
    }
}

internal sealed class CustomerSemantics : FoundgineModelConfiguration<Customer>
{
    public override void Configure(FoundgineModelBuilder<Customer> model)
    {
        model.HasRelationship(x => x.Contacts).To<Contact>();
        model.HasRelationship(x => x.Opportunities).To<Opportunity>();
    }
}

internal sealed class ContactSemantics : FoundgineModelConfiguration<Contact>
{
    public override void Configure(FoundgineModelBuilder<Contact> model)
    {
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
    }
}

internal sealed class CrmConnections : FoundgineConnectionConfiguration
{
    public override void Configure(FoundgineConnectionBuilder connections)
    {
        connections.HasRelationship<Customer, Contact>()
            .Using<Customer, Contact>()
            .HasForeignKey(c => c.CustomerId)
            .References(c => c.Id);

        connections.HasRelationship<Customer, Opportunity>()
            .Using<Customer, Opportunity>()
            .HasForeignKey(o => o.CustomerId)
            .References(c => c.Id);

        connections.HasRelationship<Opportunity, Quote>()
            .Using<Opportunity, Quote>()
            .HasForeignKey(q => q.OpportunityId)
            .References(o => o.Id);

        connections.HasRelationship<Quote, QuoteLine>()
            .Using<Quote, QuoteLine>()
            .HasForeignKey(l => l.QuoteId)
            .References(q => q.Id);
    }
}

internal sealed class Plain<T> : FoundgineModelConfiguration<T>
{
    public override void Configure(FoundgineModelBuilder<T> model)
    {
    }
}

internal sealed class OpportunitySemantics : FoundgineModelConfiguration<Opportunity>
{
    public override void Configure(FoundgineModelBuilder<Opportunity> model) =>
        model.HasRelationship(x => x.Quotes).To<Quote>();
}

internal sealed class QuoteSemantics : FoundgineModelConfiguration<Quote>
{
    public override void Configure(FoundgineModelBuilder<Quote> model) =>
        model.HasRelationship(x => x.Lines).To<QuoteLine>();
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
        model.Property(x => x.CreatedAt).HasColumnName("created_at");
        model.Property(x => x.CreatedBy).HasColumnName("created_by");
        model.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        model.Property(x => x.UpdatedBy).HasColumnName("updated_by");
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
    }
}
