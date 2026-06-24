namespace Intelligence.TradeSystem.Api.Models.Payloads;

/// <summary>Контекст режима анализа и таймфреймов payload.</summary>
public sealed record LlmAnalysisContextPayload
{
    /// <summary>Режим анализа, применённый при формировании payload.</summary>
    public required string AnalysisMode { get; init; }

    /// <summary>Первичные таймфреймы для данного режима.</summary>
    public required IReadOnlyList<string> PrimaryTimeframes { get; init; }
}
