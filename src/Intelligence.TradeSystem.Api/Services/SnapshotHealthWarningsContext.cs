using Intelligence.TradeSystem.Api.Configuration;
using Intelligence.TradeSystem.Api.Models.Payloads;

namespace Intelligence.TradeSystem.Api.Services;

/// <summary>
/// Контекст, передаваемый в <see cref="SnapshotHealthWarningsBuilder"/> для генерации мягких предупреждений.
/// </summary>
internal sealed record SnapshotHealthWarningsContext
{
    /// <summary>Режим анализа, определяющий первичные таймфреймы и набор обязательных секций.</summary>
    public required AnalysisMode Mode { get; init; }

    /// <summary><c>true</c>, если в payload запрошена секция portfolio.</summary>
    public required bool IncludePortfolio { get; init; }

    /// <summary><c>true</c>, если в payload запрошен агрегированный контекст рынка.</summary>
    public required bool IncludeAggregatedContext { get; init; }

    /// <summary>Возраст каждой секции в миллисекундах, вычисленный оценщиком свежести.</summary>
    public required IReadOnlyDictionary<string, long> SectionAgesMs { get; init; }

    /// <summary>Пороги свежести секций для текущего режима анализа.</summary>
    public required SectionFreshnessOptions Thresholds { get; init; }

    /// <summary>
    /// Доля от максимального возраста секции, при достижении которой секция считается
    /// "близкой к устареванию". Берётся из <see cref="SnapshotFreshnessOptions.StalenessProximityFactor"/>.
    /// </summary>
    public required decimal StalenessProximityFactor { get; init; }
}
