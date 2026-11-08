using System.Net.Http.Json;
using System.Text.Json;
using Crm.Api.Application;
using Crm.Api.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public class WorkflowIdTests
{
    [Fact] public void First_attempt_id_is_stable() =>
        Assert.Equal("CustomerOnboarding:acme:42", IntegrationNames.WorkflowId("acme", 42, 1));

    [Fact] public void Later_attempts_get_their_own_id() =>
        Assert.Equal("CustomerOnboarding:acme:42:2", IntegrationNames.WorkflowId("acme", 42, 2));
}

// Need PostgreSQL: set CRM_TEST_CONNECTION (see CustomerApiTests).
public class IntegrationTests(CrmFactory f) : IClassFixture<CrmFactory>
{
    private static void RequireDb() => Skip.If(CrmFactory.Connection is null, "CRM_TEST_CONNECTION not set");

    private async Task<(string Tenant, int Id)> CreateCustomer()
    {
        var tenant = $"i-{Guid.NewGuid():N}";
        var res = await f.As(tenant, "alice", "crm-sales").PostAsJsonAsync("/api/v1/customers", CrmFactory.NewCustomer());
        res.EnsureSuccessStatusCode();
        return (tenant, (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32());
    }

    private T Run<T>(Func<IServiceProvider, T> f2) { using var s = f.Services.CreateScope(); return f2(s.ServiceProvider); }

    [SkippableFact]
    public async Task Create_writes_pending_mapping_and_one_outbox_message_that_publishes_once()
    {
        RequireDb();
        var (tenant, id) = await CreateCustomer();
        var m = await Run(sp => sp.GetRequiredService<IntegrationService>().GetMappingAsync(tenant, id, default));
        Assert.Equal("Pending", m!.Status);
        Assert.Null(m.ErpCustomerId);

        using (var scope = f.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<OutboxPublisher>().PublishBatchAsync(100, default);
        using (var scope = f.Services.CreateScope())
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<OutboxPublisher>().PublishBatchAsync(100, default));
        Assert.Single(f.Starter.Started.Where(d => d.TenantId == tenant && d.CrmCustomerId == id));
    }

    [SkippableFact]
    public async Task Record_erp_customer_is_idempotent_and_rejects_a_different_erp_id()
    {
        RequireDb();
        var (tenant, id) = await CreateCustomer();
        using var scope = f.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IntegrationService>();
        await svc.RecordErpCustomerAsync(new RecordErpCustomerInput(tenant, id, "ERP-9001"), default);
        await svc.RecordErpCustomerAsync(new RecordErpCustomerInput(tenant, id, "ERP-9001"), default); // duplicate message
        var m = await svc.GetMappingAsync(tenant, id, default);
        Assert.Equal("Active", m!.Status);
        Assert.Equal("ERP-9001", m.ErpCustomerId);
        await Assert.ThrowsAsync<CrmConflictException>(() =>
            svc.RecordErpCustomerAsync(new RecordErpCustomerInput(tenant, id, "ERP-9002"), default));
    }

    [SkippableFact]
    public async Task Failed_sync_is_requeued_by_reconcile_with_a_new_attempt()
    {
        RequireDb();
        var (tenant, id) = await CreateCustomer();
        using var scope = f.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IntegrationService>();
        await svc.MarkSyncFailedAsync(new MarkSyncFailedInput(tenant, id, "erp unavailable"), default);
        Assert.Equal("Failed", (await svc.GetMappingAsync(tenant, id, default))!.Status);

        Assert.Equal(1, await svc.ReconcileAsync(tenant, TimeSpan.FromMinutes(15), default));
        var m = await svc.GetMappingAsync(tenant, id, default);
        Assert.Equal("Pending", m!.Status);
        Assert.Equal(2, m.Attempt);
        Assert.Equal(0, await svc.ReconcileAsync("another-tenant", TimeSpan.Zero, default)); // tenant-scoped
    }

    [SkippableFact]
    public async Task Publish_failure_keeps_the_message_for_retry()
    {
        RequireDb();
        await CreateCustomer();
        f.Starter.Fail = true;
        try
        {
            using var scope = f.Services.CreateScope();
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<OutboxPublisher>().PublishBatchAsync(100, default));
        }
        finally { f.Starter.Fail = false; }
    }

    [SkippableFact]
    public async Task Reconcile_endpoint_requires_admin()
    {
        RequireDb();
        var res = await f.As("t", "u", "crm-sales").PostAsync("/api/v1/integration/reconcile", null);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, res.StatusCode);
    }
}
