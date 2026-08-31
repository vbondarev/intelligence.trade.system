using Intelligence.TradeSystem.Api.Models.Payloads;
using Intelligence.TradeSystem.MarketIntelligence.Analysis.Timeframes;

namespace Intelligence.TradeSystem.Api.Mappers;

/// <summary>
/// Extension-методы для преобразования <see cref="MarketAnalysisSnapshot"/> в <see cref="LlmMarketAnalysisPayload"/>.
/// </summary>
internal static class LlmPayloadMapperExtensions
{
    private const string SchemaVersion = "1.0";
    private const decimal PressureLabelThreshold = 0.15m;
    private const decimal LiquiditySkewThreshold = 0.15m;  // ±15% → ratio >=1.15 or <=0.85
    private const int SpreadPctDecimalPlaces = 4;

    /// <summary>
    /// Преобразует снапшот в LLM-оптимизированный payload.
    /// </summary>
    public static LlmMarketAnalysisPayload ToLlmPayload(
        this MarketAnalysisSnapshot snapshot,
        AnalysisMode mode,
        LlmSnapshotHealthPayload health)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(health);

        // Строим TF-payload вместе с summary для последующего обогащения тегов.
        var (m15Payload, m15Summary) = BuildTimeframeWithResult(snapshot.M15, health.IsFresh,
            snapshot.Sentiment.MarketRegime, snapshot.H1, snapshot.H4);
        var (h1Payload, h1Summary) = BuildTimeframeWithResult(snapshot.H1, health.IsFresh,
            snapshot.Sentiment.MarketRegime, snapshot.H4, snapshot.D1);
        var (h4Payload, h4Summary) = BuildTimeframeWithResult(snapshot.H4, health.IsFresh,
            snapshot.Sentiment.MarketRegime, snapshot.D1);
        var (d1Payload, d1Summary) = BuildTimeframeWithResult(snapshot.D1, health.IsFresh,
            snapshot.Sentiment.MarketRegime);

        var enrichedTags = LlmTagEnricher.Enrich(
            snapshot.Tags, health, m15Summary, h1Summary, h4Summary, d1Summary, mode);

        return new LlmMarketAnalysisPayload
        {
            SchemaVersion = SchemaVersion,
            Exchange = snapshot.Exchange,
            Symbol = snapshot.Symbol,
            Category = snapshot.Category,
            CapturedAtUtc = snapshot.CapturedAtUtc,
            AnalysisContext = BuildAnalysisContext(mode),
            SnapshotHealth = health,
            Price = BuildPrice(snapshot.Price),
            Derivatives = BuildDerivatives(snapshot.Derivatives),
            OrderBook = BuildOrderBook(snapshot.OrderBook),
            TradeFlow = BuildTradeFlow(snapshot.TradeFlow),
            M15 = m15Payload,
            H1 = h1Payload,
            H4 = h4Payload,
            D1 = d1Payload,
            Sentiment = BuildSentiment(snapshot.Sentiment),
            Tags = [.. enrichedTags],
            IndicatorDiagnostics = [.. snapshot.IndicatorDiagnostics.Select(d => new LlmIndicatorDiagnosticPayload
            {
                Timeframe  = d.Timeframe,
                Indicator  = d.Indicator,
                Reason     = d.Reason,
                IsFallback = d.IsFallback,
                Message    = d.Message,
            })],
        };
    }

    // ─── AnalysisContext ────────────────────────────────────────────────────

    private static LlmAnalysisContextPayload BuildAnalysisContext(AnalysisMode mode) =>
        new()
        {
            AnalysisMode = mode.ToString(),
            PrimaryTimeframes = AnalysisModeDefaults.GetPrimaryTimeframes(mode),
        };

    // ─── Price ──────────────────────────────────────────────────────────────

    private static LlmPricePayload BuildPrice(PriceSnapshot s) =>
        new()
        {
            LastPrice = s.LastPrice,
            MarkPrice = s.MarkPrice,
            IndexPrice = s.IndexPrice,
            SpreadAbs = s.SpreadAbs,
            SpreadPct = s.SpreadPct,
            Price24hChangePct = s.Price24hChangePct,
            High24h = s.High24h,
            Low24h = s.Low24h,
            Volume24h = s.Volume24h,
        };

    // ─── Derivatives ────────────────────────────────────────────────────────

    private static LlmDerivativesPayload BuildDerivatives(DerivativesSnapshot s) =>
        new()
        {
            FundingRate = s.FundingRate,
            FundingRateAvg24h = s.FundingRateAvg24h,
            NextFundingTimeUtc = s.NextFundingTimeUtc,
            OpenInterest = s.OpenInterest,
            OpenInterestValue = s.OpenInterestValue,
            OpenInterestChange1hPct = s.OpenInterestChange1hPct,
            OpenInterestChange4hPct = s.OpenInterestChange4hPct,
            LongRatio = s.LongRatio,
            ShortRatio = s.ShortRatio,
            PremiumVsIndexPct = s.PremiumVsIndexPct,
        };

    // ─── OrderBook ──────────────────────────────────────────────────────────

    private static LlmOrderBookPayload BuildOrderBook(OrderBookSnapshot s)
    {
        var (spreadAbs, spreadPct) = ComputeSpread(s.BestBidPrice, s.BestAskPrice);

        return new LlmOrderBookPayload
        {
            CapturedAtUtc = s.CapturedAtUtc,
            BestBidPrice = s.BestBidPrice,
            BestAskPrice = s.BestAskPrice,
            SpreadAbs = spreadAbs,
            SpreadPct = spreadPct,
            TotalBidVolumeTop5 = s.TotalBidVolumeTop5,
            TotalAskVolumeTop5 = s.TotalAskVolumeTop5,
            TotalBidVolumeTop10 = s.TotalBidVolumeTop10,
            TotalAskVolumeTop10 = s.TotalAskVolumeTop10,
            TotalBidVolumeTop20 = s.TotalBidVolumeTop20,
            TotalAskVolumeTop20 = s.TotalAskVolumeTop20,
            ImbalanceTop5 = s.ImbalanceTop5,
            ImbalanceTop10 = s.ImbalanceTop10,
            ImbalanceTop20 = s.ImbalanceTop20,
            BidWalls = [.. s.BidWalls.Select(w => new LlmLiquidityWallPayload { Price = w.Price, Size = w.Size, DistancePctFromMarket = w.DistancePctFromMarket })],
            AskWalls = [.. s.AskWalls.Select(w => new LlmLiquidityWallPayload { Price = w.Price, Size = w.Size, DistancePctFromMarket = w.DistancePctFromMarket })],
            PressureLabel = ComputePressureLabel(s.ImbalanceTop10).ToString(),
            LiquiditySkewLabel = ComputeLiquiditySkewLabel(s.TotalBidVolumeTop20, s.TotalAskVolumeTop20).ToString(),
        };
    }

    private static (decimal spreadAbs, decimal spreadPct) ComputeSpread(decimal bid, decimal ask)
    {
        if (bid <= 0 || ask <= 0 || ask < bid)
            return (0m, 0m);

        var spreadAbs = ask - bid;
        var midPrice = (ask + bid) / 2m;
        var spreadPct = midPrice == 0 ? 0m : Math.Round(spreadAbs / midPrice * 100m, SpreadPctDecimalPlaces);

        return (spreadAbs, spreadPct);
    }

    private static PressureLabel ComputePressureLabel(decimal imbalanceTop10)
    {
        if (imbalanceTop10 > PressureLabelThreshold) return PressureLabel.BidDominant;
        if (imbalanceTop10 < -PressureLabelThreshold) return PressureLabel.AskDominant;
        return PressureLabel.Balanced;
    }

    private static LiquiditySkewLabel ComputeLiquiditySkewLabel(decimal bidVolTop20, decimal askVolTop20)
    {
        if (bidVolTop20 == 0 && askVolTop20 == 0) return LiquiditySkewLabel.Balanced;
        if (askVolTop20 == 0 && bidVolTop20 > 0) return LiquiditySkewLabel.LowerLiquidityHeavy;
        if (bidVolTop20 == 0 && askVolTop20 > 0) return LiquiditySkewLabel.UpperLiquidityHeavy;

        var ratio = bidVolTop20 / askVolTop20;
        if (ratio >= 1m + LiquiditySkewThreshold) return LiquiditySkewLabel.LowerLiquidityHeavy;
        if (ratio <= 1m - LiquiditySkewThreshold) return LiquiditySkewLabel.UpperLiquidityHeavy;
        return LiquiditySkewLabel.Balanced;
    }

    // ─── TradeFlow ──────────────────────────────────────────────────────────

    private static LlmTradeFlowPayload BuildTradeFlow(TradeFlowSnapshot s) =>
        new()
        {
            WindowStartUtc = s.WindowStartUtc,
            WindowEndUtc = s.WindowEndUtc,
            BuyVolume = s.BuyVolume,
            SellVolume = s.SellVolume,
            DeltaVolume = s.DeltaVolume,
            DeltaPct = s.DeltaPct,
            BuyTrades = s.BuyTrades,
            SellTrades = s.SellTrades,
            AvgTradeSize = s.AvgTradeSize,
            MaxTradeSize = s.MaxTradeSize,
            HasAggressiveBuyPressure = s.HasAggressiveBuyPressure,
            HasAggressiveSellPressure = s.HasAggressiveSellPressure,
        };

    // ─── Timeframe ──────────────────────────────────────────────────────────

    private const string LevelSourceV1 = "volume-profile";

    /// <summary>
    /// Строит TF-payload и возвращает также summary-результат для использования в LlmTagEnricher.
    /// </summary>
    private static (LlmTimeframePayload Payload, TimeframeSummary Summary) BuildTimeframeWithResult(
        TimeframeAnalysisSnapshot s, bool snapshotIsFresh, string? marketRegime,
        params TimeframeAnalysisSnapshot[] higherTfs)
    {
        var bias = PrecomputeBias(s);
        var higherTfOppositeLevel = ResolveHigherTfOppositeLevel(bias, higherTfs);
        var r = TimeframeSummaryBuilder.Build(s, snapshotIsFresh, marketRegime, higherTfOppositeLevel);
        return (BuildTimeframePayload(s, r), r);
    }

    private static LlmTimeframePayload BuildTimeframe(
        TimeframeAnalysisSnapshot s, bool snapshotIsFresh, string? marketRegime,
        params TimeframeAnalysisSnapshot[] higherTfs)
        => BuildTimeframeWithResult(s, snapshotIsFresh, marketRegime, higherTfs).Payload;

    private static LlmTimeframePayload BuildTimeframePayload(TimeframeAnalysisSnapshot s, TimeframeSummary r)
    {
        var lastClose = s.LastCandle.Close;

        return new LlmTimeframePayload
        {
            Timeframe = s.Timeframe,
            Trend = s.Trend.ToString(),
            TrendCode = MapTrendCode(s.Trend),
            TrendStrengthScore = s.TrendStrengthScore,
            TrendStrengthLabel = r.TrendStrengthLabel.ToString(),
            Ema20 = s.Ema20,
            Ema50 = s.Ema50,
            Ema200 = s.Ema200,
            Rsi14 = s.Rsi14,
            Rsi14IsReliable = s.Rsi14IsReliable,
            Atr14 = s.Atr14,
            VolumeRatio = s.VolumeRatio,
            Support1 = s.Support1,
            Support2 = s.Support2,
            Resistance1 = s.Resistance1,
            Resistance2 = s.Resistance2,
            DistanceToSupport1Pct = s.DistanceToSupport1Pct,
            DistanceToResistance1Pct = s.DistanceToResistance1Pct,
            Support1Meta = BuildLevelMeta(s.Support1, s.Support1Strength, s.Support1ClusterVolume, lastClose, isSupport: true, distancePct: s.DistanceToSupport1Pct),
            Support2Meta = BuildLevelMeta(s.Support2, s.Support2Strength, s.Support2ClusterVolume, lastClose, isSupport: true),
            Resistance1Meta = BuildLevelMeta(s.Resistance1, s.Resistance1Strength, s.Resistance1ClusterVolume, lastClose, isSupport: false, distancePct: s.DistanceToResistance1Pct),
            Resistance2Meta = BuildLevelMeta(s.Resistance2, s.Resistance2Strength, s.Resistance2ClusterVolume, lastClose, isSupport: false),
            IsAboveEma20 = s.IsAboveEma20,
            IsAboveEma50 = s.IsAboveEma50,
            IsAboveEma200 = s.IsAboveEma200,
            EmaBullishAlignment = s.EmaBullishAlignment,
            EmaBearishAlignment = s.EmaBearishAlignment,
            RsiOverbought = s.RsiOverbought,
            RsiOversold = s.RsiOversold,
            Summary = new LlmTimeframeSummaryPayload
            {
                Bias = r.Bias.ToString(),
                IsTrendConfirmed = r.IsTrendConfirmed,
                MomentumState = r.MomentumState.ToString(),
                EntryQuality = r.EntryQuality.ToString(),
                RiskFlags = [.. r.RiskFlags],
            },
        };
    }

    /// <summary>
    /// Строит метаданные ценового уровня поддержки или сопротивления.
    /// Возвращает <c>null</c>, если уровень равен <c>null</c> (не обнаружен).
    /// </summary>
    private static LlmLevelMetaPayload? BuildLevelMeta(
        decimal? levelPrice,
        decimal? strength,
        decimal? clusterVolume,
        decimal lastClose,
        bool isSupport,
        decimal? distancePct = null)
    {
        if (levelPrice is not { } price)
            return null;

        decimal? distance = null;

        if (distancePct is { } providedDistance)
        {
            distance = providedDistance;
        }
        else if (lastClose > 0m)
        {
            distance = isSupport
                ? Math.Round((lastClose - price) / lastClose * 100m, 4)
                : Math.Round((price - lastClose) / lastClose * 100m, 4);

            if (distance < 0m)
                distance = null;
        }

        return new LlmLevelMetaPayload
        {
            Price = price,
            Strength = strength,
            StrengthLabel = LevelStrengthLabelMapper.Map(strength).ToString(),
            Source = LevelSourceV1,
            DistancePct = distance,
            ClusterVolume = clusterVolume,
        };
    }

    // ─── Sentiment ──────────────────────────────────────────────────────────

    /// <summary>
    /// Числовой код тренда по контракту API: Unknown=0, Bullish=1, Bearish=2, Sideways=3.
    /// Enum <see cref="MarketTrend"/> намеренно несёт те же numeric-значения.
    /// </summary>
    private static int MapTrendCode(MarketTrend trend) => (int)trend;

    private static LlmSentimentPayload BuildSentiment(SentimentSnapshot s) =>
        new()
        {
            LongShortBiasScore = s.LongShortBiasScore,
            FundingBiasScore = s.FundingBiasScore,
            OrderBookPressureScore = s.OrderBookPressureScore,
            TradeFlowPressureScore = s.TradeFlowPressureScore,
            MarketRegime = s.MarketRegime,
        };


    // ─── Higher-TF level resolution ─────────────────────────────────────────

    /// <summary>
    /// Pre-computes bias from snapshot fields using the same deterministic rule as
    /// <see cref="TimeframeSummaryBuilder"/> so the correct kind of higher-TF
    /// obstacle level can be selected (resistance for Bullish, support for Bearish)
    /// before calling Build.
    /// </summary>
    private static TimeframeBias PrecomputeBias(TimeframeAnalysisSnapshot s) =>
        s.Trend switch
        {
            MarketTrend.Bullish when s.EmaBullishAlignment => TimeframeBias.Bullish,
            MarketTrend.Bearish when s.EmaBearishAlignment => TimeframeBias.Bearish,
            _ => TimeframeBias.Neutral,
        };

    /// <summary>
    /// Returns the nearest relevant opposite level from the supplied higher-timeframe
    /// snapshots that acts as a potential obstacle for the given bias direction.
    /// <para>
    /// For <see cref="TimeframeBias.Bullish"/> — nearest Resistance1 above price.<br/>
    /// For <see cref="TimeframeBias.Bearish"/> — nearest Support1 below price.
    /// </para>
    /// Only levels with a non-negative distance are considered.
    /// A negative distance means the level is on the wrong side of price and is ignored.
    /// Distance == 0 is valid and represents an obstacle exactly at the current price.
    /// A null distance means the level is absent — ignored.
    /// </summary>
    private static NearestOppositeLevel? ResolveHigherTfOppositeLevel(
        TimeframeBias bias, TimeframeAnalysisSnapshot[] higherTfs)
    {
        if (bias == TimeframeBias.Neutral || higherTfs.Length == 0) return null;

        NearestOppositeLevel? best = null;
        foreach (var htf in higherTfs)
        {
            var (dist, strength) = bias == TimeframeBias.Bullish
                ? (htf.DistanceToResistance1Pct, htf.Resistance1Strength)
                : (htf.DistanceToSupport1Pct, htf.Support1Strength);

            // Skip if level is absent or on the wrong side of the price.
            if (dist is null or < 0m) continue;

            var candidate = new NearestOppositeLevel(dist.Value, strength);
            if (best is null || dist.Value < best.DistancePct)
                best = candidate;
        }

        return best;
    }
}
