using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Application.Portfolio;

/// <summary>
/// Собирает <see cref="PortfolioSnapshot"/> из сырых данных аккаунта и списка открытых позиций.
/// </summary>
public static class PortfolioSnapshotAssembler
{
    public static PortfolioSnapshot Assemble(
        AccountBalance? balance,
        IReadOnlyList<OpenPosition> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);

        var totalEquity = balance?.TotalEquity ?? 0m;
        var availableBalance = balance?.TotalAvailableBalance ?? 0m;
        var totalWalletBalance = balance?.TotalWalletBalance ?? 0m;
        var totalUnrealizedPnl = balance?.TotalPerpUnrealizedPnl ?? 0m;

        var openPositions = positions
            .Where(p => p.Size > 0m)
            .Select(MapPosition)
            .ToList();

        return new PortfolioSnapshot
        {
            TotalEquityUsd = totalEquity,
            AvailableBalanceUsd = availableBalance,
            TotalWalletBalanceUsd = totalWalletBalance,
            TotalUnrealizedPnlUsd = totalUnrealizedPnl,
            OpenPositions = openPositions,
        };
    }

    private static OpenPositionSnapshot MapPosition(OpenPosition p)
    {
        var positionValueUsd = p.PositionValue ?? 0m;
        var unrealizedPnlUsd = p.UnrealizedPnl ?? 0m;

        var unrealizedPnlPct = positionValueUsd > 0m
            ? Math.Round(unrealizedPnlUsd / positionValueUsd * 100m, 4)
            : 0m;

        return new OpenPositionSnapshot
        {
            Symbol = p.Symbol,
            Side = p.Side,
            Size = p.Size,
            AvgPrice = p.AvgPrice ?? 0m,
            MarkPrice = p.MarkPrice ?? 0m,
            BreakEvenPrice = p.BreakEvenPrice ?? 0m,
            LiquidationPrice = p.LiquidationPrice ?? 0m,
            PositionValueUsd = positionValueUsd,
            Leverage = p.Leverage ?? 0m,
            UnrealizedPnlUsd = unrealizedPnlUsd,
            UnrealizedPnlPct = unrealizedPnlPct,
        };
    }
}
