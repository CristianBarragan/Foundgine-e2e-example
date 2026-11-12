using Crm.Api.Application;
using Microsoft.AspNetCore.Diagnostics;

namespace Crm.Api.Api;

public sealed class CrmExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        var (status, title) = ex switch
        {
            CrmValidationException => (StatusCodes.Status400BadRequest, "Validation failed"),
            CrmNotFoundException => (StatusCodes.Status404NotFound, "Not found"),
            CrmConflictException => (StatusCodes.Status409Conflict, "Conflict"),
            CrmForbiddenException => (StatusCodes.Status403Forbidden, "Forbidden"),
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Unauthorized"),
            _ => (0, "")
        };
        if (status == 0) return false;
        ctx.Response.StatusCode = status;
        await ctx.Response.WriteAsJsonAsync(new { title, detail = ex.Message, status }, ct);
        return true;
    }
}
