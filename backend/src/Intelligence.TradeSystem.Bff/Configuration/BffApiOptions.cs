namespace Intelligence.TradeSystem.Bff.Configuration;

/// <summary>
/// Адрес business API, к которому BFF обращается от имени browser session.
/// </summary>
public sealed class BffApiOptions
{
    public const string SectionName = "Bff:Api";

    public string? BaseAddress { get; init; }

    internal Uri Validate(IHostEnvironment environment) =>
        BffConfigurationValidation.RequireAbsoluteUri(BaseAddress, "Bff:Api:BaseAddress", environment);
}
