using Intelligence.TradeSystem.Api.Models.Payloads;
using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Api.Mappers;

/// <summary>
/// Extension-методы для преобразования <see cref="MarketAnalysisSnapshot"/> в <see cref="LlmMarketAnalysisPayload"/>.
/// </summary>
internal static class LlmPayloadMapperExtensions
{
    private const string SchemaVersion          = "1.0";
    private const decimal PressureLabelThreshold = 0.15m;
    private const decimal LiquiditySkewThreshold = 0.15m;  // ±15% → ratio >=1.15 or <=0.85
    private const int     SpreadPctDecimalPlaces  = 4;

    /// <summary>
    /// Преобразует снапшот в LLM-оптимизированный payload.
    /// </summary>
    public static LlmMarketAnalysisPayload ToLlmPayload(
        this MarketAnalysisSnapshot snapshot,
        AnalysisMode mode,
        bool includePortfolio,
        LlmSnapshotHealthPayload health)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(health);

        return new LlmMarketAnalysisPayload
        {
            SchemaVersion   = SchemaVersion,
            Exchange        = snapshot.Exchange,
            Symbol          = snapshot.Symbol,
            Category        = snapshot.Category,
            CapturedAtUtc   = snapshot.CapturedAtUtc,
            AnalysisContext = BuildAnalysisContext(mode, includePortfolio),
            SnapshotHealth  = health,
            Price           = BuildPrice(snapshot.Price),
            Derivatives     = BuildDerivatives(snapshot.Derivatives),
            OrderBook       = BuildOrderBook(snapshot.OrderBook),
            TradeFlow       = BuildTradeFlow(snapshot.TradeFlow),
            M15             = BuildTimeframe(snapshot.M15),
            H1              = BuildTimeframe(snapshot.H1),
            H4              = BuildTimeframe(snapshot.H4),
            D1              = BuildTimeframe(snapshot.D1),
            Sentiment       = BuildSentiment(snapshot.Sentiment),
            Tags            = [.. snapshot.Tags],
            Portfolio       = includePortfolio ? BuildPortfolio(snapshot.Portfolio) : null,
            AggregatedContext = null,
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

    private static LlmAnalysisContextPayload BuildAnalysisContext(AnalysisMode mode, bool includePortfolio) =>
        new()
        {
            AnalysisMode         = mode.ToString(),
            PrimaryTimeframes    = AnalysisModeDefaults.GetPrimaryTimeframes(mode),
            UsesPortfolioContext = includePortfolio,
            UsesAggregatedContext = false,
        };

    // ─── Price ──────────────────────────────────────────────────────────────

    private static LlmPricePayload BuildPrice(PriceSnapshot s) =>
        new()
        {
            LastPrice          = s.LastPrice,
            MarkPrice          = s.MarkPrice,
            IndexPrice         = s.IndexPrice,
            SpreadAbs          = s.SpreadAbs,
            SpreadPct          = s.SpreadPct,
            Price24hChangePct  = s.Price24hChangePct,
            High24h            = s.High24h,
            Low24h             = s.Low24h,
            Volume24h          = s.Volume24h,
        };

    // ─── Derivatives ────────────────────────────────────────────────────────

    private static LlmDerivativesPayload BuildDerivatives(DerivativesSnapshot s) =>
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

    // ─── OrderBook ──────────────────────────────────────────────────────────

    private static LlmOrderBookPayload BuildOrderBook(OrderBookSnapshot s)
    {
        var (spreadAbs, spreadPct) = ComputeSpread(s.BestBidPrice, s.BestAskPrice);

        return new LlmOrderBookPayload
        {
            CapturedAtUtc        = s.CapturedAtUtc,
            BestBidPrice         = s.BestBidPrice,
            BestAskPrice         = s.BestAskPrice,
            SpreadAbs            = spreadAbs,
            SpreadPct            = spreadPct,
            TotalBidVolumeTop5   = s.TotalBidVolumeTop5,
            TotalAskVolumeTop5   = s.TotalAskVolumeTop5,
            TotalBidVolumeTop10  = s.TotalBidVolumeTop10,
            TotalAskVolumeTop10  = s.TotalAskVolumeTop10,
            TotalBidVolumeTop20  = s.TotalBidVolumeTop20,
            TotalAskVolumeTop20  = s.TotalAskVolumeTop20,
            ImbalanceTop5        = s.ImbalanceTop5,
            ImbalanceTop10       = s.ImbalanceTop10,
            ImbalanceTop20       = s.ImbalanceTop20,
            BidWalls             = [.. s.BidWalls.Select(w => new LlmLiquidityWallPayload { Price = w.Price, Size = w.Size, DistancePctFromMarket = w.DistancePctFromMarket })],
            AskWalls             = [.. s.AskWalls.Select(w => new LlmLiquidityWallPayload { Price = w.Price, Size = w.Size, DistancePctFromMarket = w.DistancePctFromMarket })],
            PressureLabel        = ComputePressureLabel(s.ImbalanceTop10).ToString(),
            LiquiditySkewLabel   = ComputeLiquiditySkewLabel(s.TotalBidVolumeTop20, s.TotalAskVolumeTop20).ToString(),
        };
    }

    private static (decimal spreadAbs, decimal spreadPct) ComputeSpread(decimal bid, decimal ask)
    {
        if (bid <= 0 || ask <= 0 || ask < bid)
            return (0m, 0m);

        var spreadAbs = ask - bid;
        var midPrice  = (ask + bid) / 2m;
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

    // ─── TradeFlow ──────────────────────────────────────────────────────────

    private static LlmTradeFlowPayload BuildTradeFlow(TradeFlowSnapshot s) =>
        new()
        {
            WindowStartUtc             = s.WindowStartUtc,
            WindowEndUtc               = s.WindowEndUtc,
            BuyVolume                  = s.BuyVolume,
            SellVolume                 = s.SellVolume,
            DeltaVolume                = s.DeltaVolume,
            DeltaPct                   = s.DeltaPct,
            BuyTrades                  = s.BuyTrades,
            SellTrades                 = s.SellTrades,
            AvgTradeSize               = s.AvgTradeSize,
            MaxTradeSize               = s.MaxTradeSize,
            HasAggressiveBuyPressure   = s.HasAggressiveBuyPressure,
            HasAggressiveSellPressure  = s.HasAggressiveSellPressure,
        };

    // ─── Timeframe ──────────────────────────────────────────────────────────

    private const decimal LevelStrengthV1 = 0.7m;
    private const string  LevelSourceV1   = "volume-profile";

    private static LlmTimeframePayload BuildTimeframe(TimeframeAnalysisSnapshot s)
    {
        var r         = LlmTimeframeSummaryBuilder.Build(s);
        var lastClose = s.LastCandle.Close;

        return new LlmTimeframePayload
        {
            Timeframe                 = s.Timeframe,
            Trend                     = s.Trend.ToString(),
            TrendCode                 = MapTrendCode(s.Trend),
            TrendStrengthScore        = s.TrendStrengthScore,
            TrendStrengthLabel        = r.TrendStrengthLabel.ToString(),
            Ema20                     = s.Ema20,
            Ema50                     = s.Ema50,
            Ema200                    = s.Ema200,
            Rsi14                     = s.Rsi14,
            Rsi14IsReliable           = s.Rsi14IsReliable,
            Atr14                     = s.Atr14,
            VolumeRatio               = s.VolumeRatio,
            Support1                  = s.Support1,
            Support2                  = s.Support2,
            Resistance1               = s.Resistance1,
            Resistance2               = s.Resistance2,
            DistanceToSupport1Pct     = s.DistanceToSupport1Pct,
            DistanceToResistance1Pct  = s.DistanceToResistance1Pct,
            Support1Meta              = BuildLevelMeta(s.Support1, lastClose, isSupport: true,  distancePct: s.DistanceToSupport1Pct),
            Support2Meta              = BuildLevelMeta(s.Support2, lastClose, isSupport: true),
            Resistance1Meta           = BuildLevelMeta(s.Resistance1, lastClose, isSupport: false, distancePct: s.DistanceToResistance1Pct),
            Resistance2Meta           = BuildLevelMeta(s.Resistance2, lastClose, isSupport: false),
            IsAboveEma20              = s.IsAboveEma20,
            IsAboveEma50              = s.IsAboveEma50,
            IsAboveEma200             = s.IsAboveEma200,
            EmaBullishAlignment       = s.EmaBullishAlignment,
            EmaBearishAlignment       = s.EmaBearishAlignment,
            RsiOverbought             = s.RsiOverbought,
            RsiOversold               = s.RsiOversold,
            Summary = new LlmTimeframeSummaryPayload
            {
                Bias             = r.Bias.ToString(),
                IsTrendConfirmed = r.IsTrendConfirmed,
                MomentumState    = r.MomentumState.ToString(),
                EntryQuality     = r.EntryQuality.ToString(),
                RiskFlags        = [.. r.RiskFlags],
            },
        };
    }

    /// <summary>
    /// Строит метаданные ценового уровня поддержки или сопротивления.
    /// Возвращает <c>null</c>, если уровень равен <c>null</c> (не обнаружен).
    /// </summary>
    private static LlmLevelMetaPayload? BuildLevelMeta(
        decimal? levelPrice,
        decimal  lastClose,
        bool     isSupport,
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
            Price       = price,
            Strength    = LevelStrengthV1,
            Source      = LevelSourceV1,
            DistancePct = distance,
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
            LongShortBiasScore        = s.LongShortBiasScore,
            FundingBiasScore          = s.FundingBiasScore,
            OrderBookPressureScore    = s.OrderBookPressureScore,
            TradeFlowPressureScore    = s.TradeFlowPressureScore,
            MarketRegime              = s.MarketRegime,
        };

    // ─── Portfolio ──────────────────────────────────────────────────────────

    private static LlmPortfolioPayload BuildPortfolio(PortfolioSnapshot s)
    {
        if (!s.IsAvailable)
        {
            return new LlmPortfolioPayload
            {
                IsAvailable           = false,
                TotalEquityUsd        = 0m,
                TotalUnrealizedPnlUsd = 0m,
                OpenPositions         = [],
            };
        }

        return new LlmPortfolioPayload
        {
            IsAvailable            = true,
            TotalEquityUsd         = s.TotalEquityUsd,
            TotalUnrealizedPnlUsd  = s.TotalUnrealizedPnlUsd,
            OpenPositions          = [.. s.OpenPositions.Select(BuildOpenPosition)],
        };
    }

    private static LlmOpenPositionPayload BuildOpenPosition(OpenPositionSnapshot s) =>
        new()
        {
            Symbol            = s.Symbol,
            Side              = s.Side switch { PositionSide.Long => "Long", PositionSide.Short => "Short", _ => s.Side.ToString() },
            Size              = s.Size,
            AvgPrice          = s.AvgPrice,
            UnrealizedPnlPct  = s.UnrealizedPnlPct,
            Leverage          = s.Leverage,
            LiquidationPrice  = s.LiquidationPrice,
        };
}
