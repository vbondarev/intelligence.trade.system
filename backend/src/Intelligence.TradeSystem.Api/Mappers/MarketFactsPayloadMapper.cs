using System.Globalization;
using Intelligence.TradeSystem.Api.Models.MarketFacts;
using Intelligence.TradeSystem.Api.Models.Payloads;
using Intelligence.TradeSystem.MarketIntelligence.Analysis.Timeframes;

namespace Intelligence.TradeSystem.Api.Mappers;

/// <summary>
/// Extension-методы для построения <see cref="MarketFactsPayload"/> напрямую из
/// <see cref="MarketSnapshot"/>, <see cref="AnalysisMode"/> и <see cref="LlmSnapshotHealthPayload"/>.
/// Mapper детерминирован: не использует LLM и не добавляет интерпретаций.
/// </summary>
internal static class MarketFactsPayloadMapper
{
    /// <summary>
    /// Версия схемы source-снапшота (market snapshot assembly schema).
    /// Используется как <c>source.payloadSchemaVersion</c>.
    /// </summary>
    private const string SourceSchemaVersion = "1.0";

    private const decimal PressureLabelThreshold = 0.15m;
    private const decimal LiquiditySkewThreshold = 0.15m;
    private const int SpreadPctDecimalPlaces = 4;
    private const string LevelSourceV1 = "volume-profile";

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Строит <see cref="MarketFactsPayload"/> напрямую из снапшота, режима анализа и оценки здоровья.
    /// </summary>
    public static MarketFactsPayload ToMarketFacts(
        this MarketSnapshot snapshot,
        AnalysisMode mode,
        LlmSnapshotHealthPayload health)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(health);

        return new MarketFactsPayload
        {
            SchemaVersion = MarketFactsPayload.CurrentSchemaVersion,
            Source = BuildSource(snapshot),
            AnalysisContext = BuildAnalysisContext(mode),
            DataQuality = BuildDataQuality(health, snapshot),
            Price = BuildPrice(snapshot.Price),
            Derivatives = BuildDerivatives(snapshot.Derivatives),
            OrderBook = BuildOrderBook(snapshot.OrderBook),
            TradeFlow = BuildTradeFlow(snapshot.TradeFlow),
            Timeframes = BuildTimeframes(snapshot, health.IsFresh),
            Levels = BuildAggregatedLevels(snapshot),
            MarketInternalSentiment = BuildInternalSentiment(snapshot.Sentiment),
            Tags = [.. snapshot.Tags],
        };
    }

    // ─── Source ──────────────────────────────────────────────────────────────

    private static MarketFactsSourcePayload BuildSource(MarketSnapshot s) =>
        new()
        {
            // Source snapshot schema version (market snapshot assembly schema).
            PayloadSchemaVersion = SourceSchemaVersion,
            Exchange = s.Exchange,
            Symbol = s.Symbol,
            Category = s.Category,
            CapturedAtUtc = s.CapturedAtUtc,
        };

    // ─── AnalysisContext ─────────────────────────────────────────────────────

    private static MarketFactsAnalysisContextPayload BuildAnalysisContext(AnalysisMode mode) =>
        new()
        {
            AnalysisMode = mode.ToString(),
            PrimaryTimeframes = AnalysisModeDefaults.GetPrimaryTimeframes(mode),
        };

    // ─── DataQuality ─────────────────────────────────────────────────────────

    private static MarketFactsDataQualityPayload BuildDataQuality(
        LlmSnapshotHealthPayload health,
        MarketSnapshot snapshot) =>
        new()
        {
            Status = ResolveDataQualityStatus(health),
            IsFresh = health.IsFresh,
            IsPartial = health.IsPartial,
            Warnings = health.Warnings,
            MissingSections = health.MissingSections ?? [],
            SectionAgesMs = health.SectionAgesMs ?? new Dictionary<string, long>(),
            IndicatorDiagnostics = [.. snapshot.IndicatorDiagnostics.Select(d => new MarketFactsIndicatorDiagnosticPayload
            {
                Timeframe  = d.Timeframe,
                Indicator  = d.Indicator,
                Reason     = d.Reason,
                IsFallback = d.IsFallback,
                Message    = d.Message,
            })],
        };

    private static string ResolveDataQualityStatus(LlmSnapshotHealthPayload health)
    {
        // IsPartial takes precedence over IsFresh to reflect missing data above stale data.
        if (health.IsPartial) return "partial";
        if (!health.IsFresh) return "stale";
        return "ok";
    }

    // ─── Price ───────────────────────────────────────────────────────────────

    private static MarketFactsPricePayload BuildPrice(PriceSnapshot s) =>
        new()
        {
            LastPrice         = s.LastPrice,
            MarkPrice         = s.MarkPrice,
            IndexPrice        = s.IndexPrice,
            SpreadAbs         = s.SpreadAbs,
            SpreadPct         = s.SpreadPct,
            Price24hChangePct = s.Price24hChangePct,
            High24h           = s.High24h,
            Low24h            = s.Low24h,
            Volume24h         = s.Volume24h,
        };

    // ─── Derivatives ─────────────────────────────────────────────────────────

    private static MarketFactsDerivativesPayload BuildDerivatives(DerivativesSnapshot s) =>
        new()
        {
            FundingRate              = s.FundingRate,
            FundingRateAvg24h        = s.FundingRateAvg24h,
            NextFundingTimeUtc       = s.NextFundingTimeUtc,
            OpenInterest             = s.OpenInterest,
            OpenInterestValue        = s.OpenInterestValue,
            OpenInterestChange1hPct  = s.OpenInterestChange1hPct,
            OpenInterestChange4hPct  = s.OpenInterestChange4hPct,
            LongRatio                = s.LongRatio,
            ShortRatio               = s.ShortRatio,
            PremiumVsIndexPct        = s.PremiumVsIndexPct,
        };

    // ─── OrderBook ───────────────────────────────────────────────────────────

    private static MarketFactsOrderBookPayload BuildOrderBook(OrderBookSnapshot s)
    {
        var (spreadAbs, spreadPct) = ComputeSpread(s.BestBidPrice, s.BestAskPrice);

        return new MarketFactsOrderBookPayload
        {
            CapturedAtUtc         = s.CapturedAtUtc,
            BestBidPrice          = s.BestBidPrice,
            BestAskPrice          = s.BestAskPrice,
            SpreadAbs             = spreadAbs,
            SpreadPct             = spreadPct,
            TotalBidVolumeTop5    = s.TotalBidVolumeTop5,
            TotalAskVolumeTop5    = s.TotalAskVolumeTop5,
            TotalBidVolumeTop10   = s.TotalBidVolumeTop10,
            TotalAskVolumeTop10   = s.TotalAskVolumeTop10,
            TotalBidVolumeTop20   = s.TotalBidVolumeTop20,
            TotalAskVolumeTop20   = s.TotalAskVolumeTop20,
            ImbalanceTop5         = s.ImbalanceTop5,
            ImbalanceTop10        = s.ImbalanceTop10,
            ImbalanceTop20        = s.ImbalanceTop20,
            BidWalls              = [.. s.BidWalls.Select(MapLiquidityWall)],
            AskWalls              = [.. s.AskWalls.Select(MapLiquidityWall)],
            PressureLabel         = ComputePressureLabel(s.ImbalanceTop10).ToString(),
            LiquiditySkewLabel    = ComputeLiquiditySkewLabel(s.TotalBidVolumeTop20, s.TotalAskVolumeTop20).ToString(),
        };
    }

    private static MarketFactsLiquidityWallPayload MapLiquidityWall(LiquidityWall w) =>
        new()
        {
            Price                 = w.Price,
            Size                  = w.Size,
            DistancePctFromMarket = w.DistancePctFromMarket,
        };

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
        if (imbalanceTop10 > PressureLabelThreshold)  return PressureLabel.BidDominant;
        if (imbalanceTop10 < -PressureLabelThreshold) return PressureLabel.AskDominant;
        return PressureLabel.Balanced;
    }

    private static LiquiditySkewLabel ComputeLiquiditySkewLabel(decimal bidVolTop20, decimal askVolTop20)
    {
        if (bidVolTop20 == 0 && askVolTop20 == 0) return LiquiditySkewLabel.Balanced;
        if (askVolTop20 == 0 && bidVolTop20 > 0)  return LiquiditySkewLabel.LowerLiquidityHeavy;
        if (bidVolTop20 == 0 && askVolTop20 > 0)  return LiquiditySkewLabel.UpperLiquidityHeavy;

        var ratio = bidVolTop20 / askVolTop20;
        if (ratio >= 1m + LiquiditySkewThreshold) return LiquiditySkewLabel.LowerLiquidityHeavy;
        if (ratio <= 1m - LiquiditySkewThreshold) return LiquiditySkewLabel.UpperLiquidityHeavy;
        return LiquiditySkewLabel.Balanced;
    }

    // ─── TradeFlow ───────────────────────────────────────────────────────────

    private static MarketFactsTradeFlowPayload BuildTradeFlow(TradeFlowSnapshot s) =>
        new()
        {
            WindowStartUtc              = s.WindowStartUtc,
            WindowEndUtc                = s.WindowEndUtc,
            BuyVolume                   = s.BuyVolume,
            SellVolume                  = s.SellVolume,
            DeltaVolume                 = s.DeltaVolume,
            DeltaPct                    = s.DeltaPct,
            BuyTrades                   = s.BuyTrades,
            SellTrades                  = s.SellTrades,
            AvgTradeSize                = s.AvgTradeSize,
            MaxTradeSize                = s.MaxTradeSize,
            HasAggressiveBuyPressure    = s.HasAggressiveBuyPressure,
            HasAggressiveSellPressure   = s.HasAggressiveSellPressure,
            Direction                   = ResolveTradeFlowDirection(s.BuyVolume, s.SellVolume),
            Label                       = ResolveTradeFlowLabel(s.HasAggressiveBuyPressure, s.HasAggressiveSellPressure),
        };

    private static string ResolveTradeFlowDirection(decimal buyVolume, decimal sellVolume)
    {
        if (buyVolume > sellVolume)  return "buy_dominant";
        if (sellVolume > buyVolume)  return "sell_dominant";
        return "neutral";
    }

    private static string ResolveTradeFlowLabel(bool hasAggressiveBuy, bool hasAggressiveSell)
    {
        // Aggressive pressure flags take priority over delta-based label.
        if (hasAggressiveBuy && hasAggressiveSell) return "mixed_aggressive_pressure";
        if (hasAggressiveBuy)                      return "aggressive_buying";
        if (hasAggressiveSell)                     return "aggressive_selling";
        return "neutral";
    }

    // ─── Timeframes ──────────────────────────────────────────────────────────

    private static Dictionary<string, MarketFactsTimeframePayload> BuildTimeframes(
        MarketSnapshot snapshot,
        bool snapshotIsFresh)
    {
        var regime = snapshot.Sentiment.MarketRegime;

        return new Dictionary<string, MarketFactsTimeframePayload>
        {
            ["15m"] = BuildTimeframePayload(snapshot.M15, snapshotIsFresh, regime, snapshot.H1, snapshot.H4),
            ["1h"]  = BuildTimeframePayload(snapshot.H1, snapshotIsFresh, regime, snapshot.H4, snapshot.D1),
            ["4h"]  = BuildTimeframePayload(snapshot.H4, snapshotIsFresh, regime, snapshot.D1),
            ["1d"]  = BuildTimeframePayload(snapshot.D1, snapshotIsFresh, regime),
        };
    }

    private static MarketFactsTimeframePayload BuildTimeframePayload(
        TimeframeAnalysisSnapshot s,
        bool snapshotIsFresh,
        string? marketRegime,
        params TimeframeAnalysisSnapshot[] higherTfs)
    {
        var bias                  = PrecomputeBias(s);
        var higherTfOppositeLevel = ResolveHigherTfOppositeLevel(bias, higherTfs);
        var summary               = TimeframeSummaryBuilder.Build(s, snapshotIsFresh, marketRegime, higherTfOppositeLevel);
        var lastClose             = s.LastCandle.Close;

        return new MarketFactsTimeframePayload
        {
            Timeframe  = s.Timeframe,
            Trend      = BuildTrend(s, summary),
            Indicators = BuildIndicators(s),
            Levels     = BuildTimeframeLevels(s, lastClose),
            DerivedFlags   = BuildDerivedFlags(s),
            BackendSummary = BuildBackendSummary(summary),
        };
    }

    private static MarketFactsTimeframeTrendPayload BuildTrend(
        TimeframeAnalysisSnapshot s,
        TimeframeSummary summary) =>
        new()
        {
            Trend              = s.Trend.ToString(),
            TrendCode          = MapTrendCode(s.Trend).ToString(CultureInfo.InvariantCulture),
            TrendStrengthScore = s.TrendStrengthScore,
            TrendStrengthLabel = summary.TrendStrengthLabel.ToString(),
        };

    private static MarketFactsTimeframeIndicatorsPayload BuildIndicators(TimeframeAnalysisSnapshot s) =>
        new()
        {
            Ema20          = s.Ema20,
            Ema50          = s.Ema50,
            Ema200         = s.Ema200,
            Rsi14          = s.Rsi14,
            Rsi14IsReliable = s.Rsi14IsReliable,
            Atr14          = s.Atr14,
            VolumeRatio    = s.VolumeRatio,
        };

    private static MarketFactsTimeframeLevelsPayload BuildTimeframeLevels(
        TimeframeAnalysisSnapshot s,
        decimal lastClose) =>
        new()
        {
            Support1                 = s.Support1,
            Support2                 = s.Support2,
            Resistance1              = s.Resistance1,
            Resistance2              = s.Resistance2,
            DistanceToSupport1Pct    = s.DistanceToSupport1Pct,
            DistanceToResistance1Pct = s.DistanceToResistance1Pct,
            Support1Meta             = BuildLevelMeta(s.Support1, s.Support1Strength, s.Support1ClusterVolume, lastClose, isSupport: true, distancePct: s.DistanceToSupport1Pct),
            Support2Meta             = BuildLevelMeta(s.Support2, s.Support2Strength, s.Support2ClusterVolume, lastClose, isSupport: true),
            Resistance1Meta          = BuildLevelMeta(s.Resistance1, s.Resistance1Strength, s.Resistance1ClusterVolume, lastClose, isSupport: false, distancePct: s.DistanceToResistance1Pct),
            Resistance2Meta          = BuildLevelMeta(s.Resistance2, s.Resistance2Strength, s.Resistance2ClusterVolume, lastClose, isSupport: false),
        };

    private static MarketFactsLevelMetaPayload? BuildLevelMeta(
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

        if (distancePct is { } provided)
        {
            distance = provided;
        }
        else if (lastClose > 0m)
        {
            distance = isSupport
                ? Math.Round((lastClose - price) / lastClose * 100m, 4)
                : Math.Round((price - lastClose) / lastClose * 100m, 4);

            if (distance < 0m)
                distance = null;
        }

        return new MarketFactsLevelMetaPayload
        {
            Price         = price,
            Strength      = strength,
            StrengthLabel = LevelStrengthLabelMapper.Map(strength).ToString(),
            Source        = LevelSourceV1,
            DistancePct   = distance,
            ClusterVolume = clusterVolume,
        };
    }

    private static MarketFactsTimeframeDerivedFlagsPayload BuildDerivedFlags(TimeframeAnalysisSnapshot s) =>
        new()
        {
            IsAboveEma20        = s.IsAboveEma20,
            IsAboveEma50        = s.IsAboveEma50,
            IsAboveEma200       = s.IsAboveEma200,
            EmaBullishAlignment = s.EmaBullishAlignment,
            EmaBearishAlignment = s.EmaBearishAlignment,
            RsiOverbought       = s.RsiOverbought,
            RsiOversold         = s.RsiOversold,
        };

    private static MarketFactsTimeframeBackendSummaryPayload BuildBackendSummary(
        TimeframeSummary r) =>
        new()
        {
            Bias             = r.Bias.ToString(),
            IsTrendConfirmed = r.IsTrendConfirmed,
            MomentumState    = r.MomentumState.ToString(),
            EntryQuality     = r.EntryQuality.ToString(),
            RiskFlags        = [.. r.RiskFlags],
        };

    /// <summary>
    /// Числовой код тренда по контракту API: Unknown=0, Bullish=1, Bearish=2, Sideways=3.
    /// Enum <see cref="MarketTrend"/> намеренно несёт те же numeric-значения.
    /// </summary>
    private static int MapTrendCode(MarketTrend trend) => (int)trend;

    // ─── Aggregated Levels ───────────────────────────────────────────────────

    private static MarketFactsLevelsPayload BuildAggregatedLevels(MarketSnapshot snapshot)
    {
        var timeframes = new[]
        {
            snapshot.M15,
            snapshot.H1,
            snapshot.H4,
            snapshot.D1,
        };

        var supports    = new List<MarketFactsAggregatedLevelPayload>();
        var resistances = new List<MarketFactsAggregatedLevelPayload>();

        foreach (var tf in timeframes)
        {
            AddAggregatedLevel(supports, tf.Timeframe, 1, "support",
                tf.Support1, tf.Support1Strength, tf.Support1ClusterVolume, tf.DistanceToSupport1Pct);

            AddAggregatedLevel(supports, tf.Timeframe, 2, "support",
                tf.Support2, tf.Support2Strength, tf.Support2ClusterVolume, distancePct: null);

            AddAggregatedLevel(resistances, tf.Timeframe, 1, "resistance",
                tf.Resistance1, tf.Resistance1Strength, tf.Resistance1ClusterVolume, tf.DistanceToResistance1Pct);

            AddAggregatedLevel(resistances, tf.Timeframe, 2, "resistance",
                tf.Resistance2, tf.Resistance2Strength, tf.Resistance2ClusterVolume, distancePct: null);
        }

        return new MarketFactsLevelsPayload
        {
            Supports    = supports,
            Resistances = resistances,
        };
    }

    private static void AddAggregatedLevel(
        List<MarketFactsAggregatedLevelPayload> list,
        string timeframe,
        int rank,
        string kind,
        decimal? price,
        decimal? strength,
        decimal? clusterVolume,
        decimal? distancePct)
    {
        // Skip absent levels (no price means the level was not detected).
        if (price is null)
            return;

        list.Add(new MarketFactsAggregatedLevelPayload
        {
            Timeframe     = timeframe,
            Rank          = rank,
            Kind          = kind,
            Price         = price,
            Strength      = strength,
            StrengthLabel = LevelStrengthLabelMapper.Map(strength).ToString(),
            Source        = LevelSourceV1,
            DistancePct   = distancePct,
            ClusterVolume = clusterVolume,
        });
    }

    // ─── MarketInternalSentiment ─────────────────────────────────────────────

    private static MarketFactsInternalSentimentPayload BuildInternalSentiment(SentimentSnapshot s) =>
        new()
        {
            LongShortBiasScore       = s.LongShortBiasScore,
            FundingBiasScore         = s.FundingBiasScore,
            OrderBookPressureScore   = s.OrderBookPressureScore,
            TradeFlowPressureScore   = s.TradeFlowPressureScore,
            MarketRegime             = s.MarketRegime,
        };

    // ─── Higher-TF bias / level helpers ─────────────────────────────────────

    /// <summary>
    /// Pre-computes bias from snapshot fields using the same deterministic rule as
    /// <see cref="TimeframeSummaryBuilder"/> so the correct higher-TF obstacle level
    /// can be selected before calling Build.
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
    /// For <see cref="TimeframeBias.Bullish"/> — nearest Resistance1 above price.
    /// For <see cref="TimeframeBias.Bearish"/> — nearest Support1 below price.
    /// Negative distance means wrong side of price and is ignored.
    /// </summary>
    private static NearestOppositeLevel? ResolveHigherTfOppositeLevel(
        TimeframeBias bias,
        TimeframeAnalysisSnapshot[] higherTfs)
    {
        if (bias == TimeframeBias.Neutral || higherTfs.Length == 0) return null;

        NearestOppositeLevel? best = null;
        foreach (var htf in higherTfs)
        {
            var (dist, strength) = bias == TimeframeBias.Bullish
                ? (htf.DistanceToResistance1Pct, htf.Resistance1Strength)
                : (htf.DistanceToSupport1Pct, htf.Support1Strength);

            if (dist is null or < 0m) continue;

            var candidate = new NearestOppositeLevel(dist.Value, strength);
            if (best is null || dist.Value < best.DistancePct)
                best = candidate;
        }

        return best;
    }
}
