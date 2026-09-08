namespace Intelligence.TradeSystem.Application.Accounts.Access;

/// <summary>
/// Neutral metadata needed to decide whether an exchange API key can be used safely.
/// </summary>
public sealed record ApiKeyAccessMetadata(bool IsReadOnly);
