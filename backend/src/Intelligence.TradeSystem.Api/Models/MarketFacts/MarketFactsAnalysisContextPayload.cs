namespace Intelligence.TradeSystem.Api.Models.MarketFacts;

/// <summary>
/// Контекст режима анализа и конфигурации payload.
/// </summary>
public sealed record MarketFactsAnalysisContextPayload
{
    /// <summary>
    /// Режим анализа. Например: <c>Intraday</c>, <c>Swing</c>, <c>Portfolio</c>.
    /// </summary>
    public required string AnalysisMode { get; init; }

    /// <summary>
    /// Первичные таймфреймы для данного режима анализа.
    /// Например: <c>["15m", "1h", "4h"]</c> для <c>Intraday</c>.
    /// </summary>
    public required IReadOnlyList<string> PrimaryTimeframes { get; init; }
}
