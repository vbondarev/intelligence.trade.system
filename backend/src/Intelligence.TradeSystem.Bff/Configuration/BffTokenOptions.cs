namespace Intelligence.TradeSystem.Bff.Configuration;

/// <summary>
/// Параметры обслуживания OAuth tokens BFF session.
/// </summary>
/// <remarks>
/// Порог refresh access token — внутренний invariant 60 секунд и deployment setting не является.
/// <see cref="EndpointTimeout"/> задаётся в <c>appsettings.json</c>. Свойство nullable, чтобы
/// отсутствующее значение отличалось от явного нуля.
/// </remarks>
public sealed class BffTokenOptions
{
    public const string SectionName = "Bff:Token";

    /// <summary>
    /// Timeout server-to-server запроса к OIDC token endpoint Identity.
    /// </summary>
    public TimeSpan? EndpointTimeout { get; init; }

    internal BffTokenSettings Validate()
    {
        if (EndpointTimeout is not { } endpointTimeout || endpointTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "Bff:Token:EndpointTimeout должен быть задан и больше нуля.");
        }

        return new BffTokenSettings(endpointTimeout);
    }
}

/// <summary>
/// Проверенные параметры обслуживания OAuth tokens.
/// </summary>
internal sealed record BffTokenSettings(TimeSpan EndpointTimeout);
