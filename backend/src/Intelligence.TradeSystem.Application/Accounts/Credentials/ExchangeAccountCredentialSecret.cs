namespace Intelligence.TradeSystem.Application.Accounts.Credentials;

/// <summary>
/// Transient API credential pair. The values are intentionally available only through
/// a controlled callback and are never exposed as public properties.
/// </summary>
public sealed class ExchangeAccountCredentialSecret
{
    private readonly string _apiKey;
    private readonly string _apiSecret;

    public ExchangeAccountCredentialSecret(string apiKey, string apiSecret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiSecret);

        _apiKey = apiKey;
        _apiSecret = apiSecret;
    }

    /// <summary>
    /// Uses the credential values for the duration of the callback.
    /// </summary>
    public void Use(Action<string, string> use)
    {
        ArgumentNullException.ThrowIfNull(use);
        use(_apiKey, _apiSecret);
    }

    /// <summary>
    /// Uses the credential values for the duration of the callback and returns its result.
    /// </summary>
    public TResult Use<TResult>(Func<string, string, TResult> use)
    {
        ArgumentNullException.ThrowIfNull(use);
        return use(_apiKey, _apiSecret);
    }

    /// <inheritdoc />
    public override string ToString() => "[REDACTED EXCHANGE CREDENTIALS]";
}
