using Intelligence.TradeSystem.Abstractions;
using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Api.Contracts;

/// <summary>
/// Запрос API на построение AI-анализа по указанному инструменту.
/// </summary>
public sealed record AiAnalysisRequest
{
    /// <summary>
    /// Идентификатор биржи. В JSON передаётся строковым именем значения enum, например <c>Bybit</c>.
    /// </summary>
    public ExchangeId? Exchange { get; init; }

    /// <summary>
    /// Тикер торгового инструмента, например <c>BTCUSDT</c>.
    /// </summary>
    public string? Symbol { get; init; }

    /// <summary>
    /// Категория рынка инструмента. В JSON передаётся строковым именем значения enum, например <c>Linear</c>, <c>Spot</c> или <c>Inverse</c>.
    /// </summary>
    public MarketCategory? Category { get; init; }

    /// <summary>
    /// Пользовательский запрос, который передаётся AI-сервису вместе с рыночным снимком.
    /// </summary>
    public string? UserQuery { get; init; }
}
