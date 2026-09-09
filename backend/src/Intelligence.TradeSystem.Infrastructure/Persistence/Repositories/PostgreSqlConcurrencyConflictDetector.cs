using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;

internal static class PostgreSqlConcurrencyConflictDetector
{
    public static bool IsDuplicatePrimaryKey(
        DbUpdateException exception,
        string primaryKeyConstraintName)
        => IsUniqueConstraint(exception, primaryKeyConstraintName);

    public static bool IsUniqueConstraint(
        DbUpdateException exception,
        string constraintName)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(constraintName);

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgresException)
            {
                return postgresException.SqlState == "23505" &&
                       string.Equals(
                           postgresException.ConstraintName,
                           constraintName,
                           StringComparison.Ordinal);
            }
        }

        return false;
    }
}
