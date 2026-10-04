namespace Intelligence.TradeSystem.Identity.Configuration;

/// <summary>
/// Локальный пользователь для ручной проверки и browser E2E. Допускается только в Development.
/// </summary>
public sealed class DevelopmentUserOptions
{
    public const string SectionName = "Identity:DevelopmentUser";

    public bool Enabled { get; init; }

    public string? Username { get; init; }

    /// <summary>
    /// Пароль передаётся только через environment/secret store и никогда не логируется.
    /// </summary>
    public string? Password { get; init; }

    public void Validate(IHostEnvironment environment)
    {
        if (!Enabled)
        {
            return;
        }

        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "Identity:DevelopmentUser:Enabled допускается только в окружении Development.");
        }

        if (string.IsNullOrWhiteSpace(Username))
        {
            throw new InvalidOperationException("Identity:DevelopmentUser:Username должен быть задан.");
        }

        if (string.IsNullOrWhiteSpace(Password))
        {
            throw new InvalidOperationException("Identity:DevelopmentUser:Password должен быть задан.");
        }
    }
}
