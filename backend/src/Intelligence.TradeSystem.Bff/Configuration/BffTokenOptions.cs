namespace Intelligence.TradeSystem.Bff.Configuration;

/// <summary>
/// Параметры обслуживания OAuth tokens BFF session.
/// </summary>
/// <remarks>
/// Значения по умолчанию задаются только в <c>appsettings.json</c>. Свойства nullable, чтобы
/// отсутствующее значение отличалось от явного <see cref="TimeSpan.Zero"/>.
/// </remarks>
public sealed class BffTokenOptions
{
    public const string SectionName = "Bff:Token";

    /// <summary>
    /// Насколько заранее до истечения access token BFF выполняет refresh.
    /// </summary>
    public TimeSpan? RefreshSkew { get; init; }

    /// <summary>
    /// Timeout server-to-server запроса к OIDC token endpoint Identity.
    /// </summary>
    public TimeSpan? EndpointTimeout { get; init; }

    internal BffTokenSettings Validate()
    {
        if (RefreshSkew is not { } refreshSkew || refreshSkew < TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "Bff:Token:RefreshSkew должен быть задан и не может быть отрицательным.");
        }

        if (EndpointTimeout is not { } endpointTimeout || endpointTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "Bff:Token:EndpointTimeout должен быть задан и больше нуля.");
        }

        return new BffTokenSettings(refreshSkew, endpointTimeout);
    }
}

/// <summary>
/// Проверенные параметры обслуживания OAuth tokens.
/// </summary>
internal sealed record BffTokenSettings(TimeSpan RefreshSkew, TimeSpan EndpointTimeout);
