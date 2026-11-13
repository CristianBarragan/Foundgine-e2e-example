using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>Test host with dev-header auth. DB-backed tests need CRM_TEST_CONNECTION (e.g. the compose crm-db: Server=localhost,1433;Database=crm_test;User Id=sa;Password=Crm_dev_pw1!;TrustServerCertificate=True).</summary>
public sealed class CrmFactory : WebApplicationFactory<Program>
{
    public FakeStarter Starter { get; } = new();

    public static string? Connection => Environment.GetEnvironmentVariable("CRM_TEST_CONNECTION");

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("Auth:DevHeaders", "true");
        builder.UseSetting("Foundgine:Keycloak:EphemeralWarrantKey", "true");
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<Crm.Api.Integration.IWorkflowStarter>();
            s.AddSingleton(Starter);
            s.AddSingleton<Crm.Api.Integration.IWorkflowStarter>(Starter);
        });
        if (Connection is { } cs)
        {
            builder.UseSetting("CrmConnectionString", cs);
            builder.UseSetting("Database:MigrateOnStart", "true");
        }
    }

    public HttpClient As(string tenant, string user, params string[] roles)
    {
        var c = CreateClient();
        c.DefaultRequestHeaders.Add("X-Tenant-Id", tenant);
        c.DefaultRequestHeaders.Add("X-User-Id", user);
        c.DefaultRequestHeaders.Add("X-Roles", string.Join(',', roles));
        return c;
    }

    public static object NewCustomer(string? email = null) => new
    {
        kind = "Business", name = "Acme Ltd", email = email ?? $"{Guid.NewGuid():N}@acme.test", phone = "021 555 0100"
    };
}

public sealed class FakeStarter : Crm.Api.Integration.IWorkflowStarter
{
    public List<Crm.Api.Integration.CustomerSyncDto> Started { get; } = [];
    public List<Crm.Api.Integration.QuoteAcceptedDto> Quotes { get; } = [];
    public List<Crm.Api.Integration.ReturnRequestedDto> Returns { get; } = [];
    public List<string?> Initiators { get; } = [];
    public bool Fail { get; set; }

    public Task StartReturnProcessingAsync(Crm.Api.Integration.ReturnRequestedDto dto, CancellationToken ct, string? initiator = null)
    {
        if (Fail) throw new InvalidOperationException("temporal down");
        lock (Returns) Returns.Add(dto);
        lock (Initiators) Initiators.Add(initiator);
        return Task.CompletedTask;
    }

    public Task StartQuoteToOrderAsync(Crm.Api.Integration.QuoteAcceptedDto dto, CancellationToken ct, string? initiator = null)
    {
        if (Fail) throw new InvalidOperationException("temporal down");
        lock (Quotes) Quotes.Add(dto);
        lock (Initiators) Initiators.Add(initiator);
        return Task.CompletedTask;
    }

    public Task StartCustomerOnboardingAsync(Crm.Api.Integration.CustomerSyncDto dto, CancellationToken ct, string? initiator = null)
    {
        if (Fail) throw new InvalidOperationException("temporal down");
        lock (Started) Started.Add(dto);
        lock (Initiators) Initiators.Add(initiator);
        return Task.CompletedTask;
    }
}
