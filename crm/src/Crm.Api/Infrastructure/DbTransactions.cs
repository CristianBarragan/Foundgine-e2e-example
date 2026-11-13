using Microsoft.EntityFrameworkCore;

namespace Crm.Api.Infrastructure;

/// <summary>
/// User-initiated transactions with SQL Server's retrying execution strategy (<c>EnableRetryOnFailure</c>, see
/// <see cref="CrmEf"/>). EF refuses <c>BeginTransactionAsync</c> outside the strategy ("does not support user-initiated
/// transactions"); the whole transaction has to run as one retriable unit. Use this instead of
/// <c>Database.BeginTransactionAsync</c>.
/// </summary>
/// <remarks>
/// <paramref name="work"/> may run more than once, so it must be safe to repeat: keep side effects inside the database
/// transaction (rolled back on failure) or idempotent. The change tracker is cleared before every retry so entities
/// added by the failed attempt are not re-saved twice.
/// </remarks>
public static class DbTransactions
{
    public static async Task<T> InTransactionAsync<T>(this DbContext db, Func<Task<T>> work, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        var attempt = 0;
        return await strategy.ExecuteAsync(async () =>
        {
            if (attempt++ > 0) db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var result = await work();
            await tx.CommitAsync(ct);
            return result;
        }, ct);
    }

    public static Task InTransactionAsync(this DbContext db, Func<Task> work, CancellationToken ct) =>
        db.InTransactionAsync<bool>(async () => { await work(); return true; }, ct);
}
