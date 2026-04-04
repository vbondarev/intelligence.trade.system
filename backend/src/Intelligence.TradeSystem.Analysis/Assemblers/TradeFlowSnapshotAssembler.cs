using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Analysis.Assemblers;

/// <summary>
/// Собирает <see cref="TradeFlowSnapshot"/> из списка сырых сделок <see cref="Trade"/>.
/// <para>
/// Порядок преобразований:
/// <list type="number">
///   <item>Валидация входного списка</item>
///   <item>Вычисление объёмов: BuyVolume / SellVolume / DeltaVolume / DeltaPct</item>
///   <item>Подсчёт количества сделок: TotalTrades / BuyTrades / SellTrades</item>
///   <item>Вычисление сигналов: AvgTradeSize / MaxTradeSize / флаги агрессии</item>
///   <item>Сборка снимка с временны́м окном</item>
/// </list>
/// </para>
/// </summary>
public static class TradeFlowSnapshotAssembler
{
    /// <summary>
    /// Порог дельты объёма в процентах, выше которого давление считается агрессивным.
    /// Если <c>|DeltaPct| > AggressivePressureThresholdPct</c> — выставляется соответствующий флаг.
    /// </summary>
    internal const decimal AggressivePressureThresholdPct = 10m;

    /// <summary>
    /// Вычисляет и возвращает <see cref="TradeFlowSnapshot"/> для переданного списка сделок.
    /// </summary>
    /// <param name="trades">Список сырых сделок с биржи.</param>
    /// <exception cref="ArgumentNullException">Если <paramref name="trades"/> равен <c>null</c>.</exception>
    /// <exception cref="ArgumentException">Если список сделок пустой.</exception>
    public static TradeFlowSnapshot Assemble(IReadOnlyList<Trade> trades)
    {
        // 1. Validate
        ArgumentNullException.ThrowIfNull(trades);

        if (trades.Count == 0)
            throw new ArgumentException("Trades list must not be empty.", nameof(trades));

        // 2. Volumes
        decimal buyVolume  = 0m;
        decimal sellVolume = 0m;

        foreach (var trade in trades)
        {
            if (trade.Side == TradeSide.Buy)
                buyVolume  += trade.Quantity;
            else
                sellVolume += trade.Quantity;
        }

        var totalVolume = buyVolume + sellVolume;
        var deltaVolume = buyVolume - sellVolume;
        var deltaPct    = totalVolume == 0m ? 0m : deltaVolume / totalVolume * 100m;

        // 3. Counts
        var totalTrades = trades.Count;
        var buyTrades   = trades.Count(t => t.Side == TradeSide.Buy);
        var sellTrades  = totalTrades - buyTrades;

        // 4. Signals
        var avgTradeSize = totalVolume / totalTrades;
        var maxTradeSize = trades.Max(t => t.Quantity);

        var hasAggressiveBuyPressure  = deltaPct >  AggressivePressureThresholdPct;
        var hasAggressiveSellPressure = deltaPct < -AggressivePressureThresholdPct;

        // 5. Assemble
        return new TradeFlowSnapshot
        {
            WindowStartUtc = trades.Min(t => t.Timestamp),
            WindowEndUtc   = trades.Max(t => t.Timestamp),

            BuyVolume   = buyVolume,
            SellVolume  = sellVolume,
            DeltaVolume = deltaVolume,
            DeltaPct    = deltaPct,

            TotalTrades = totalTrades,
            BuyTrades   = buyTrades,
            SellTrades  = sellTrades,

            AvgTradeSize = avgTradeSize,
            MaxTradeSize = maxTradeSize,

            HasAggressiveBuyPressure  = hasAggressiveBuyPressure,
            HasAggressiveSellPressure = hasAggressiveSellPressure
        };
    }
}

