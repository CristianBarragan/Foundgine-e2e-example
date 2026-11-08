using System.Text.Json;
using Crm.Api.Domain;
using Crm.Api.Integration;
using Foundgine.Extensions.Temporal.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using Xunit;

public class OutboxRouterTests
{
    private static OutboxMessage Row(string type, string payload, string? initiator) => new()
    {
        Id = Guid.NewGuid(), TenantId = "acme", Type = type, AggregateId = 7, Payload = payload,
        CreatedAt = DateTime.UtcNow, NextAttemptAt = DateTime.UtcNow, Initiator = initiator
    };

    [Fact]
    public async Task Legacy_types_start_the_java_workflow_and_carry_the_initiator()
    {
        var starter = new FakeStarter();
        var dto = new CustomerSyncDto(1, "acme", 7, 1, "c-1", "Business", "Acme", "a@acme.test", "", "Active");
        await new OutboxRouter(starter).DispatchAsync(
            Row(IntegrationNames.OnboardingRequested, JsonSerializer.Serialize(dto), "user-42"), default);
        Assert.Single(starter.Started);
        Assert.Equal("user-42", Assert.Single(starter.Initiators));
    }

    [Fact]
    public async Task Unknown_type_fails_so_the_outbox_retries()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new OutboxRouter(new FakeStarter()).DispatchAsync(Row("Nope", "{}", null), default));
    }

    [Fact]
    public async Task Routed_topic_is_not_started_as_a_java_workflow_when_the_foundgine_bridge_is_absent()
    {
        // Without the bridge registered (Temporal disabled) a routed topic falls through to the legacy switch and is
        // rejected as unknown rather than silently dropped.
        var options = new FoundgineTemporalOptions();
        options.TopicRoutes["crm.something"] = "tenant-user-provisioning";
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new OutboxRouter(new FakeStarter(), options, null).DispatchAsync(Row("crm.something", "{}", null), default));
    }
}

public class InitiatorPropagationTests(CrmFactory f) : IClassFixture<CrmFactory>
{
    [SkippableFact]
    public async Task Customer_create_publishes_with_the_calling_subject_as_initiator()
    {
        Skip.If(CrmFactory.Connection is null, "CRM_TEST_CONNECTION not set");
        var tenant = $"r-{Guid.NewGuid():N}";
        (await f.As(tenant, "alice-sub", "crm-sales").PostAsJsonAsync("/api/v1/customers", CrmFactory.NewCustomer()))
            .EnsureSuccessStatusCode();
        using var scope = f.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<OutboxPublisher>().PublishBatchAsync(100, default);
        Assert.Contains("alice-sub", f.Starter.Initiators);
    }
}
