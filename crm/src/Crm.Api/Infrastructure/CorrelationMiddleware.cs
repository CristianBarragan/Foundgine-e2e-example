namespace Crm.Api.Infrastructure;

/// <summary>
/// Accepts an inbound X-Correlation-Id (sanitised, max 64 chars) or mints one, echoes it on the response, stores it in
/// HttpContext.Items and opens a logging scope so every log line of the request carries it.
/// </summary>
public sealed class CorrelationMiddleware(RequestDelegate next, ILogger<CorrelationMiddleware> log)
{
    public const string Header = "X-Correlation-Id";
    public const string ItemKey = "correlation-id";

    public async Task InvokeAsync(HttpContext ctx)
    {
        var id = Sanitise(ctx.Request.Headers[Header].FirstOrDefault()) ?? Guid.NewGuid().ToString("N");
        ctx.Items[ItemKey] = id;
        System.Diagnostics.Activity.Current?.SetTag("correlation.id", id); // searchable on the request span
        ctx.Response.OnStarting(() =>
        {
            ctx.Response.Headers[Header] = id;
            return Task.CompletedTask;
        });
        using (log.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = id }))
            await next(ctx);
    }

    /// <summary>Only letters, digits, '-', '_' and '.' survive; anything else (log injection) means "mint a new one".</summary>
    public static string? Sanitise(string? v) =>
        string.IsNullOrWhiteSpace(v) || v.Length > 64 || !v.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.') ? null : v;

    public static string Current(HttpContext? ctx) => ctx?.Items[ItemKey] as string ?? "";
}
