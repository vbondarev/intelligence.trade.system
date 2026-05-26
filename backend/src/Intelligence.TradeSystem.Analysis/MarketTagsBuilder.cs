using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Analysis;

/// <summary>
/// Централизованный builder тегов снапшота V2.
///
/// Whitelist V1 (сохранён для совместимости):
///   trending · neutral · positive-funding · negative-funding
///   bid-pressure · ask-pressure · aggressive-buying · aggressive-selling
///
/// Whitelist V2 (расширен):
///   Режим:      volatile-regime · bullish-regime · bearish-regime · unknown-market-regime
///   Funding:    neutral-funding
///   OrderBook:  strong-orderbook-imbalance · upper-liquidity-heavy · lower-liquidity-heavy
///   TradeFlow:  stale-tradeflow · short-tradeflow-window · low-tradeflow-volume
///               orderbook-tradeflow-conflict · weak-tradeflow-confirmation
///   OI:         oi-declining · oi-rising · long-crowded · short-crowded
///               possible-short-covering · possible-long-unwinding
///   Price:      near-24h-high · near-24h-low
///   Таймфреймы: low-volume · rsi-overbought · rsi-oversold · weak-trend · range-bound
///               neutral-timeframes · near-resistance · near-support · overextended-momentum
///               directional-trend-with-neutral-regime
///   Качество:   no-clean-entry · actionable-entry · weak-entry-confirmation
///               trend-confirmed-entry-filtered · stale-snapshot · stale-orderbook
///
/// Порядок тегов детерминирован по приоритету:
///   HIGH → MEDIUM → LOW (в каждой группе — порядок добавления в Build).
/// Дедупликация: первое вхождение сохраняется. Лимит: <see cref="MaxTags"/>.
/// </summary>
internal static class MarketTagsBuilder
{
    // ─── V1 whitelist — строковые константы тегов ─────────────────────────────

    /// <summary>Рыночный режим активного тренда.</summary>
    public const string TagTrending = MarketTagConstants.Trending;

    /// <summary>Нейтральный рыночный режим.</summary>
    public const string TagNeutral = MarketTagConstants.Neutral;

    /// <summary>Ставка финансирования положительная (лонги переплачивают).</summary>
    public const string TagPositiveFunding = MarketTagConstants.PositiveFunding;

    /// <summary>Ставка финансирования отрицательная (шорты переплачивают).</summary>
    public const string TagNegativeFunding = MarketTagConstants.NegativeFunding;

    /// <summary>Стакан заявок с доминированием bid-стороны.</summary>
    public const string TagBidPressure = MarketTagConstants.BidPressure;

    /// <summary>Стакан заявок с доминированием ask-стороны.</summary>
    public const string TagAskPressure = MarketTagConstants.AskPressure;

    /// <summary>Агрессивное давление покупателей в потоке сделок.</summary>
    public const string TagAggressiveBuying = MarketTagConstants.AggressiveBuying;

    /// <summary>Агрессивное давление продавцов в потоке сделок.</summary>
    public const string TagAggressiveSelling = MarketTagConstants.AggressiveSelling;

    // ─── V2 whitelist — режим ─────────────────────────────────────────────────

    public const string TagVolatileRegime = MarketTagConstants.VolatileRegime;
    public const string TagBullishRegime = MarketTagConstants.BullishRegime;
    public const string TagBearishRegime = MarketTagConstants.BearishRegime;
    public const string TagUnknownMarketRegime = MarketTagConstants.UnknownMarketRegime;

    // ─── V2 whitelist — tradeFlow quality (из TradeFlowPressureScoreAdjuster) ──
    public const string TagLowTradeFlowVolume = MarketTagConstants.LowTradeFlowVolume;
    public const string TagOrderBookTradeFlowConflict = MarketTagConstants.OrderBookTradeFlowConflict;
    public const string TagWeakTradeFlowConfirmation = MarketTagConstants.WeakTradeFlowConfirmation;

    // ─── V2 whitelist — orderBook ──────────────────────────────────────────────

    public const string TagStrongOrderBookImbalance = MarketTagConstants.StrongOrderBookImbalance;
    public const string TagUpperLiquidityHeavy = MarketTagConstants.UpperLiquidityHeavy;
    public const string TagLowerLiquidityHeavy = MarketTagConstants.LowerLiquidityHeavy;
    public const string TagOiDeclining = MarketTagConstants.OiDeclining;
    public const string TagOiRising = MarketTagConstants.OiRising;
    public const string TagLongCrowded = MarketTagConstants.LongCrowded;
    public const string TagShortCrowded = MarketTagConstants.ShortCrowded;
    public const string TagPossibleShortCovering = MarketTagConstants.PossibleShortCovering;
    public const string TagPossibleLongUnwinding = MarketTagConstants.PossibleLongUnwinding;
    public const string TagNeutralFunding = MarketTagConstants.NeutralFunding;
    public const string TagNear24hHigh = MarketTagConstants.Near24hHigh;
    public const string TagNear24hLow = MarketTagConstants.Near24hLow;
    public const string TagLowVolume = MarketTagConstants.LowVolume;
    public const string TagRsiOverbought = MarketTagConstants.RsiOverbought;
    public const string TagRsiOversold = MarketTagConstants.RsiOversold;
    public const string TagWeakTrend = MarketTagConstants.WeakTrend;
    public const string TagRangeBound = MarketTagConstants.RangeBound;
    public const string TagNeutralTimeframes = MarketTagConstants.NeutralTimeframes;
    public const string TagNearResistance = MarketTagConstants.NearResistance;
    public const string TagNearSupport = MarketTagConstants.NearSupport;
    public const string TagOverextendedMomentum = MarketTagConstants.OverextendedMomentum;
    public const string TagDirectionalTrendWithNeutralRegime = MarketTagConstants.DirectionalTrendWithNeutralRegime;

    // ─── Параметры ───────────────────────────────────────────────────────────

    /// <summary>Максимальное количество тегов в одном снапшоте.</summary>
    public const int MaxTags = MarketTagConstants.MaxTags;

    /// <summary>
    /// Порог абсолютного дисбаланса стакана на глубине Top-5,
    /// при превышении которого выставляется тег давления.
    /// </summary>
    internal const decimal OrderBookPressureThreshold = 0.3m;

    /// <summary>Порог для сильного дисбаланса стакана (|OBPressureScore| >= threshold).</summary>
    internal const decimal StrongOrderBookImbalanceThreshold = 0.75m;

    /// <summary>Порог направленного давления для score-based условий.</summary>
    internal const decimal DirectionalPressureThreshold = 0.25m;

    /// <summary>Порог близости к 24ч high/low в процентах.</summary>
    internal const decimal Near24hProximityPct = 0.30m;

    /// <summary>Порог VolumeRatio ниже которого считается низкий объём.</summary>
    internal const decimal LowVolumeRatioThreshold = 0.50m;

    /// <summary>Порог ставки финансирования, ниже которого считается нейтральной.</summary>
    internal const decimal NeutralFundingThreshold = 0.00001m;

    /// <summary>Порог соотношения лонг/шорт для "переполненной" стороны.</summary>
    internal const decimal LongShortCrowdingThreshold = 0.55m;

    /// <summary>Порог силы тренда, ниже которого тренд считается слабым (от TrendStrengthLabelMapper.ModerateThreshold).</summary>
    internal const decimal WeakTrendThreshold = 0.50m;

    /// <summary>Порог близости к уровню (0.30%) для near-resistance/near-support.</summary>
    internal const decimal NearLevelThreshold = 0.30m;

    /// <summary>Порог скоса ликвидности для upper/lower liquidity heavy.</summary>
    private const decimal LiquiditySkewThreshold = 0.15m;

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Строит детерминированный список тегов из снапшотов.
    /// Теги добавляются в порядке приоритета (HIGH → MEDIUM → LOW).
    /// Результат дедуплицирован и содержит не более <see cref="MaxTags"/> тегов.
    /// </summary>
    /// <param name="derivatives">Снапшот деривативов (обязательный).</param>
    /// <param name="orderBook">Снапшот стакана (обязательный).</param>
    /// <param name="tradeFlow">Снапшот потока сделок (обязательный).</param>
    /// <param name="sentiment">Снапшот сентимента (обязательный).</param>
    /// <param name="price">Снапшот цены для проверки близости к 24ч high/low (опционально).</param>
    /// <param name="m15">Таймфрейм M15 для rule-based тегов (опционально).</param>
    /// <param name="h1">Таймфрейм H1 для rule-based тегов (опционально).</param>
    /// <param name="h4">Таймфрейм H4 для rule-based тегов (опционально).</param>
    /// <param name="capturedAtUtc">
    /// Время фиксации снапшота — используется только для freshness-проверки tradeFlow.
    /// Если <c>null</c>, freshness-cap не применяется (рекомендуется <c>null</c> из assembler,
    /// т.к. mode-specific пороги известны только в API-слое).
    /// </param>
    public static List<string> Build(
        DerivativesSnapshot derivatives,
        OrderBookSnapshot orderBook,
        TradeFlowSnapshot tradeFlow,
        SentimentSnapshot sentiment,
        PriceSnapshot? price = null,
        TimeframeAnalysisSnapshot? m15 = null,
        TimeframeAnalysisSnapshot? h1 = null,
        TimeframeAnalysisSnapshot? h4 = null,
        DateTimeOffset? capturedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(derivatives);
        ArgumentNullException.ThrowIfNull(orderBook);
        ArgumentNullException.ThrowIfNull(tradeFlow);
        ArgumentNullException.ThrowIfNull(sentiment);

        var all = new List<string>(MaxTags * 2);
        var primaryTfs = GetNonNullTfs(m15, h1, h4);

        // ── HIGH PRIORITY ──────────────────────────────────────────────────────

        // 1. TradeFlow quality (short window, low volume, conflict, weak confirmation)
        //    stale-tradeflow здесь не генерируется при capturedAtUtc=null — добавляется LlmTagEnricher.
        var qualityTags = TradeFlowPressureScoreAdjuster.ComputeQualityTags(
            sentiment.TradeFlowPressureScore,
            tradeFlow,
            sentiment.OrderBookPressureScore,
            capturedAtUtc);
        AddAll(all, qualityTags);

        // 2. OI direction
        AddOiTags(derivatives, all);

        // 3. Режим (volatile — самый высокий приоритет в группе)
        AddRegimeTags(sentiment.MarketRegime, all);

        // 4. Близость к 24ч high/low
        if (price is not null)
            AddPrice24hTags(price, all);

        // 5. Низкий объём на первичных ТФ
        if (primaryTfs.Count > 0)
            AddLowVolumeTags(primaryTfs, all);

        // 6. Направленный тренд при нейтральном режиме
        if (primaryTfs.Count > 0)
            AddDirectionalNeutralRegimeTag(primaryTfs, sentiment.MarketRegime, all);

        // ── MEDIUM PRIORITY ────────────────────────────────────────────────────

        // 7. Давление в стакане
        AddOrderBookTags(orderBook, sentiment, all);

        // 8. Агрессия в потоке сделок (buying имеет приоритет над selling)
        AddAggressionTags(tradeFlow, sentiment, all);

        // 9. Финансирование
        AddFundingTags(derivatives, all);

        // 10. RSI-экстремы на первичных ТФ
        if (primaryTfs.Count > 0)
            AddRsiTags(primaryTfs, all);

        // 11. Структура рынка на первичных ТФ (range-bound, neutral, weak-trend)
        if (primaryTfs.Count > 0)
            AddTimeframeStructureTags(primaryTfs, all);

        // 12. Кросс-сигналы (short covering / long unwinding)
        AddCrossSignalTags(all);

        // ── LOW PRIORITY ───────────────────────────────────────────────────────

        // 13. Близость к уровням
        if (primaryTfs.Count > 0)
            AddLevelProximityTags(primaryTfs, all);

        // 14. Перегрев моментума
        if (primaryTfs.Count > 0)
            AddOverextendedMomentumTag(primaryTfs, all);

        // 15. Скос ликвидности
        AddLiquiditySkewTags(orderBook, all);

        // 16. Перегрузка длинных/коротких позиций
        AddCrowdingTags(derivatives, all);

        return Deduplicate(all, MaxTags);
    }

    // ─── V1 rule helpers (сохранены для обратной совместимости тестов) ─────────

    /// <summary>
    /// Rule V1 4.1: только Trending → "trending" и Neutral → "neutral".
    /// Другие режимы — вне V1 whitelist.
    /// </summary>
    internal static string? GetRegimeTag(string regime) => regime switch
    {
        "Trending" => TagTrending,
        "Neutral" => TagNeutral,
        _ => null,
    };

    /// <summary>
    /// Rule V1 4.2: fundingRate &gt; 0 → "positive-funding"; &lt; 0 → "negative-funding"; == 0 → нет тега.
    /// </summary>
    internal static string? GetFundingTag(decimal fundingRate) =>
        fundingRate > 0m ? TagPositiveFunding :
        fundingRate < 0m ? TagNegativeFunding : null;

    /// <summary>
    /// Rule V1 4.3: ImbalanceTop5 &gt; threshold → "bid-pressure"; &lt; -threshold → "ask-pressure".
    /// </summary>
    internal static string? GetPressureTag(decimal imbalanceTop5) =>
        imbalanceTop5 > OrderBookPressureThreshold ? TagBidPressure :
        imbalanceTop5 < -OrderBookPressureThreshold ? TagAskPressure : null;

    /// <summary>
    /// Rule V1 4.4: buying имеет приоритет над selling при одновременном срабатывании.
    /// </summary>
    internal static string? GetAggressionTag(bool hasBuyPressure, bool hasSellPressure) =>
        hasBuyPressure ? TagAggressiveBuying :
        hasSellPressure ? TagAggressiveSelling : null;

    // ─── Private tag-group implementations ───────────────────────────────────

    /// <summary>V2 расширенный маппинг режима.</summary>
    private static void AddRegimeTags(string? marketRegime, List<string> target)
    {
        var normalized = marketRegime?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            target.Add(TagUnknownMarketRegime);
            return;
        }

        if (string.Equals(normalized, "Volatile", StringComparison.OrdinalIgnoreCase)) { target.Add(TagVolatileRegime); return; }
        if (string.Equals(normalized, "Bullish", StringComparison.OrdinalIgnoreCase)) { target.Add(TagBullishRegime); return; }
        if (string.Equals(normalized, "Bearish", StringComparison.OrdinalIgnoreCase)) { target.Add(TagBearishRegime); return; }
        if (string.Equals(normalized, "Neutral", StringComparison.OrdinalIgnoreCase)) { target.Add(TagNeutral); return; }
        if (string.Equals(normalized, "Trending", StringComparison.OrdinalIgnoreCase)) { target.Add(TagTrending); return; }
        target.Add(TagUnknownMarketRegime);
    }

    private static void AddFundingTags(DerivativesSnapshot derivatives, List<string> target)
    {
        var rate = derivatives.FundingRate;
        if (rate > NeutralFundingThreshold) { target.Add(TagPositiveFunding); return; }
        if (rate < -NeutralFundingThreshold) { target.Add(TagNegativeFunding); return; }
        target.Add(TagNeutralFunding);
    }

    private static void AddOrderBookTags(OrderBookSnapshot orderBook, SentimentSnapshot sentiment, List<string> target)
    {
        var imbalance = orderBook.ImbalanceTop5;
        var obScore = sentiment.OrderBookPressureScore;

        // bid vs ask — взаимоисключающие
        if (imbalance > OrderBookPressureThreshold || obScore > DirectionalPressureThreshold)
            target.Add(TagBidPressure);
        else if (imbalance < -OrderBookPressureThreshold || obScore < -DirectionalPressureThreshold)
            target.Add(TagAskPressure);

        // сильный дисбаланс
        if (Math.Abs(obScore) >= StrongOrderBookImbalanceThreshold)
            target.Add(TagStrongOrderBookImbalance);
    }

    private static void AddAggressionTags(TradeFlowSnapshot tradeFlow, SentimentSnapshot sentiment, List<string> target)
    {
        var tfScore = sentiment.TradeFlowPressureScore;
        var hasBuy = tradeFlow.HasAggressiveBuyPressure || tfScore > DirectionalPressureThreshold;
        var hasSell = tradeFlow.HasAggressiveSellPressure || tfScore < -DirectionalPressureThreshold;

        // buying имеет приоритет над selling
        if (hasBuy) target.Add(TagAggressiveBuying);
        else if (hasSell) target.Add(TagAggressiveSelling);
    }

    private static void AddOiTags(DerivativesSnapshot derivatives, List<string> target)
    {
        var oi1h = derivatives.OpenInterestChange1hPct;
        var oi4h = derivatives.OpenInterestChange4hPct;

        if (oi1h < 0m && oi4h < 0m) target.Add(TagOiDeclining);
        else if (oi1h > 0m && oi4h > 0m) target.Add(TagOiRising);
    }

    /// <summary>
    /// Добавляет кросс-сигнальные теги на основе уже накопленных тегов.
    /// Должна вызываться после AddAggressionTags и AddOiTags.
    /// </summary>
    private static void AddCrossSignalTags(List<string> accumulated)
    {
        var hasBuying = accumulated.Contains(TagAggressiveBuying, StringComparer.Ordinal);
        var hasSelling = accumulated.Contains(TagAggressiveSelling, StringComparer.Ordinal);
        var hasOiDeclining = accumulated.Contains(TagOiDeclining, StringComparer.Ordinal);

        if (hasOiDeclining)
        {
            if (hasBuying) accumulated.Add(TagPossibleShortCovering);
            if (hasSelling) accumulated.Add(TagPossibleLongUnwinding);
        }
    }

    private static void AddPrice24hTags(PriceSnapshot price, List<string> target)
    {
        if (price.High24h > 0m)
        {
            var distToHigh = (price.High24h - price.LastPrice) / price.High24h * 100m;
            if (distToHigh >= 0m && distToHigh <= Near24hProximityPct)
                target.Add(TagNear24hHigh);
        }

        if (price.Low24h > 0m)
        {
            var distToLow = (price.LastPrice - price.Low24h) / price.Low24h * 100m;
            if (distToLow >= 0m && distToLow <= Near24hProximityPct)
                target.Add(TagNear24hLow);
        }
    }

    private static void AddLowVolumeTags(List<TimeframeAnalysisSnapshot> tfs, List<string> target)
    {
        foreach (var tf in tfs)
        {
            if (tf.VolumeRatioIsReliable && tf.VolumeRatio < LowVolumeRatioThreshold)
            {
                target.Add(TagLowVolume);
                return;
            }
        }
    }

    private static void AddRsiTags(List<TimeframeAnalysisSnapshot> tfs, List<string> target)
    {
        var anyOverbought = false;
        var anyOversold = false;

        foreach (var tf in tfs)
        {
            if (tf.Rsi14IsReliable && tf.RsiOverbought) anyOverbought = true;
            if (tf.Rsi14IsReliable && tf.RsiOversold) anyOversold = true;
        }

        if (anyOverbought) target.Add(TagRsiOverbought);
        if (anyOversold) target.Add(TagRsiOversold);
    }

    private static void AddTimeframeStructureTags(List<TimeframeAnalysisSnapshot> tfs, List<string> target)
    {
        var neutralCount = 0;
        var weakTrendCount = 0;
        var anyRangeBound = false;

        foreach (var tf in tfs)
        {
            if (tf.Trend is MarketTrend.Sideways or MarketTrend.Unknown)
            {
                neutralCount++;
                anyRangeBound = true;
            }

            if (tf.TrendStrengthScore < WeakTrendThreshold)
                weakTrendCount++;
        }

        if (anyRangeBound) target.Add(TagRangeBound);
        if (neutralCount > tfs.Count / 2) target.Add(TagNeutralTimeframes);
        if (weakTrendCount > tfs.Count / 2) target.Add(TagWeakTrend);
    }

    private static void AddDirectionalNeutralRegimeTag(
        IReadOnlyList<TimeframeAnalysisSnapshot> tfs,
        string? marketRegime,
        List<string> target)
    {
        if (!string.Equals(marketRegime?.Trim(), "Neutral", StringComparison.OrdinalIgnoreCase))
            return;

        foreach (var tf in tfs)
        {
            if (tf.Trend is MarketTrend.Bullish or MarketTrend.Bearish)
            {
                target.Add(TagDirectionalTrendWithNeutralRegime);
                return;
            }
        }
    }

    private static void AddLevelProximityTags(List<TimeframeAnalysisSnapshot> tfs, List<string> target)
    {
        var anyNearResistance = false;
        var anyNearSupport = false;

        foreach (var tf in tfs)
        {
            if (tf.DistanceToResistance1Pct is { } dr && dr >= 0m && dr <= NearLevelThreshold)
                anyNearResistance = true;
            if (tf.DistanceToSupport1Pct is { } ds && ds >= 0m && ds <= NearLevelThreshold)
                anyNearSupport = true;
        }

        if (anyNearResistance) target.Add(TagNearResistance);
        if (anyNearSupport) target.Add(TagNearSupport);
    }

    private static void AddOverextendedMomentumTag(IReadOnlyList<TimeframeAnalysisSnapshot> tfs, List<string> target)
    {
        foreach (var tf in tfs)
        {
            var isOverextended =
                (tf.Trend == MarketTrend.Bullish && tf.Rsi14IsReliable && tf.RsiOverbought) ||
                (tf.Trend == MarketTrend.Bearish && tf.Rsi14IsReliable && tf.RsiOversold);

            if (isOverextended)
            {
                target.Add(TagOverextendedMomentum);
                return;
            }
        }
    }

    private static void AddLiquiditySkewTags(OrderBookSnapshot orderBook, List<string> target)
    {
        var bidVol = orderBook.TotalBidVolumeTop20;
        var askVol = orderBook.TotalAskVolumeTop20;

        if (bidVol == 0m && askVol == 0m) return;
        if (askVol == 0m && bidVol > 0m) { target.Add(TagLowerLiquidityHeavy); return; }
        if (bidVol == 0m && askVol > 0m) { target.Add(TagUpperLiquidityHeavy); return; }

        var ratio = bidVol / askVol;
        if (ratio >= 1m + LiquiditySkewThreshold) target.Add(TagLowerLiquidityHeavy);
        else if (ratio <= 1m - LiquiditySkewThreshold) target.Add(TagUpperLiquidityHeavy);
    }

    private static void AddCrowdingTags(DerivativesSnapshot derivatives, List<string> target)
    {
        if (derivatives.LongRatio > LongShortCrowdingThreshold) target.Add(TagLongCrowded);
        else if (derivatives.ShortRatio > LongShortCrowdingThreshold) target.Add(TagShortCrowded);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static List<TimeframeAnalysisSnapshot> GetNonNullTfs(
        TimeframeAnalysisSnapshot? m15,
        TimeframeAnalysisSnapshot? h1,
        TimeframeAnalysisSnapshot? h4)
    {
        var list = new List<TimeframeAnalysisSnapshot>(3);
        if (m15 is not null) list.Add(m15);
        if (h1 is not null) list.Add(h1);
        if (h4 is not null) list.Add(h4);
        return list;
    }

    private static void AddAll(List<string> target, IReadOnlyList<string> source)
    {
        foreach (var item in source)
            target.Add(item);
    }

    private static List<string> Deduplicate(List<string> all, int maxCount)
    {
        var result = new List<string>(Math.Min(all.Count, maxCount));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tag in all)
        {
            if (result.Count >= maxCount) break;
            if (seen.Add(tag)) result.Add(tag);
        }
        return result;
    }
}
