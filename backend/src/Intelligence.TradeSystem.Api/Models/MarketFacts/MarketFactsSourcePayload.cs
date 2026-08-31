namespace Intelligence.TradeSystem.Api.Models.MarketFacts;

/// <summary>
/// Источник данных и мета-информация о снапшоте.
/// </summary>
public sealed record MarketFactsSourcePayload
{
    /// <summary>Версия схемы payload. Например: <c>market-facts/v1</c>.</summary>
    public required string PayloadSchemaVersion { get; init; }

    /// <summary>Название биржи. Например: <c>Bybit</c>.</summary>
    public required string Exchange { get; init; }

    /// <summary>Тикер инструмента. Например: <c>BTCUSDT</c>.</summary>
    public required string Symbol { get; init; }

    /// <summary>Категория рынка. Например: <c>Linear</c>.</summary>
    public required string Category { get; init; }

    /// <summary>Момент времени (UTC), в который был собран снапшот.</summary>
    public required DateTimeOffset CapturedAtUtc { get; init; }
}
