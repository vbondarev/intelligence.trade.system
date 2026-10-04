namespace Intelligence.TradeSystem.Web.Configuration;

/// <summary>
/// Параметры server-side browser session.
/// </summary>
public sealed class WebSessionOptions
{
    public const string SectionName = "Web:Session";

    private static readonly TimeSpan MaximumLifetime = TimeSpan.FromHours(24);

    /// <summary>
    /// Sliding lifetime session cookie и server-side ticket.
    /// </summary>
    public TimeSpan Lifetime { get; init; } = TimeSpan.FromHours(8);

    internal void Validate()
    {
        if (Lifetime <= TimeSpan.Zero || Lifetime > MaximumLifetime)
        {
            throw new InvalidOperationException(
                "Web:Session:Lifetime должен быть больше нуля и не больше 24 часов.");
        }
    }
}
