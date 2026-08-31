namespace Intelligence.TradeSystem.Api.Models.MarketFacts;

/// <summary>
/// Качество и полнота данных снапшота.
/// </summary>
public sealed record MarketFactsDataQualityPayload
{
    /// <summary>
    /// Общий статус качества данных.
    /// Допустимые значения: <c>ok</c>, <c>partial</c>, <c>stale</c>, <c>error</c>.
    /// Вычисляется mapper'ом.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>Признак свежести данных.</summary>
    public required bool IsFresh { get; init; }

    /// <summary>Признак частичного снапшота (некоторые секции отсутствуют).</summary>
    public required bool IsPartial { get; init; }

    /// <summary>Предупреждения о качестве данных.</summary>
    public required IReadOnlyList<string> Warnings { get; init; }

    /// <summary>Список отсутствующих секций снапшота.</summary>
    public required IReadOnlyList<string> MissingSections { get; init; }

    /// <summary>
    /// Возраст каждой секции в миллисекундах на момент сборки снапшота.
    /// Ключ — название секции, значение — возраст в мс.
    /// </summary>
    public required IReadOnlyDictionary<string, long> SectionAgesMs { get; init; }

    /// <summary>
    /// Диагностические записи для индикаторов всех таймфреймов.
    /// Пустой список (<c>[]</c>) означает, что все индикаторы рассчитаны полноценно.
    /// </summary>
    public required IReadOnlyList<MarketFactsIndicatorDiagnosticPayload> IndicatorDiagnostics { get; init; }
}
