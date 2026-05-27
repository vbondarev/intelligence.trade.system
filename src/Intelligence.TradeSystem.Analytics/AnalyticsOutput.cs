namespace Intelligence.TradeSystem.Analytics;

/// <summary>
/// Готовый результат аналитического слоя для downstream-потребителей.
/// Объединяет канонический рыночный режим и компактный текстовый контекст,
/// пригодный для передачи в AI- и presentation-слои без повторной интерпретации snapshot.
/// </summary>
public sealed record AnalyticsOutput
{
    /// <summary>
    /// Каноническое строковое обозначение рыночного режима.
    /// Значение должно соответствовать одному из <c>MarketRegimes</c>.
    /// </summary>
    public required string MarketRegime { get; init; }

    /// <summary>
    /// Компактный детерминированный текстовый контекст, сформированный на основе рыночного снимка.
    /// Это вспомогательное представление для downstream-слоёв, а не финальный ответ пользователю.
    /// </summary>
    public required string FormattedContext { get; init; }
}
