using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Indicators;

/// <summary>
/// Собирает <see cref="DerivativesSnapshot"/> из сырых данных тикера и уже вычисленных
/// снапшотов ставки финансирования, открытого интереса и соотношения лонг/шорт.
/// <para>
/// Порядок преобразований:
/// <list type="number">
///   <item>Валидация тикера</item>
///   <item>Текущие значения из <see cref="Ticker"/>: FundingRate, NextFundingTimeUtc, OpenInterest, OpenInterestValue</item>
///   <item>Вычисление PremiumVsIndexPct из Mark/Index цен тикера (null, если IndexPrice = 0)</item>
///   <item>FundingRateAvg24h из <see cref="FundingRateSnapshot"/> — или fallback на текущую ставку</item>
///   <item>OpenInterestChange1hPct / Change4hPct из <see cref="OpenInterestSnapshot"/></item>
///   <item>LongRatio / ShortRatio из <see cref="LongShortRatioSnapshot"/></item>
///   <item>Сборка снимка</item>
/// </list>
/// </para>
/// </summary>
public static class DerivativesSnapshotAssembler
{
    /// <summary>
    /// Вычисляет и возвращает <see cref="DerivativesSnapshot"/> для переданного тикера
    /// и вспомогательных снапшотов.
    /// </summary>
    /// <param name="ticker">Сырые данные тикера с биржи.</param>
    /// <param name="fundingRate">
    /// Снапшот истории ставки финансирования, собранный через <see cref="FundingRateSnapshotAssembler"/>.
    /// Если <c>null</c>, <see cref="DerivativesSnapshot.FundingRateAvg24h"/> заполняется
    /// текущей ставкой из тикера.
    /// </param>
    /// <param name="openInterest">
    /// Снапшот открытого интереса, собранный через <see cref="OpenInterestSnapshotAssembler"/>.
    /// Если <c>null</c>, изменения OI устанавливаются в <c>0</c>.
    /// </param>
    /// <param name="longShortRatio">
    /// Снапшот соотношения лонг/шорт, собранный через <see cref="LongShortRatioSnapshotAssembler"/>.
    /// Если <c>null</c>, коэффициенты позиционирования устанавливаются в <c>0</c>.
    /// </param>
    /// <exception cref="ArgumentNullException">Если <paramref name="ticker"/> равен <c>null</c>.</exception>
    public static DerivativesSnapshot Assemble(
        Ticker ticker,
        FundingRateSnapshot? fundingRate,
        OpenInterestSnapshot? openInterest,
        LongShortRatioSnapshot? longShortRatio)
    {
        // 1. Validate
        ArgumentNullException.ThrowIfNull(ticker);

        // 2. Current values from ticker
        var currentFundingRate        = ticker.FundingRate       ?? 0m;
        var currentOpenInterest       = ticker.OpenInterest      ?? 0m;
        var currentOpenInterestValue  = ticker.OpenInterestValue ?? 0m;

        // 3. PremiumVsIndexPct = (MarkPrice − IndexPrice) / IndexPrice × 100
        //    null when IndexPrice = 0 (spot market or data unavailable)
        var premiumVsIndexPct = ticker.IndexPrice > 0m
            ? Math.Round((ticker.MarkPrice - ticker.IndexPrice) / ticker.IndexPrice * 100m, 4)
            : (decimal?)null;

        // 4. FundingRateAvg24h — from history snapshot; fallback to current rate
        var fundingRateAvg24h = fundingRate?.Avg24hRate ?? currentFundingRate;

        // 5. OI changes — from snapshot; default 0 when unavailable
        var oiChange1hPct = openInterest?.Change1hPct ?? 0m;
        var oiChange4hPct = openInterest?.Change4hPct ?? 0m;

        // 6. Long / Short ratios — from snapshot; default 0 when unavailable
        var longRatio  = longShortRatio?.CurrentBuyRatio  ?? 0m;
        var shortRatio = longShortRatio?.CurrentSellRatio ?? 0m;

        // 7. Assemble
        return new DerivativesSnapshot
        {
            FundingRate        = currentFundingRate,
            NextFundingTimeUtc = ticker.NextFundingTimeUtc,
            OpenInterest       = currentOpenInterest,
            OpenInterestValue  = currentOpenInterestValue,

            LongRatio  = longRatio,
            ShortRatio = shortRatio,

            PremiumVsIndexPct = premiumVsIndexPct,

            OpenInterestChange1hPct = oiChange1hPct,
            OpenInterestChange4hPct = oiChange4hPct,

            FundingRateAvg24h = fundingRateAvg24h,
        };
    }
}

