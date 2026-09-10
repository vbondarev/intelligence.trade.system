using Intelligence.TradeSystem.Application.Concurrency;

namespace Intelligence.TradeSystem.Application.Accounts.Credentials;

/// <summary>
/// Технические метаданные сохранённой пары учётных данных. Они никогда не содержат открытый текст
/// или зашифрованные учётные данные.
/// </summary>
public sealed record ExchangeAccountCredentialMetadata(ConcurrencyVersion Version);
