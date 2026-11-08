using Crm.Api.Application;
using Crm.Api.Infrastructure;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Providers.EntityFrameworkCore;
using Foundgine.Runtime;
using Microsoft.EntityFrameworkCore;

namespace Crm.Api.Domain;

/// <summary>Foundgine capability for the CRM: model, EF provider on PostgreSQL and the application services.</summary>
public sealed class CrmDomain : IFoundgineCapability
{
    public const string ConnectionStringName = "CrmConnectionString";

    public static void Configure(FoundgineCapabilityContext context)
    {
        context.Options.UseMetadata(CrmSemantics.Metadata);
        // Tenant and capability checks happen in the application services (see TenantScope / policies).
        context.Options.AuthorizationPolicy ??= new AllowAllSemanticAuthorizationPolicy();

        context.Services.AddFoundgineEf(
            CrmSemantics.Metadata,
            (sp, o) => o.UseCrmNpgsql(
                sp.GetRequiredService<IConfiguration>()[ConnectionStringName]
                ?? Environment.GetEnvironmentVariable(ConnectionStringName)
                ?? throw new InvalidOperationException($"{ConnectionStringName} is required.")),
            CrmEf.ConfigureModel);

        context.Services.AddSingleton(new SemanticAuthorizer(new AllowAllSemanticAuthorizationPolicy()));
        context.Services.AddSingleton<Foundgine.Core.Semantic.Planning.Planner>();
        context.Services.AddScoped<CustomerService>();
        context.Services.AddScoped<ContactService>();
        context.Services.AddScoped<OpportunityService>();
        context.Services.AddScoped<QuoteService>();
    }
}

public static class CrmDomainExtensions
{
    public static FoundgineOptions UseCrmDomain(this FoundgineOptions options) => options.Enable<CrmDomain>();
}
