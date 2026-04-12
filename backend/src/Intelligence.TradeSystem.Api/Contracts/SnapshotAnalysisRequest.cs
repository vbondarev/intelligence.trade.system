namespace Intelligence.TradeSystem.Api.Contracts;

/// <summary>
/// HTTP request contract для snapshot-analysis.
/// </summary>
public sealed record SnapshotAnalysisRequest
{
    /// <summary>
    /// Идентификатор биржи.
    /// На текущем этапе ожидается строковое имя enum-значения, например <c>Bybit</c>.
    /// </summary>
    public string? Exchange { get; init; }

    /// <summary>
    /// Тикер торгового инструмента, например <c>BTCUSDT</c>.
    /// </summary>
    public string? Symbol { get; init; }

    /// <summary>
    /// Категория рынка инструмента, например <c>Linear</c>, <c>Spot</c> или <c>Inverse</c>.
    /// </summary>
    public string? Category { get; init; }
}

