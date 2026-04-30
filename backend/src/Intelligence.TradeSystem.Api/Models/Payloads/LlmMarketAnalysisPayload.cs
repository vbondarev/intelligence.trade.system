using System.Text.Json.Serialization;

namespace Intelligence.TradeSystem.Api.Models.Payloads;

/// <summary>
/// LLM-оптимизированный payload рыночного анализа.
/// Содержит только сигнальные данные, пригодные как прямой вход для GPT / Qwen / DeepSeek.
/// </summary>
public sealed record LlmMarketAnalysisPayload
{
    /// <summary>Версия схемы payload. Текущая версия: <c>1.0</c>.</summary>
    public required string SchemaVersion { get; init; }

    /// <summary>Название биржи. Например: <c>Bybit</c>.</summary>
    public required string Exchange { get; init; }

    /// <summary>Тикер инструмента. Например: <c>BTCUSDT</c>.</summary>
    public required string Symbol { get; init; }

    /// <summary>Категория рынка. Например: <c>linear</c>.</summary>
    public required string Category { get; init; }

    /// <summary>Момент времени (UTC), в который был собран снапшот.</summary>
    public required DateTimeOffset CapturedAtUtc { get; init; }

    /// <summary>Контекст режима анализа и конфигурации payload.</summary>
    public required LlmAnalysisContextPayload AnalysisContext { get; init; }

    /// <summary>Оценка свежести и полноты снапшота.</summary>
    public required LlmSnapshotHealthPayload SnapshotHealth { get; init; }

    /// <summary>Текущее состояние цены.</summary>
    public required LlmPricePayload Price { get; init; }

    /// <summary>Данные деривативного рынка.</summary>
    public required LlmDerivativesPayload Derivatives { get; init; }

    /// <summary>Состояние стакана заявок.</summary>
    public required LlmOrderBookPayload OrderBook { get; init; }

    /// <summary>Поток сделок за скользящее окно.</summary>
    public required LlmTradeFlowPayload TradeFlow { get; init; }

    /// <summary>Технический анализ на таймфрейме 15 минут.</summary>
    public required LlmTimeframePayload M15 { get; init; }

    /// <summary>Технический анализ на таймфрейме 1 час.</summary>
    public required LlmTimeframePayload H1 { get; init; }

    /// <summary>Технический анализ на таймфрейме 4 часа.</summary>
    public required LlmTimeframePayload H4 { get; init; }

    /// <summary>Технический анализ на дневном таймфрейме.</summary>
    public required LlmTimeframePayload D1 { get; init; }

    /// <summary>Агрегированные оценки сентимента рынка.</summary>
    public required LlmSentimentPayload Sentiment { get; init; }

    /// <summary>Теги для классификации снапшота.</summary>
    public required IReadOnlyList<string> Tags { get; init; }

    /// <summary>
    /// Данные портфеля. Присутствует только если <c>includePortfolio=true</c>.
    /// Если секция запрошена, но данные недоступны — <c>isAvailable: false</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LlmPortfolioPayload? Portfolio { get; init; }

    /// <summary>
    /// Агрегированный текстовый контекст. Зарезервировано для будущего расширения.
    /// На первом этапе не поддерживается — поле не сериализуется в payload.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AggregatedContext { get; init; }
}
