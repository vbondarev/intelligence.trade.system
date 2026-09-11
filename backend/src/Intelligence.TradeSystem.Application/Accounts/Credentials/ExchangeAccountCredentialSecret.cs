namespace Intelligence.TradeSystem.Application.Accounts.Credentials;

/// <summary>
/// Временная пара API-учётных данных. Значения намеренно доступны только через
/// контролируемый обратный вызов и никогда не раскрываются в открытых свойствах.
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
    /// Использует значения учётных данных в течение обратного вызова.
    /// </summary>
    public void Use(Action<string, string> use)
    {
        ArgumentNullException.ThrowIfNull(use);
        use(_apiKey, _apiSecret);
    }

    /// <summary>
    /// Использует значения учётных данных в течение обратного вызова и возвращает его результат.
    /// </summary>
    public TResult Use<TResult>(Func<string, string, TResult> use)
    {
        ArgumentNullException.ThrowIfNull(use);
        return use(_apiKey, _apiSecret);
    }

    /// <inheritdoc />
    public override string ToString() => "[REDACTED EXCHANGE CREDENTIALS]";
}
