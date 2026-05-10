using Intelligence.TradeSystem.Api.Models.Payloads;
using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Api.Services;

/// <summary>
/// Оценивает свежесть и полноту снапшота для заданного режима анализа.
/// </summary>
public interface ISnapshotHealthEvaluator
{
    /// <summary>
    /// Вычисляет <see cref="LlmSnapshotHealthPayload"/> для указанного снапшота и режима анализа.
    /// </summary>
    /// <param name="snapshot">Рыночный снимок.</param>
    /// <param name="mode">Режим анализа.</param>
    /// <param name="includePortfolio"><c>true</c>, если portfolio-секция запрошена в payload.</param>
    /// <param name="includeAggregatedContext"><c>true</c>, если агрегированный контекст запрошен в payload.</param>
    LlmSnapshotHealthPayload Evaluate(
        MarketAnalysisSnapshot snapshot,
        AnalysisMode mode,
        bool includePortfolio,
        bool includeAggregatedContext);
}

