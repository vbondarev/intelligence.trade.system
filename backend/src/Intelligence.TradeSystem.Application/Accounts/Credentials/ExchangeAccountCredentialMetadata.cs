using Intelligence.TradeSystem.Application.Concurrency;

namespace Intelligence.TradeSystem.Application.Accounts.Credentials;

/// <summary>
/// Technical metadata for a stored credential pair. It never contains plaintext
/// or encrypted credential material.
/// </summary>
public sealed record ExchangeAccountCredentialMetadata(ConcurrencyVersion Version);
