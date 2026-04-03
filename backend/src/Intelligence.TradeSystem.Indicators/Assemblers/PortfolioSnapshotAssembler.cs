using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Indicators.Assemblers;

/// <summary>
/// Собирает <see cref="PortfolioSnapshot"/> из сырых данных аккаунта и списка открытых позиций.
/// <para>
/// Порядок преобразований:
/// <list type="number">
///   <item>Валидация списка позиций</item>
///   <item>Балансы из <see cref="AccountBalance"/>; <c>null</c>-баланс даёт нулевые значения</item>
///   <item>Маппинг каждой позиции в <see cref="OpenPositionSnapshot"/>; позиции с нулевым размером пропускаются</item>
///   <item>Вычисление <c>UnrealizedPnlPct</c> через безопасное деление</item>
///   <item>Сборка снимка</item>
/// </list>
/// </para>
/// </summary>
public static class PortfolioSnapshotAssembler
{
    /// <summary>
    /// Вычисляет и возвращает <see cref="PortfolioSnapshot"/> для переданного баланса
    /// и списка открытых позиций.
    /// </summary>
    /// <param name="balance">
    /// Баланс торгового аккаунта. Если <c>null</c> (например, приватный запрос завершился ошибкой),
    /// все балансовые поля снапшота устанавливаются в <c>0</c>.
    /// </param>
    /// <param name="positions">
    /// Список открытых позиций. Может быть пустым — это означает отсутствие открытых позиций.
    /// </param>
    /// <exception cref="ArgumentNullException">Если <paramref name="positions"/> равен <c>null</c>.</exception>
    public static PortfolioSnapshot Assemble(
        AccountBalance? balance,
        IReadOnlyList<OpenPosition> positions)
    {
        // 1. Validate
        ArgumentNullException.ThrowIfNull(positions);

        // 2. Balance fields — safe fallback to 0 when balance unavailable
        var totalEquity          = balance?.TotalEquity             ?? 0m;
        var availableBalance     = balance?.TotalAvailableBalance   ?? 0m;
        var totalWalletBalance   = balance?.TotalWalletBalance      ?? 0m;
        var totalUnrealizedPnl   = balance?.TotalPerpUnrealizedPnl  ?? 0m;

        // 3. Map positions — skip any that somehow have zero size (defence-in-depth)
        var openPositions = positions
            .Where(p => p.Size > 0m)
            .Select(MapPosition)
            .ToList();

        // 4. Assemble
        return new PortfolioSnapshot
        {
            TotalEquityUsd        = totalEquity,
            AvailableBalanceUsd   = availableBalance,
            TotalWalletBalanceUsd = totalWalletBalance,
            TotalUnrealizedPnlUsd = totalUnrealizedPnl,
            OpenPositions         = openPositions,
        };
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static OpenPositionSnapshot MapPosition(OpenPosition p)
    {
        var positionValueUsd  = p.PositionValue ?? 0m;
        var unrealizedPnlUsd  = p.UnrealizedPnl ?? 0m;

        // UnrealizedPnlPct = (UnrealizedPnl / PositionValue) × 100; safe division
        var unrealizedPnlPct  = positionValueUsd > 0m
            ? Math.Round(unrealizedPnlUsd / positionValueUsd * 100m, 4)
            : 0m;

        return new OpenPositionSnapshot
        {
            Symbol           = p.Symbol,
            Side             = p.Side,
            Size             = p.Size,
            AvgPrice         = p.AvgPrice         ?? 0m,
            MarkPrice        = p.MarkPrice        ?? 0m,
            BreakEvenPrice   = p.BreakEvenPrice   ?? 0m,
            LiquidationPrice = p.LiquidationPrice ?? 0m,
            PositionValueUsd = positionValueUsd,
            Leverage         = p.Leverage         ?? 0m,
            UnrealizedPnlUsd = unrealizedPnlUsd,
            UnrealizedPnlPct = unrealizedPnlPct,
        };
    }
}

