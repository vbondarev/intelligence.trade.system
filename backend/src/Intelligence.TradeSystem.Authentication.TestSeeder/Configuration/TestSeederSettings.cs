namespace Intelligence.TradeSystem.Authentication.TestSeeder.Configuration;

/// <summary>
/// Собственные <c>TestSeeder:*</c> settings одноразового seeding process.
/// </summary>
/// <remarks>
/// Identity connection string сюда не входит: её читает и валидирует
/// <c>AddIdentityPersistence</c>. Settings читаются из стандартного <see cref="IConfiguration"/>
/// один раз при запуске; изменённые значения применяются только повторным запуском seeder.
/// </remarks>
public sealed record TestSeederSettings(
    string Username,
    string Password,
    string ClientId,
    string RedirectUri)
{
    public static TestSeederSettings FromConfiguration(IConfiguration configuration)
    {
        var username = GetRequired(configuration, "TestSeeder:Username");
        var password = GetRequired(configuration, "TestSeeder:Password");
        var clientId = GetRequired(configuration, "TestSeeder:ClientId");
        var redirectUri = GetRequired(configuration, "TestSeeder:RedirectUri");

        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("TestSeeder:RedirectUri должен быть абсолютным URI.");
        }

        return new TestSeederSettings(username, password, clientId, redirectUri);
    }

    private static string GetRequired(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{key} должен быть задан непустым значением.");
        }

        return value;
    }
}
