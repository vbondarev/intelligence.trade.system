namespace Intelligence.TradeSystem.Domain;

/// <summary>
/// Тип торгового аккаунта Bybit.
/// Определяет, с какого субсчёта запрашивается баланс.
/// </summary>
public enum AccountType
{
    /// <summary>
    /// Unified Trading Account (UTA) — объединённый аккаунт, поддерживает
    /// спот, деривативы и опционы с единым пулом маржи.
    /// </summary>
    Unified,

    /// <summary>
    /// Деривативный аккаунт (фьючерсы, бессрочные контракты).
    /// Используется в классической (non-UTA) схеме.
    /// </summary>
    Contract,

    /// <summary>Спотовый аккаунт.</summary>
    Spot,
}
