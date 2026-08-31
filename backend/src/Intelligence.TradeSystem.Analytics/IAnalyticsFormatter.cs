using Intelligence.TradeSystem.Domain.Snapshots;
using Intelligence.TradeSystem.MarketIntelligence.Snapshots;

namespace Intelligence.TradeSystem.Analytics;

/// <summary>
/// Формирует компактный текстовый аналитический контекст на основе уже собранного
/// <see cref="MarketAnalysisSnapshot"/>.
/// Не пересчитывает raw market data и не генерирует финальный пользовательский ответ,
/// а подготавливает стабильное текстовое представление для downstream-потребителей,
/// таких как AI- и presentation-слои.
/// </summary>
public interface IAnalyticsFormatter
{
    /// <summary>
    /// Преобразует готовый рыночный снимок в компактное детерминированное текстовое представление.
    /// </summary>
    /// <param name="snapshot">Полностью собранный <see cref="MarketAnalysisSnapshot"/>.</param>
    /// <returns>
    /// Компактный текстовый контекст для последующей интерпретации в AI- или UI-слое.
    /// </returns>
    /// <exception cref="ArgumentNullException">Если <paramref name="snapshot"/> равен <c>null</c>.</exception>
    string Format(MarketAnalysisSnapshot snapshot);
}
