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

    public decimal? Ema20 { get; init; }
    public decimal? Ema50 { get; init; }
    public decimal? Ema200 { get; init; }
    public decimal? Rsi14 { get; init; }
    /// <summary><c>true</c> — RSI рассчитан на основе достаточного количества свечей.</summary>
    public bool Rsi14IsReliable { get; init; }
    public decimal? Atr14 { get; init; }
    public decimal? VolumeRatio { get; init; }
    /// <summary>Ближайший уровень поддержки. <c>null</c> — не обнаружен.</summary>
    public decimal? Support1 { get; init; }
    /// <summary>Второй уровень поддержки. <c>null</c> — не обнаружен.</summary>
    public decimal? Support2 { get; init; }
    /// <summary>Ближайший уровень сопротивления. <c>null</c> — не обнаружен.</summary>
    public decimal? Resistance1 { get; init; }
    /// <summary>Второй уровень сопротивления. <c>null</c> — не обнаружен.</summary>
    public decimal? Resistance2 { get; init; }
    /// <summary>Расстояние до <c>support1</c> в процентах. <c>null</c> — если уровень не обнаружен.</summary>
    public decimal? DistanceToSupport1Pct { get; init; }
    /// <summary>Расстояние до <c>resistance1</c> в процентах. <c>null</c> — если уровень не обнаружен.</summary>
    public decimal? DistanceToResistance1Pct { get; init; }

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
