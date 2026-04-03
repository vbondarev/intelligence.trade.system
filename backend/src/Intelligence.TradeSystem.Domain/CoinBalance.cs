namespace Intelligence.TradeSystem.Domain;

/// <summary>
/// Баланс отдельной монеты внутри аккаунта Bybit.
/// Содержит как стоимостные показатели (в USD), так и количественные.
/// </summary>
public sealed record CoinBalance(
    string Coin,
    decimal? Equity,
    decimal? UsdValue,
    decimal? WalletBalance,
    decimal? AvailableBalance,
    decimal? Locked,
    decimal? UnrealizedPnl)
{
    /// <summary>Тикер монеты, например <c>BTC</c>, <c>USDT</c>.</summary>
    public string Coin { get; init; } = Coin;

    /// <summary>
    /// Эквити монеты: <c>WalletBalance + UnrealizedPnl</c>.
    /// <c>null</c> если данные недоступны для данного типа аккаунта.
    /// </summary>
    public decimal? Equity { get; init; } = Equity;

    /// <summary>
    /// Рыночная стоимость монеты в USD.
    /// <c>null</c> если данные недоступны.
    /// </summary>
    public decimal? UsdValue { get; init; } = UsdValue;

    /// <summary>Баланс кошелька (без учёта нереализованного PnL).</summary>
    public decimal? WalletBalance { get; init; } = WalletBalance;

    /// <summary>
    /// Свободный баланс, доступный для торговли/вывода.
    /// Соответствует полю <c>free</c> в ответе Bybit.
    /// </summary>
    public decimal? AvailableBalance { get; init; } = AvailableBalance;

    /// <summary>Заблокированный баланс (в ордерах и марже).</summary>
    public decimal? Locked { get; init; } = Locked;

    /// <summary>Нереализованный PnL по открытым позициям в данной монете.</summary>
    public decimal? UnrealizedPnl { get; init; } = UnrealizedPnl;
}

