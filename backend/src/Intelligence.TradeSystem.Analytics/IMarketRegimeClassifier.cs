using Intelligence.TradeSystem.Domain.Snapshots;
using Intelligence.TradeSystem.MarketIntelligence.Snapshots;

namespace Intelligence.TradeSystem.Analytics;

/// <summary>
/// Интерпретирует уже собранный <see cref="MarketAnalysisSnapshot"/> и определяет канонический рыночный режим.
/// Не выполняет повторную агрегацию raw exchange data и не пересчитывает базовые технические индикаторы.
/// </summary>
public interface IMarketRegimeClassifier
{
    /// <summary>
    /// Классифицирует текущий режим рынка по агрегированному снимку.
    /// </summary>
    /// <param name="snapshot">Полностью собранный <see cref="MarketAnalysisSnapshot"/>.</param>
    /// <returns>
    /// Строковое обозначение рыночного режима.
    /// Возвращаемое значение должно соответствовать одному из канонических значений
    /// <see cref="MarketRegimes"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">Если <paramref name="snapshot"/> равен <c>null</c>.</exception>
    string Classify(MarketAnalysisSnapshot snapshot);
}
