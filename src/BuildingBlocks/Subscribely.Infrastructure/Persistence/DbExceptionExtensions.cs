using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Subscribely.Infrastructure.Persistence;

public static class DbExceptionExtensions
{
    /// <summary>Violação de índice UNIQUE (23505); opcionalmente de uma constraint específica.</summary>
    public static bool IsUniqueViolation(this DbUpdateException exception, string? constraintName = null) =>
        exception?.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
        && (constraintName is null || string.Equals(pg.ConstraintName, constraintName, StringComparison.Ordinal));
}
