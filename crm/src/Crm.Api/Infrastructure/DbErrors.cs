using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Crm.Api.Infrastructure;

/// <summary>Database error classification for SQL Server, kept in one place so services do not know the provider.</summary>
public static class DbErrors
{
    /// <summary>2601 = duplicate key in a unique index, 2627 = unique or primary key constraint violation.</summary>
    public static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };
}
