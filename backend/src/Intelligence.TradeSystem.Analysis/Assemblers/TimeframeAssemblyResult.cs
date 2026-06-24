using Intelligence.TradeSystem.Analysis.Diagnostics;
using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Analysis.Assemblers;

/// <summary>
/// Результат работы <see cref="TimeframeSnapshotAssembler"/>.
/// Содержит вычисленный снапшот и диагностику качества индикаторов.
/// </summary>
public sealed record TimeframeAssemblyResult
{
    /// <summary>Снапшот технического анализа для одного таймфрейма.</summary>
    public required TimeframeAnalysisSnapshot Snapshot { get; init; }

    /// <summary>
    /// Список диагностических записей по индикаторам, которые были рассчитаны
    /// по fallback-логике или оказались недоступными.
    /// Пустой список означает, что все индикаторы рассчитаны полноценно.
    /// </summary>
    public required IReadOnlyList<IndicatorDiagnostic> Diagnostics { get; init; }
}
