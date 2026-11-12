using Crm.Api.Application;
using Foundgine.Providers.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Crm.Api.Infrastructure;

public sealed class AuditRow
{
    public long Id { get; set; }
    public string UserId { get; set; } = "";
    public string Action { get; set; } = "";
    public string Target { get; set; } = "";
    public string Outcome { get; set; } = "";
    public string Detail { get; set; } = "";
    public string CorrelationId { get; set; } = "";
    public DateTime OccurredAt { get; set; }
}

/// <summary>Writes and reads the append-only admin audit trail on its own connection (so a failed business transaction is still audited).</summary>
public sealed class AdminAudit(IDbContextFactory<FoundgineDbContext> factory, ITenantContext tenant, IHttpContextAccessor http)
{
    public async Task WriteAsync(string action, string target, string outcome, string detail, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var correlation = CorrelationMiddleware.Current(http.HttpContext);
        Telemetry.AdminAudit.Add(1, new KeyValuePair<string, object?>("action", action), new KeyValuePair<string, object?>("outcome", outcome));
        var d = detail.Length > 300 ? detail[..300] : detail;
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO admin_audit (tenant_id, user_id, action, target, outcome, detail, correlation_id, occurred_at)
            VALUES ({tenant.TenantId}, {tenant.UserId}, {action}, {target}, {outcome}, {d}, {correlation}, {DateTime.UtcNow})", ct);
    }

    public async Task<IReadOnlyList<AuditRow>> ListAsync(int take, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var t = tenant.TenantId;
        var n = Math.Clamp(take, 1, 200);
        return await db.Database.SqlQuery<AuditRow>($@"
            SELECT audit_id AS ""Id"", user_id AS ""UserId"", action AS ""Action"", target AS ""Target"", outcome AS ""Outcome"",
                   detail AS ""Detail"", correlation_id AS ""CorrelationId"", occurred_at AS ""OccurredAt""
            FROM admin_audit WHERE tenant_id = {t} ORDER BY audit_id DESC LIMIT {n}").ToListAsync(ct);
    }
}

/// <summary>Endpoint filter: audits the outcome of an administrative endpoint. Succeeded (2xx), Rejected (a business error such as 403/404/409/400) or Failed (anything else).</summary>
public sealed class AuditFilter(string action) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var audit = ctx.HttpContext.RequestServices.GetRequiredService<AdminAudit>();
        var ct = ctx.HttpContext.RequestAborted;
        var target = ctx.HttpContext.Request.RouteValues["id"]?.ToString() ?? "";
        try
        {
            var result = await next(ctx);
            await audit.WriteAsync(action, target, "Succeeded", "", ct);
            return result;
        }
        catch (Exception ex) when (ex is CrmValidationException or CrmConflictException or CrmNotFoundException or CrmForbiddenException)
        {
            await audit.WriteAsync(action, target, "Rejected", ex.Message, CancellationToken.None);
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await audit.WriteAsync(action, target, "Failed", ex.GetType().Name, CancellationToken.None);
            throw;
        }
    }
}
