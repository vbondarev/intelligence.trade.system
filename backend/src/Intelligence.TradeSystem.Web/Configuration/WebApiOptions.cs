namespace Intelligence.TradeSystem.Web.Configuration;

/// <summary>
/// Адрес business API, к которому BFF обращается от имени browser session.
/// </summary>
public sealed class WebApiOptions
{
    public const string SectionName = "Web:Api";

    public string? BaseAddress { get; init; }

    internal Uri Validate(IHostEnvironment environment) =>
        WebConfigurationValidation.RequireAbsoluteUri(BaseAddress, "Web:Api:BaseAddress", environment);
}
