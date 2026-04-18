namespace Intelligence.TradeSystem.Api.Contracts;

/// <summary>
/// Запрос API на построение агрегированного рыночного снимка по указанному инструменту.
/// </summary>
public sealed record SnapshotAnalysisRequest
{
    /// <summary>
    /// Идентификатор биржи как строковое имя значения enum, например <c>Bybit</c>.
    /// </summary>
    public string? Exchange { get; init; }

    /// <summary>
    /// Тикер торгового инструмента, например <c>BTCUSDT</c>.
    /// </summary>
    public string? Symbol { get; init; }

    /// <summary>
    /// Категория рынка инструмента как строковое имя значения enum, например <c>Linear</c>, <c>Spot</c> или <c>Inverse</c>.
    /// </summary>
    public string? Category { get; init; }
}

