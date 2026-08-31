using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.MarketIntelligence.Snapshots;

namespace Intelligence.TradeSystem.MarketIntelligence.Analysis.Assemblers;

/// <summary>
/// Собирает <see cref="PriceSnapshot"/> из сырых данных тикера <see cref="Ticker"/>.
/// <para>
/// Порядок преобразований:
/// <list type="number">
///   <item>Валидация входного тикера</item>
///   <item>Вычисление производных полей: абсолютный и процентный спред</item>
///   <item>Сборка снимка</item>
/// </list>
/// </para>
/// </summary>
public static class PriceSnapshotAssembler
{
    /// <summary>
    /// Вычисляет и возвращает <see cref="PriceSnapshot"/> для переданного тикера.
    /// </summary>
    /// <param name="ticker">Сырые данные тикера с биржи.</param>
    /// <exception cref="ArgumentNullException">Если <paramref name="ticker"/> равен <c>null</c>.</exception>
    public static PriceSnapshot Assemble(Ticker ticker)
    {
        ArgumentNullException.ThrowIfNull(ticker);

        // 1. Derived
        var spreadAbs = ticker.AskPrice - ticker.BidPrice;
        var midPrice = (ticker.BidPrice + ticker.AskPrice) / 2m;
        var spreadPct = midPrice > 0m
            ? Math.Round(spreadAbs / midPrice * 100m, 4)
            : 0m;

        // 2. Assemble
        return new PriceSnapshot
        {
            LastPrice = ticker.LastPrice,
            MarkPrice = ticker.MarkPrice,
            IndexPrice = ticker.IndexPrice,

            BidPrice = ticker.BidPrice,
            BidSize = ticker.BidSize,
            AskPrice = ticker.AskPrice,
            AskSize = ticker.AskSize,

            SpreadAbs = spreadAbs,
            SpreadPct = spreadPct,

            Price24hChangePct = ticker.Price24hChangePct,
            High24h = ticker.High24h,
            Low24h = ticker.Low24h,
            Volume24h = ticker.Volume24h,
            Turnover24h = ticker.Turnover24h,
        };
    }
}
