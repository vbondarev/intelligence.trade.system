namespace Intelligence.TradeSystem.Api.Models.MarketFacts;

/// <summary>
/// Технический анализ на конкретном таймфрейме.
/// </summary>
public sealed record MarketFactsTimeframePayload
{
    /// <summary>Таймфрейм. Например: <c>15m</c>, <c>1h</c>, <c>4h</c>, <c>1d</c>.</summary>
    public required string Timeframe { get; init; }

    /// <summary>Тренд на таймфрейме.</summary>
    public required MarketFactsTimeframeTrendPayload Trend { get; init; }

    /// <summary>Значения технических индикаторов.</summary>
    public required MarketFactsTimeframeIndicatorsPayload Indicators { get; init; }

    /// <summary>Уровни поддержки и сопротивления таймфрейма.</summary>
    public required MarketFactsTimeframeLevelsPayload Levels { get; init; }

    /// <summary>Производные флаги, вычисленные на основе индикаторов.</summary>
    public required MarketFactsTimeframeDerivedFlagsPayload DerivedFlags { get; init; }

    /// <summary>Backend-generated summary и флаги качества входа.</summary>
    public required MarketFactsTimeframeBackendSummaryPayload BackendSummary { get; init; }
}
