namespace Intelligence.TradeSystem.Domain.Snapshots;

/// <summary>
/// Снимок текущего состояния торгового счёта: балансы и список открытых позиций.
/// Предоставляет GPT контекст о риск-экспозиции трейдера на момент анализа.
/// </summary>
public sealed record PortfolioSnapshot
{
    /// <summary>
    /// Возвращает недоступный снимок портфеля с нулевыми значениями.
    /// Используется, когда авторизованный пользовательский контекст ещё недоступен
    /// (например, в legacy-эндпоинте, работающем только с публичными рыночными данными).
    /// </summary>
    public static PortfolioSnapshot Unavailable { get; } = new()
    {
        IsAvailable = false,
        TotalEquityUsd = 0m,
        AvailableBalanceUsd = 0m,
        TotalWalletBalanceUsd = 0m,
        TotalUnrealizedPnlUsd = 0m,
        OpenPositions = [],
    };

    /// <summary>
    /// Признак доступности данных портфеля.
    /// <c>false</c> — данные временно недоступны (например, биржа вернула ошибку или истёк токен).
    /// При <c>false</c> числовые поля содержат нулевые значения.
    /// По умолчанию <c>true</c>.
    /// </summary>
    public bool IsAvailable { get; init; } = true;

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
