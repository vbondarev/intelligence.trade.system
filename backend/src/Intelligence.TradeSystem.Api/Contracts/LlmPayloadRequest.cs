using Intelligence.TradeSystem.Abstractions;
using Intelligence.TradeSystem.Api.Models.Payloads;
using Intelligence.TradeSystem.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Intelligence.TradeSystem.Api.Contracts;

/// <summary>
/// Query-параметры эндпоинта <c>GET /api/market-analysis/{symbol}/llm-payload</c>.
/// </summary>
public sealed record LlmPayloadRequest
{
    /// <summary>
    /// Идентификатор биржи. Передаётся строковым именем значения enum, например <c>Bybit</c>.
    /// </summary>
    [FromQuery(Name = "exchange")]
    public ExchangeId? Exchange { get; init; }

    /// <summary>
    /// Категория рынка инструмента. Например <c>Linear</c>, <c>Spot</c> или <c>Inverse</c>.
    /// </summary>
    [FromQuery(Name = "category")]
    public MarketCategory? Category { get; init; }

    /// <summary>
    /// Режим анализа. Допустимые значения: <c>Intraday</c>, <c>Swing</c>, <c>Portfolio</c>.
    /// Если не передан — используется <c>Intraday</c>.
    /// </summary>
    [FromQuery(Name = "mode")]
    public AnalysisMode? Mode { get; init; }
}
