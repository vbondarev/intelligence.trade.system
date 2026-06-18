namespace Intelligence.TradeSystem.Domain;

/// <summary>
/// Баланс торгового аккаунта Bybit — сырые данные с биржи.
/// Содержит агрегированные суммы по всему аккаунту и постатейный список монет.
/// Производные поля (PnL%, distribution и т.п.) вычисляются в ассемблере.
/// </summary>
public sealed record AccountBalance(
    AccountType AccountType,
    decimal? TotalEquity,
    decimal? TotalWalletBalance,
    decimal? TotalAvailableBalance,
    decimal? TotalPerpUnrealizedPnl,
    IReadOnlyList<CoinBalance> Coins)
{
    /// <summary>Тип аккаунта, с которого получены данные.</summary>
    public AccountType AccountType { get; init; } = AccountType;

    /// <summary>
    /// Суммарный капитал аккаунта в USD: <c>TotalWalletBalance + TotalPerpUnrealizedPnl</c>.
    /// <c>null</c> если данные недоступны (например, аккаунт пуст).
    /// </summary>
    public decimal? TotalEquity { get; init; } = TotalEquity;

    /// <summary>Суммарный баланс кошелька в USD без учёта нереализованного PnL.</summary>
    public decimal? TotalWalletBalance { get; init; } = TotalWalletBalance;

    /// <summary>Свободный баланс, доступный для открытия новых позиций (в USD).</summary>
    public decimal? TotalAvailableBalance { get; init; } = TotalAvailableBalance;

    /// <summary>
    /// Совокупный нереализованный PnL по всем бессрочным позициям (в USD).
    /// Положительное значение — позиции в прибыли, отрицательное — в убытке.
    /// </summary>
    public decimal? TotalPerpUnrealizedPnl { get; init; } = TotalPerpUnrealizedPnl;

    /// <summary>
    /// Постатейный список монет с индивидуальными балансами.
    /// Пустой список означает, что аккаунт не содержит активов.
    /// </summary>
    public IReadOnlyList<CoinBalance> Coins { get; init; } = Coins;
}
