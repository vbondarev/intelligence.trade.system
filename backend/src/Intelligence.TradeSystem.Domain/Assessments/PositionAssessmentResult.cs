using Intelligence.TradeSystem.Domain.Decisions;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Portfolio;
using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Domain.Assessments;

/// <summary>
/// Структурированный неизменяемый результат детерминированной оценки позиции.
/// </summary>
public sealed record PositionAssessmentResult
{
    /// <summary>
    /// Создаёт структурированный результат оценки.
    /// </summary>
    public PositionAssessmentResult(
        PositionSide positionSide,
        decimal? currentPrice,
        PositionAssessmentTrendContext trend,
        PositionAssessmentMomentumContext momentum,
        PositionAssessmentVolatilityContext volatility,
        PositionAssessmentLevelsContext levels,
        PositionAssessmentPnlContext pnl,
        PositionAssessmentStopContext stop,
        PositionAssessmentBreakevenContext breakeven,
        PositionAssessmentLiquidationContext liquidation,
        PositionAssessmentPortfolioRiskContext portfolioRisk,
        PositionAssessmentDataQualityContext dataQuality)
    {
        if (!Enum.IsDefined(positionSide))
            throw new ArgumentOutOfRangeException(nameof(positionSide), positionSide, "Position side must be defined.");

        ArgumentNullException.ThrowIfNull(trend);
        ArgumentNullException.ThrowIfNull(momentum);
        ArgumentNullException.ThrowIfNull(volatility);
        ArgumentNullException.ThrowIfNull(levels);
        ArgumentNullException.ThrowIfNull(pnl);
        ArgumentNullException.ThrowIfNull(stop);
        ArgumentNullException.ThrowIfNull(breakeven);
        ArgumentNullException.ThrowIfNull(liquidation);
        ArgumentNullException.ThrowIfNull(portfolioRisk);
        ArgumentNullException.ThrowIfNull(dataQuality);
        ValidateEnums(trend, momentum, stop, liquidation, portfolioRisk, dataQuality);

        PositionSide = positionSide;
        CurrentPrice = currentPrice;
        Trend = trend;
        Momentum = momentum;
        Volatility = volatility;
        Levels = levels;
        Pnl = pnl;
        Stop = stop;
        Breakeven = breakeven;
        Liquidation = liquidation;
        PortfolioRisk = portfolioRisk;
        DataQuality = dataQuality;
    }

    /// <summary>Направление позиции.</summary>
    public PositionSide PositionSide { get; }

    /// <summary>
    /// Признак legacy-результата, созданного до появления структурированного assessment context.
    /// </summary>
    public bool IsLegacy { get; init; }

    /// <summary>Цена, использованная для расчёта производных признаков.</summary>
    public decimal? CurrentPrice { get; }

    /// <summary>Контекст тренда.</summary>
    public PositionAssessmentTrendContext Trend { get; }

    /// <summary>Контекст моментума.</summary>
    public PositionAssessmentMomentumContext Momentum { get; }

    /// <summary>Контекст волатильности.</summary>
    public PositionAssessmentVolatilityContext Volatility { get; }

    /// <summary>Контекст значимых уровней.</summary>
    public PositionAssessmentLevelsContext Levels { get; }

    /// <summary>Контекст PnL и цены входа.</summary>
    public PositionAssessmentPnlContext Pnl { get; }

    /// <summary>Контекст защитного стопа.</summary>
    public PositionAssessmentStopContext Stop { get; }

    /// <summary>Контекст безубытка.</summary>
    public PositionAssessmentBreakevenContext Breakeven { get; }

    /// <summary>Контекст ликвидации.</summary>
    public PositionAssessmentLiquidationContext Liquidation { get; }

    /// <summary>Контекст портфельного риска.</summary>
    public PositionAssessmentPortfolioRiskContext PortfolioRisk { get; }

    /// <summary>Контекст качества данных и неотключаемого safety guard.</summary>
    public PositionAssessmentDataQualityContext DataQuality { get; }

    private static void ValidateEnums(
        PositionAssessmentTrendContext trend,
        PositionAssessmentMomentumContext momentum,
        PositionAssessmentStopContext stop,
        PositionAssessmentLiquidationContext liquidation,
        PositionAssessmentPortfolioRiskContext portfolioRisk,
        PositionAssessmentDataQualityContext dataQuality)
    {
        if (!Enum.IsDefined(trend.MarketTrend) ||
            !Enum.IsDefined(trend.PositionAlignment) ||
            !Enum.IsDefined(momentum.State) ||
            !Enum.IsDefined(stop.State) ||
            !Enum.IsDefined(liquidation.State) ||
            !Enum.IsDefined(portfolioRisk.PolicyDecision) ||
            !Enum.IsDefined(dataQuality.Market) ||
            !Enum.IsDefined(dataQuality.Portfolio) ||
            !Enum.IsDefined(dataQuality.Overall) ||
            !Enum.IsDefined(dataQuality.SafetyState))
            throw new ArgumentOutOfRangeException(nameof(dataQuality), "Assessment result contains an undefined state.");

        ArgumentException.ThrowIfNullOrWhiteSpace(trend.Timeframe);
    }

    /// <summary>
    /// Создаёт минимальный результат для legacy API, не имеющего структурированных market inputs.
    /// </summary>
    public static PositionAssessmentResult Legacy(RiskIncreaseDecision portfolioRiskDecision) =>
        new(
            PositionSide.Unknown,
            null,
            new(AssessmentTrendDirection.Unknown, PositionTrendAlignment.FlatOrUnknown, 0m, "legacy"),
            new(null, false, AssessmentMomentumState.Unavailable, false),
            new(null, null, false, false),
            new(null, null, null, null, null, null, null),
            new(null, null, null, null, AssessmentPricePosition.Unavailable),
            new(
                null,
                null,
                null,
                AssessmentStopState.Unavailable,
                AssessmentPricePosition.Unavailable,
                null,
                false),
            new(null, null, null, AssessmentPricePosition.Unavailable),
            new(null, null, AssessmentLiquidationState.Unavailable),
            new(
                portfolioRiskDecision,
                null,
                null,
                null,
                null,
                null,
                false,
                false),
            new(
                AssessmentDataQuality.Uncertain,
                AssessmentDataQuality.Uncertain,
                AssessmentDataQuality.Uncertain,
                AssessmentSafetyState.NotEvaluated))
        {
            IsLegacy = true,
        };
}

/// <summary>Контекст рыночного тренда относительно позиции.</summary>
public sealed record PositionAssessmentTrendContext(
    AssessmentTrendDirection MarketTrend,
    PositionTrendAlignment PositionAlignment,
    decimal Strength,
    string Timeframe);

/// <summary>Контекст RSI и состояния моментума.</summary>
public sealed record PositionAssessmentMomentumContext(
    decimal? Rsi14,
    bool IsReliable,
    AssessmentMomentumState State,
    bool PotentialExhaustion);

/// <summary>Контекст фактической и нормализованной волатильности.</summary>
public sealed record PositionAssessmentVolatilityContext(
    decimal? Atr14,
    decimal? AtrPercentOfPrice,
    bool IsReliable,
    bool IsFallback);

/// <summary>Контекст поддержки и сопротивления.</summary>
public sealed record PositionAssessmentLevelsContext(
    decimal? CurrentPrice,
    decimal? Support1,
    decimal? DistanceToSupport1Percent,
    decimal? Support1Strength,
    decimal? Resistance1,
    decimal? DistanceToResistance1Percent,
    decimal? Resistance1Strength);

/// <summary>Контекст PnL и положения рынка относительно цены входа.</summary>
public sealed record PositionAssessmentPnlContext(
    decimal? UnrealizedPnl,
    decimal? PnlPercent,
    decimal? AverageEntryPrice,
    decimal? CurrentPrice,
    AssessmentPricePosition PriceRelativeToEntry)
{
    /// <summary>Положение текущей цены выгодно для направления позиции.</summary>
    public bool IsFavorable { get; init; }
}

/// <summary>Контекст стопа и его положения относительно позиции.</summary>
public sealed record PositionAssessmentStopContext(
    decimal? StopPrice,
    decimal? DistanceFromCurrentPercent,
    decimal? StopRelativeToEntryPercent,
    AssessmentStopState State,
    AssessmentPricePosition PriceRelativeToEntry,
    decimal? TrailingStopDistance,
    bool HasTrailingStop);

/// <summary>Контекст цены безубытка.</summary>
public sealed record PositionAssessmentBreakevenContext(
    decimal? BreakEvenPrice,
    decimal? DistanceFromCurrentPercent,
    decimal? DistanceFromEntryPercent,
    AssessmentPricePosition PriceRelativeToBreakEven)
{
    /// <summary>Текущая цена находится на выгодной стороне безубытка.</summary>
    public bool IsProfitable { get; init; }
}

/// <summary>Контекст расстояния до ликвидации.</summary>
public sealed record PositionAssessmentLiquidationContext(
    decimal? LiquidationPrice,
    decimal? DistanceFromCurrentPercent,
    AssessmentLiquidationState State);

/// <summary>Контекст результата портфельной проверки до safety guard.</summary>
public sealed record PositionAssessmentPortfolioRiskContext(
    RiskIncreaseDecision PolicyDecision,
    decimal? FreeCapitalPercent,
    decimal? GrossExposureToEquityPercent,
    decimal? LargestPositionConcentrationPercent,
    decimal? TotalUnrealizedPnl,
    decimal? UsedCapital,
    bool IsComplete,
    bool IsFresh);

/// <summary>Сводный контекст качества источников и safety guard.</summary>
public sealed record PositionAssessmentDataQualityContext(
    AssessmentDataQuality Market,
    AssessmentDataQuality Portfolio,
    AssessmentDataQuality Overall,
    AssessmentSafetyState SafetyState);
