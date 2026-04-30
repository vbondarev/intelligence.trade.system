namespace Intelligence.TradeSystem.Api.Models.Payloads;

/// <summary>Контекст режима анализа и флагов конфигурации payload.</summary>
public sealed record LlmAnalysisContextPayload
{
    /// <summary>Режим анализа, применённый при формировании payload.</summary>
    public required string AnalysisMode { get; init; }

    /// <summary>Первичные таймфреймы для данного режима.</summary>
    public required IReadOnlyList<string> PrimaryTimeframes { get; init; }

    /// <summary><c>true</c>, если в payload включён контекст портфеля.</summary>
    public required bool UsesPortfolioContext { get; init; }

    /// <summary><c>true</c>, если в payload включён агрегированный контекст.</summary>
    public required bool UsesAggregatedContext { get; init; }
}