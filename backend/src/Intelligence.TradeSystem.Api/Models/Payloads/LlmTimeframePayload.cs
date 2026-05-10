using System.Text.Json.Serialization;

namespace Intelligence.TradeSystem.Api.Models.Payloads;

/// <summary>Технический анализ одного таймфрейма с детерминированным summary.</summary>
public sealed record LlmTimeframePayload
{
    public required string Timeframe { get; init; }
    public required string Trend { get; init; }

    /// <summary>Числовой код тренда согласно контракту API: Unknown=0, Bullish=1, Bearish=2, Sideways=3.</summary>
    public required int TrendCode { get; init; }

    public required decimal TrendStrengthScore { get; init; }

    /// <summary>Метка силы тренда.</summary>
    public required string TrendStrengthLabel { get; init; }

    public required decimal Ema20 { get; init; }
    public required decimal Ema50 { get; init; }
    public required decimal Ema200 { get; init; }
    public required decimal Rsi14 { get; init; }
    public required decimal Atr14 { get; init; }
    public required decimal VolumeRatio { get; init; }
    public required decimal Support1 { get; init; }
    public required decimal Support2 { get; init; }
    public required decimal Resistance1 { get; init; }
    public required decimal Resistance2 { get; init; }
    public required decimal DistanceToSupport1Pct { get; init; }
    public required decimal DistanceToResistance1Pct { get; init; }

    /// <summary>
    /// Метаданные ближайшего уровня поддержки.
    /// Отсутствует, если <c>support1 == 0</c> (уровень не обнаружен).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LlmLevelMetaPayload? Support1Meta { get; init; }

    /// <summary>
    /// Метаданные второго уровня поддержки.
    /// Отсутствует, если <c>support2 == 0</c> (уровень не обнаружен).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LlmLevelMetaPayload? Support2Meta { get; init; }

    /// <summary>
    /// Метаданные ближайшего уровня сопротивления.
    /// Отсутствует, если <c>resistance1 == 0</c> (уровень не обнаружен).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LlmLevelMetaPayload? Resistance1Meta { get; init; }

    /// <summary>
    /// Метаданные второго уровня сопротивления.
    /// Отсутствует, если <c>resistance2 == 0</c> (уровень не обнаружен).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LlmLevelMetaPayload? Resistance2Meta { get; init; }
    public required bool IsAboveEma20 { get; init; }
    public required bool IsAboveEma50 { get; init; }
    public required bool IsAboveEma200 { get; init; }
    public required bool EmaBullishAlignment { get; init; }
    public required bool EmaBearishAlignment { get; init; }
    public required bool RsiOverbought { get; init; }
    public required bool RsiOversold { get; init; }

    /// <summary>Детерминированный итоговый анализ таймфрейма.</summary>
    public required LlmTimeframeSummaryPayload Summary { get; init; }
}
