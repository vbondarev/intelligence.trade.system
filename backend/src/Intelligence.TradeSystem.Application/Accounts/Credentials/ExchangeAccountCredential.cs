using Intelligence.TradeSystem.Application.Concurrency;

namespace Intelligence.TradeSystem.Application.Accounts.Credentials;

/// <summary>
/// A user-scoped credential pair together with its independent persistence version.
/// </summary>
public sealed class ExchangeAccountCredential
{
    private readonly ExchangeAccountCredentialSecret secret;

    public ExchangeAccountCredential(
        ExchangeAccountCredentialSecret secret,
        ConcurrencyVersion version)
    {
        ArgumentNullException.ThrowIfNull(secret);
        this.secret = secret;
        Version = version;
    }

    public ConcurrencyVersion Version { get; }

    /// <summary>
    /// Uses the transient credential values without exposing them as object properties.
    /// </summary>
    public void Use(Action<string, string> use) => secret.Use(use);

    /// <inheritdoc />
    public override string ToString() => $"Exchange account credential version {Version}.";
}
