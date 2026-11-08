using System.Text.Json;
using Foundgine.Extensions.Temporal.Configuration;
using Foundgine.Extensions.Temporal.Messaging;
using Foundgine.Providers.Outbox;

namespace Crm.Api.Integration;

/// <summary>Delivers one outbox row to Temporal. The seam between the CRM outbox and the Temporal providers.</summary>
public interface IOutboxRouter
{
    Task DispatchAsync(Crm.Api.Domain.OutboxMessage row, CancellationToken ct);
}

/// <summary>
/// Two delivery paths, chosen by the row's type:
/// <list type="bullet">
/// <item>Types listed in <c>Foundgine:Temporal:TopicRoutes</c> go through the Foundgine Temporal provider
/// (<see cref="FoundgineTemporalOutboxPublisher"/>): a generic process workflow on the foundgine queue, with the
/// initiator in the message headers and the outbox id as business key (redelivery is absorbed).</item>
/// <item>Everything else keeps starting the Java orchestration workflows through <see cref="IWorkflowStarter"/>.</item>
/// </list>
/// Both paths carry the Keycloak subject that caused the row (<c>initiator</c>) and the tenant, never a token.
/// </summary>
public sealed class OutboxRouter(IWorkflowStarter starter, FoundgineTemporalOptions? foundgineOptions = null,
    FoundgineTemporalOutboxPublisher? foundgine = null) : IOutboxRouter
{
    public async Task DispatchAsync(Crm.Api.Domain.OutboxMessage row, CancellationToken ct)
    {
        if (foundgine is not null && foundgineOptions is not null && foundgineOptions.TopicRoutes.ContainsKey(row.Type))
        {
            var headers = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(row.Initiator)) headers[FoundgineTemporalOutboxPublisher.InitiatorHeader] = row.Initiator;
            await foundgine.PublishAsync(new FoundgineOutboxMessage(
                Id: row.Id, TenantId: row.TenantId, Topic: row.Type, PayloadJson: row.Payload,
                Key: row.AggregateId.ToString(), CorrelationId: row.Id.ToString(), Headers: headers,
                CreatedAt: new DateTimeOffset(DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc))), ct);
            return;
        }

        switch (row.Type)
        {
            case IntegrationNames.OnboardingRequested:
                await starter.StartCustomerOnboardingAsync(JsonSerializer.Deserialize<CustomerSyncDto>(row.Payload)!, ct, row.Initiator);
                break;
            case QuoteIntegrationNames.QuoteAccepted:
                await starter.StartQuoteToOrderAsync(JsonSerializer.Deserialize<QuoteAcceptedDto>(row.Payload)!, ct, row.Initiator);
                break;
            case FulfilmentIntegrationNames.ReturnRequested:
                await starter.StartReturnProcessingAsync(JsonSerializer.Deserialize<ReturnRequestedDto>(row.Payload)!, ct, row.Initiator);
                break;
            default:
                throw new InvalidOperationException($"Unknown outbox type {row.Type}");
        }
    }
}
