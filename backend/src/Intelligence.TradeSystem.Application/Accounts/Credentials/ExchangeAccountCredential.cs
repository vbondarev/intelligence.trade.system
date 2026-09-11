using Intelligence.TradeSystem.Application.Concurrency;

namespace Intelligence.TradeSystem.Application.Accounts.Credentials;

/// <summary>
/// Пара учётных данных в области пользователя вместе с независимой версией сохранения.
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
    /// Использует временные значения учётных данных, не раскрывая их в свойствах объекта.
    /// </summary>
    public void Use(Action<string, string> use) => secret.Use(use);

    /// <summary>
    /// Использует временные значения учётных данных и возвращает результат обратного вызова.
    /// </summary>
    public TResult Use<TResult>(Func<string, string, TResult> use) => secret.Use(use);

    /// <inheritdoc />
    public override string ToString() => $"Exchange account credential version {Version}.";
}
