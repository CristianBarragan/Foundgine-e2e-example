using Microsoft.EntityFrameworkCore;

namespace Foundgine.Crm.Migrations;

// Schema-only EF model. The API continues to execute reads/writes through Foundgine + Npgsql.
public sealed class CrmDbContext(DbContextOptions<CrmDbContext> options) : DbContext(options)
{
    public DbSet<CompanyRow> Companies => Set<CompanyRow>();
    public DbSet<ContactRow> Contacts => Set<ContactRow>();
    public DbSet<OpportunityRow> Opportunities => Set<OpportunityRow>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("public");
        model.Entity<CompanyRow>(e =>
        {
            e.ToTable("crm_companies"); e.HasKey(x => x.Id).HasName("pk_crm_companies");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            e.Property(x => x.Industry).HasColumnName("industry");
            e.Property(x => x.Website).HasColumnName("website");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("now()");
            e.HasAlternateKey(x => new { x.TenantId, x.Id }).HasName("uq_crm_companies_tenant_id");
            e.HasIndex(x => new { x.TenantId, x.Name }).HasDatabaseName("ix_crm_companies_tenant_name");
        });
        model.Entity<ContactRow>(e =>
        {
            e.ToTable("crm_contacts"); e.HasKey(x => x.Id).HasName("pk_crm_contacts");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
            e.Property(x => x.CompanyId).HasColumnName("company_id");
            e.Property(x => x.FirstName).HasColumnName("first_name").HasMaxLength(200).IsRequired();
            e.Property(x => x.LastName).HasColumnName("last_name").HasMaxLength(200).IsRequired();
            e.Property(x => x.Email).HasColumnName("email"); e.Property(x => x.Phone).HasColumnName("phone");
            e.HasOne<CompanyRow>().WithMany().HasForeignKey(x => new { x.TenantId, x.CompanyId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_crm_contacts_company");
            e.HasIndex(x => new { x.TenantId, x.Email }).HasDatabaseName("ix_crm_contacts_tenant_email");
        });
        model.Entity<OpportunityRow>(e =>
        {
            e.ToTable("crm_opportunities", table => table.HasCheckConstraint("ck_crm_opportunities_amount_nonnegative", "amount >= 0")); e.HasKey(x => x.Id).HasName("pk_crm_opportunities");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
            e.Property(x => x.CompanyId).HasColumnName("company_id");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            e.Property(x => x.Amount).HasColumnName("amount").HasPrecision(18, 2);
            e.Property(x => x.Stage).HasColumnName("stage").HasDefaultValue("Qualification").IsRequired();
            e.Property(x => x.ExpectedCloseDate).HasColumnName("expected_close_date").HasColumnType("timestamp with time zone");
            e.HasOne<CompanyRow>().WithMany().HasForeignKey(x => new { x.TenantId, x.CompanyId })
                .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_crm_opportunities_company");
            e.HasIndex(x => new { x.TenantId, x.Stage }).HasDatabaseName("ix_crm_opportunities_tenant_stage");
        });
    }
}

public sealed class CompanyRow
{
    public Guid Id { get; set; } public string TenantId { get; set; } = ""; public string Name { get; set; } = "";
    public string? Industry { get; set; } public string? Website { get; set; } public DateTimeOffset CreatedAt { get; set; }
}
public sealed class ContactRow
{
    public Guid Id { get; set; } public string TenantId { get; set; } = ""; public Guid? CompanyId { get; set; }
    public string FirstName { get; set; } = ""; public string LastName { get; set; } = ""; public string? Email { get; set; } public string? Phone { get; set; }
}
public sealed class OpportunityRow
{
    public Guid Id { get; set; } public string TenantId { get; set; } = ""; public Guid CompanyId { get; set; }
    public string Name { get; set; } = ""; public decimal Amount { get; set; } public string Stage { get; set; } = "Qualification"; public DateTimeOffset? ExpectedCloseDate { get; set; }
}
