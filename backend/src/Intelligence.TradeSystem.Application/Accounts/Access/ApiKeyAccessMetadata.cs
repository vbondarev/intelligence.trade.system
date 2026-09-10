namespace Intelligence.TradeSystem.Application.Accounts.Access;

/// <summary>
/// Нейтральные метаданные, необходимые для решения, можно ли безопасно использовать API-ключ биржи.
/// </summary>
public sealed record ApiKeyAccessMetadata(bool IsReadOnly);
