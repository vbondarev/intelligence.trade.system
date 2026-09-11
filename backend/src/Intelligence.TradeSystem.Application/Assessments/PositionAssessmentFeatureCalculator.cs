using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Decisions;
using Intelligence.TradeSystem.Domain.Portfolio;
using Intelligence.TradeSystem.Domain.Snapshots;
using Intelligence.TradeSystem.MarketIntelligence.Snapshots;

namespace Intelligence.TradeSystem.Application.Assessments;

internal static class PositionAssessmentFeatureCalculator
{
    public static PositionAssessmentResult BuildResult(
        PositionAssessmentInput input,
        TimeframeAnalysisSnapshot timeframe,
        decimal? currentPrice,
        AssessmentDataQuality marketQuality,
        AssessmentDataQuality portfolioQuality,
        AssessmentDataQuality overallQuality,
        RiskIncreasePolicyResult portfolioRiskResult)
    {
        var side = input.Position.ExchangePositionKey.PositionSide;
        var trend = MapTrend(timeframe.Trend);
        var alignment = GetTrendAlignment(trend, side);
        var momentum = BuildMomentum(timeframe, input.Rules, trend, side);
        var levels = new PositionAssessmentLevelsContext(
            currentPrice,
            timeframe.Support1,
            timeframe.DistanceToSupport1Pct,
            timeframe.Support1Strength,
            timeframe.Resistance1,
            timeframe.DistanceToResistance1Pct,
            timeframe.Resistance1Strength);

        return new PositionAssessmentResult(
            side,
            currentPrice,
            new(trend, alignment, timeframe.TrendStrengthScore, timeframe.Timeframe),
            momentum,
            BuildVolatility(timeframe, currentPrice),
            levels,
            BuildPnl(input.Position, currentPrice, side),
            BuildStop(input.Position, currentPrice, side),
            BuildBreakeven(input.Position, currentPrice),
            BuildLiquidation(input.Position, currentPrice, side, input.Rules),
            new(
                portfolioRiskResult.Decision,
                input.PortfolioState.FreeCapitalPercent,
                input.PortfolioState.GrossExposureToEquityPercent,
                input.PortfolioState.LargestPositionConcentrationPercent,
                input.PortfolioState.TotalUnrealizedPnl,
                input.PortfolioState.UsedCapital,
                input.PortfolioState.IsComplete,
                input.PortfolioState.IsFresh),
            new(
                marketQuality,
                portfolioQuality,
                overallQuality,
                overallQuality == AssessmentDataQuality.FreshCompleteReliable
                    ? AssessmentSafetyState.Allowed
                    : AssessmentSafetyState.Blocked));
    }

    public static List<ReasonCode> BuildReasonCodes(
        PositionAssessmentInput input,
        PositionAssessmentResult result)
    {
        var reasons = new List<ReasonCode>();
        AddDataQualityReasons(reasons, result.DataQuality);

        reasons.Add(result.Trend.PositionAlignment switch
        {
            PositionTrendAlignment.Aligned => ReasonCode.TrendAligned,
            PositionTrendAlignment.Adverse => ReasonCode.TrendAdverse,
            _ => ReasonCode.TrendFlatOrUnknown,
        });

        reasons.Add(result.Momentum.State switch
        {
            AssessmentMomentumState.Normal => ReasonCode.MomentumNormal,
            AssessmentMomentumState.Overbought => ReasonCode.MomentumOverbought,
            AssessmentMomentumState.Oversold => ReasonCode.MomentumOversold,
            _ => ReasonCode.MomentumUnavailable,
        });
        if (result.Momentum.PotentialExhaustion)
            reasons.Add(ReasonCode.MomentumExhaustion);
        if (!result.Volatility.IsReliable || !result.Volatility.Atr14.HasValue)
            reasons.Add(ReasonCode.VolatilityUnavailable);

        if (result.Levels.Support1.HasValue)
        {
            reasons.Add(ReasonCode.SupportAvailable);
            if (result.Levels.DistanceToSupport1Percent <= input.Rules.NearbyLevelDistancePercent)
                reasons.Add(ReasonCode.SupportNearby);
        }
        if (result.Levels.Resistance1.HasValue)
        {
            reasons.Add(ReasonCode.ResistanceAvailable);
            if (result.Levels.DistanceToResistance1Percent <= input.Rules.NearbyLevelDistancePercent)
                reasons.Add(ReasonCode.ResistanceNearby);
        }
        if (input.MarketSnapshot.H4.VolumeRatioIsReliable &&
            input.MarketSnapshot.H4.VolumeRatio < input.Rules.LowVolumeRatioThreshold)
            reasons.Add(ReasonCode.LowVolume);

        reasons.Add(result.Pnl.UnrealizedPnl switch
        {
            > 0m => ReasonCode.PnlPositive,
            < 0m => ReasonCode.PnlNegative,
            _ => ReasonCode.PnlUnavailable,
        });
        reasons.Add(result.Stop.State switch
        {
            AssessmentStopState.Protective => ReasonCode.StopProtective,
            AssessmentStopState.NonProtective => ReasonCode.StopNonProtective,
            AssessmentStopState.Unknown => ReasonCode.StopUnknown,
            _ => ReasonCode.StopMissing,
        });
        reasons.Add(result.Breakeven.PriceRelativeToBreakEven switch
        {
            AssessmentPricePosition.Unavailable => ReasonCode.BreakevenUnavailable,
            _ => result.Breakeven.IsProfitable
                ? ReasonCode.BreakevenProfitable
                : ReasonCode.BreakevenUnprofitable,
        });
        reasons.Add(result.Liquidation.State switch
        {
            AssessmentLiquidationState.Far => ReasonCode.LiquidationFar,
            AssessmentLiquidationState.Near => ReasonCode.LiquidationNearby,
            AssessmentLiquidationState.Invalid => ReasonCode.LiquidationInvalid,
            _ => ReasonCode.LiquidationUnavailable,
        });

        return reasons;
    }

    public static decimal? GetCurrentPrice(MarketSnapshot snapshot) =>
        snapshot.Price.MarkPrice > 0m
            ? snapshot.Price.MarkPrice
            : snapshot.Price.LastPrice > 0m
                ? snapshot.Price.LastPrice
                : null;

    private static PositionAssessmentMomentumContext BuildMomentum(
        TimeframeAnalysisSnapshot timeframe,
        PositionAssessmentRules rules,
        AssessmentTrendDirection trend,
        PositionSide side)
    {
        if (!timeframe.Rsi14IsReliable || !timeframe.Rsi14.HasValue)
            return new(null, false, AssessmentMomentumState.Unavailable, false);

        var state = timeframe.Rsi14.Value >= rules.RsiOverboughtThreshold
            ? AssessmentMomentumState.Overbought
            : timeframe.Rsi14.Value <= rules.RsiOversoldThreshold
                ? AssessmentMomentumState.Oversold
                : AssessmentMomentumState.Normal;
        var exhaustion =
            (side == PositionSide.Long && trend == AssessmentTrendDirection.Bullish && state == AssessmentMomentumState.Overbought) ||
            (side == PositionSide.Short && trend == AssessmentTrendDirection.Bearish && state == AssessmentMomentumState.Oversold);

        return new(timeframe.Rsi14, true, state, exhaustion);
    }

    private static PositionAssessmentVolatilityContext BuildVolatility(
        TimeframeAnalysisSnapshot timeframe,
        decimal? currentPrice)
    {
        var atrPercent = timeframe.Atr14.HasValue && currentPrice is > 0m
            ? (decimal?)(timeframe.Atr14.Value / currentPrice.Value * 100m)
            : null;
        return new(timeframe.Atr14, atrPercent, timeframe.AtrIsReliable, timeframe.AtrIsFallback);
    }

    private static PositionAssessmentPnlContext BuildPnl(
        Position position,
        decimal? currentPrice,
        PositionSide side)
    {
        var pnlPercent = position.UnrealizedPnl.HasValue && position.PositionValue is > 0m
            ? (decimal?)(position.UnrealizedPnl.Value / position.PositionValue.Value * 100m)
            : null;
        var relativeToEntry = GetPricePosition(currentPrice, position.AverageEntryPrice);
        var isFavorable = currentPrice.HasValue && position.AverageEntryPrice.HasValue &&
            (side == PositionSide.Long
                ? currentPrice.Value > position.AverageEntryPrice.Value
                : currentPrice.Value < position.AverageEntryPrice.Value);

        return new(
            position.UnrealizedPnl,
            pnlPercent,
            position.AverageEntryPrice,
            currentPrice,
            relativeToEntry)
        {
            IsFavorable = isFavorable,
        };
    }

    private static PositionAssessmentStopContext BuildStop(
        Position position,
        decimal? currentPrice,
        PositionSide side)
    {
        var stop = position.StopLoss ?? position.TrailingStop;
        if (!stop.HasValue)
            return new(null, null, null, AssessmentStopState.Unavailable, AssessmentPricePosition.Unavailable);

        var distance = CalculateAbsoluteDistancePercent(currentPrice, stop);
        var stopRelativeToEntry = CalculateSignedDistancePercent(position.AverageEntryPrice, stop);
        var relativeToEntry = GetPricePosition(stop, position.AverageEntryPrice);
        var state = currentPrice is null
            ? AssessmentStopState.Unknown
            : side == PositionSide.Long
                ? stop < currentPrice ? AssessmentStopState.Protective : AssessmentStopState.NonProtective
                : stop > currentPrice ? AssessmentStopState.Protective : AssessmentStopState.NonProtective;

        return new(stop, distance, stopRelativeToEntry, state, relativeToEntry);
    }

    private static PositionAssessmentBreakevenContext BuildBreakeven(
        Position position,
        decimal? currentPrice)
    {
        if (!position.BreakEvenPrice.HasValue)
            return new(null, null, null, AssessmentPricePosition.Unavailable);

        return new(
            position.BreakEvenPrice,
            CalculateAbsoluteDistancePercent(currentPrice, position.BreakEvenPrice),
            CalculateSignedDistancePercent(position.AverageEntryPrice, position.BreakEvenPrice),
            GetPricePosition(currentPrice, position.BreakEvenPrice))
        {
            IsProfitable = position.ExchangePositionKey.PositionSide == PositionSide.Long
                ? currentPrice > position.BreakEvenPrice
                : currentPrice < position.BreakEvenPrice,
        };
    }

    private static PositionAssessmentLiquidationContext BuildLiquidation(
        Position position,
        decimal? currentPrice,
        PositionSide side,
        PositionAssessmentRules rules)
    {
        if (!position.LiquidationPrice.HasValue || currentPrice is not > 0m)
            return new(position.LiquidationPrice, null, AssessmentLiquidationState.Unavailable);

        var distance = side == PositionSide.Long
            ? (currentPrice.Value - position.LiquidationPrice.Value) / currentPrice.Value * 100m
            : (position.LiquidationPrice.Value - currentPrice.Value) / currentPrice.Value * 100m;
        var state = distance < 0m
            ? AssessmentLiquidationState.Invalid
            : distance <= rules.LiquidationDangerDistancePercent
                ? AssessmentLiquidationState.Near
                : AssessmentLiquidationState.Far;

        return new(position.LiquidationPrice, distance, state);
    }

    private static void AddDataQualityReasons(
        List<ReasonCode> reasons,
        PositionAssessmentDataQualityContext dataQuality)
    {
        switch (dataQuality.Market)
        {
            case AssessmentDataQuality.Stale:
                reasons.Add(ReasonCode.MarketDataStale);
                break;
            case AssessmentDataQuality.Partial:
                reasons.Add(ReasonCode.MarketDataPartial);
                break;
            case AssessmentDataQuality.Uncertain:
                reasons.Add(ReasonCode.MarketDataUncertain);
                break;
        }
    }

    private static AssessmentTrendDirection MapTrend(MarketTrend trend) => trend switch
    {
        MarketTrend.Bullish => AssessmentTrendDirection.Bullish,
        MarketTrend.Bearish => AssessmentTrendDirection.Bearish,
        MarketTrend.Sideways => AssessmentTrendDirection.Sideways,
        _ => AssessmentTrendDirection.Unknown,
    };

    private static PositionTrendAlignment GetTrendAlignment(
        AssessmentTrendDirection trend,
        PositionSide side) =>
        trend switch
        {
            AssessmentTrendDirection.Bullish when side == PositionSide.Long => PositionTrendAlignment.Aligned,
            AssessmentTrendDirection.Bullish => PositionTrendAlignment.Adverse,
            AssessmentTrendDirection.Bearish when side == PositionSide.Short => PositionTrendAlignment.Aligned,
            AssessmentTrendDirection.Bearish => PositionTrendAlignment.Adverse,
            _ => PositionTrendAlignment.FlatOrUnknown,
        };

    private static AssessmentPricePosition GetPricePosition(decimal? value, decimal? reference) =>
        value is null || reference is null
            ? AssessmentPricePosition.Unavailable
            : value.Value < reference.Value
                ? AssessmentPricePosition.Below
                : value.Value > reference.Value
                    ? AssessmentPricePosition.Above
                    : AssessmentPricePosition.At;

    private static decimal? CalculateAbsoluteDistancePercent(decimal? current, decimal? target) =>
        current is > 0m && target.HasValue
            ? Math.Abs(current.Value - target.Value) / current.Value * 100m
            : null;

    private static decimal? CalculateSignedDistancePercent(decimal? origin, decimal? target) =>
        origin is > 0m && target.HasValue
            ? (target.Value - origin.Value) / origin.Value * 100m
            : null;
}
