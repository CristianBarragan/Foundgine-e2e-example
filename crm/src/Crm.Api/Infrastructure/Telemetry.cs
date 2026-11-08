using System.Diagnostics.Metrics;

namespace Crm.Api.Infrastructure;

/// <summary>Business metrics exported when Telemetry:Enabled. The Meter name is registered with OpenTelemetry in Program.cs.</summary>
public static class Telemetry
{
    public const string MeterName = "Crm.Api";
    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> OutboxPublished = Meter.CreateCounter<long>("crm.outbox.published", description: "Outbox messages handed to Temporal.");
    public static readonly Counter<long> OutboxFailed = Meter.CreateCounter<long>("crm.outbox.publish_failed", description: "Outbox publish attempts that failed and were rescheduled.");
    public static readonly Counter<long> AdminAudit = Meter.CreateCounter<long>("crm.admin.audit", description: "Audited admin operations, tagged by action and outcome.");
}
