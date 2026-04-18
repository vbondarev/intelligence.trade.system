using Intelligence.TradeSystem.Api.Models;

namespace Intelligence.TradeSystem.Api.Contracts;

/// <summary>
/// Ответ API с агрегированным рыночным снимком инструмента.
/// </summary>
public sealed record MarketAnalysisResponse
{
    /// <summary>Название биржи, с которой был собран снимок.</summary>
    public required string Exchange { get; init; }

    /// <summary>Тикер торгового инструмента, например <c>BTCUSDT</c>.</summary>
    public required string Symbol { get; init; }

    /// <summary>Категория рынка инструмента, например <c>linear</c>, <c>spot</c> или <c>inverse</c>.</summary>
    public required string Category { get; init; }

    /// <summary>Момент времени (UTC), в который был собран снимок.</summary>
    public required DateTimeOffset CapturedAtUtc { get; init; }

    /// <summary>Текущее состояние цены инструмента и статистика за последние 24 часа.</summary>
    public required MarketAnalysisPriceModel Price { get; init; }

    /// <summary>Деривативные метрики инструмента: funding rate, open interest и соотношение long/short.</summary>
    public required MarketAnalysisDerivativesModel Derivatives { get; init; }

    /// <summary>Агрегированное состояние стакана заявок на момент снимка.</summary>
    public required MarketAnalysisOrderBookModel OrderBook { get; init; }

    /// <summary>Агрегированный поток сделок за последнее скользящее окно.</summary>
    public required MarketAnalysisTradeFlowModel TradeFlow { get; init; }

    /// <summary>Технический анализ таймфрейма 15 минут.</summary>
    public required MarketAnalysisTimeframeModel M15 { get; init; }

    /// <summary>Технический анализ таймфрейма 1 час.</summary>
    public required MarketAnalysisTimeframeModel H1 { get; init; }

    /// <summary>Технический анализ таймфрейма 4 часа.</summary>
    public required MarketAnalysisTimeframeModel H4 { get; init; }

    /// <summary>Технический анализ дневного таймфрейма.</summary>
    public required MarketAnalysisTimeframeModel D1 { get; init; }

    /// <summary>Агрегированные оценки рыночного сентимента и режима.</summary>
    public required MarketAnalysisSentimentModel Sentiment { get; init; }

    /// <summary>Текущее состояние торгового счёта и открытых позиций.</summary>
    public required MarketAnalysisPortfolioModel Portfolio { get; init; }

    /// <summary>Классификационные теги снимка, полезные для быстрой интерпретации клиентами и downstream-пайплайнами.</summary>
    public required List<string> Tags { get; init; }
}
