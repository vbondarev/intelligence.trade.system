using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Analytics;

/// <summary>
/// Собирает единый результат аналитического слоя на основе готового <see cref="MarketAnalysisSnapshot"/>.
/// Предназначен для downstream-потребителей, которым нужны согласованные `MarketRegime`
/// и `FormattedContext` одним вызовом.
/// </summary>
public interface IAnalyticsOutputComposer
{
    /// <summary>
    /// Возвращает канонический рыночный режим и согласованный текстовый контекст одним вызовом.
    /// </summary>
    /// <param name="snapshot">Полностью собранный <see cref="MarketAnalysisSnapshot"/>.</param>
    /// <returns>Готовый <see cref="AnalyticsOutput"/> для downstream-потребителей.</returns>
    /// <exception cref="ArgumentNullException">Если <paramref name="snapshot"/> равен <c>null</c>.</exception>
    AnalyticsOutput Compose(MarketAnalysisSnapshot snapshot);
}

