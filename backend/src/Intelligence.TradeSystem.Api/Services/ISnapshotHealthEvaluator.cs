using Intelligence.TradeSystem.Api.Models.Payloads;

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
    LlmSnapshotHealthPayload Evaluate(
        MarketSnapshot snapshot,
        AnalysisMode mode);
}
