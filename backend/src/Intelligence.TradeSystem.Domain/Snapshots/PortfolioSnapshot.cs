namespace Intelligence.TradeSystem.Domain.Snapshots;

/// <summary>
/// Снимок текущего состояния торгового счёта: балансы и список открытых позиций.
/// Предоставляет GPT контекст о риск-экспозиции трейдера на момент анализа.
/// </summary>
public sealed record PortfolioSnapshot
{
    /// <summary>Суммарный капитал счёта в USD: <c>WalletBalance + UnrealizedPnL</c>.</summary>
    public decimal TotalEquityUsd { get; init; }

    /// <summary>Свободный баланс, доступный для открытия новых позиций (в USD).</summary>
    public decimal AvailableBalanceUsd { get; init; }

    /// <summary>Суммарный баланс кошелька без учёта нереализованного PnL (в USD).</summary>
    public decimal TotalWalletBalanceUsd { get; init; }

    /// <summary>
    /// Совокупный нереализованный PnL по всем открытым позициям (в USD).
    /// Положительное значение — позиции в прибыли, отрицательное — в убытке.
    /// </summary>
    public decimal TotalUnrealizedPnlUsd { get; init; }

    /// <summary>Список всех открытых позиций на момент снимка.</summary>
    public List<OpenPositionSnapshot> OpenPositions { get; init; } = [];
}
