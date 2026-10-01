using Npgsql;

namespace Intelligence.TradeSystem.Infrastructure.Persistence;

/// <summary>
/// Configuration contract business connection string для EF Core design-time tooling.
/// </summary>
/// <remarks>
/// Единственным source является environment variable <see cref="EnvironmentVariableName"/>:
/// <c>appsettings*.json</c> и другие providers runtime host не используются. Значение читается
/// один раз при запуске процесса, поэтому изменение environment требует повторного запуска.
/// Проверяется только синтаксис PostgreSQL connection string, без подключения к базе данных.
/// </remarks>
internal static class TradeSystemDesignTimeConnectionString
{
    internal const string EnvironmentVariableName = "ConnectionStrings__TradeSystem";

    /// <exception cref="InvalidOperationException">
    /// Environment variable не задана, пуста или содержит некорректную connection string.
    /// </exception>
    internal static string ReadRequiredFromEnvironment() =>
        Validate(Environment.GetEnvironmentVariable(EnvironmentVariableName));

    /// <returns>Исходное значение без нормализации.</returns>
    /// <exception cref="InvalidOperationException">
    /// Значение отсутствует, пусто или не является корректной PostgreSQL connection string.
    /// </exception>
    internal static string Validate(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"{EnvironmentVariableName} должна быть задана непустой PostgreSQL connection string.");
        }

        try
        {
            _ = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            // Исключение parser не переносится в цепочку: вложенные ошибки преобразования
            // могут содержать исходное значение connection string вместе с secrets.
            throw new InvalidOperationException(
                $"{EnvironmentVariableName} содержит некорректную PostgreSQL connection string.");
        }

        return connectionString;
    }
}
