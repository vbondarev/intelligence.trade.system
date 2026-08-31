namespace Intelligence.TradeSystem.Api.Models.MarketFacts;

/// <summary>
/// Canonical facts payload v1 для downstream-агентов (OpenClaw / Mr Crypto).
/// Содержит детерминированные факты и labels без готового текстового анализа.
/// </summary>
public sealed record MarketFactsPayload
{
    /// <summary>Актуальная версия схемы.</summary>
    public const string CurrentSchemaVersion = "market-facts/v1";

    /// <summary>Версия схемы payload. Текущая версия: <c>market-facts/v1</c>.</summary>
    public required string SchemaVersion { get; init; }

    /// <summary>Источник данных и мета-информация о снапшоте.</summary>
    public required MarketFactsSourcePayload Source { get; init; }

    /// <summary>Контекст режима анализа.</summary>
    public required MarketFactsAnalysisContextPayload AnalysisContext { get; init; }

    /// <summary>Качество и полнота данных.</summary>
    public required MarketFactsDataQualityPayload DataQuality { get; init; }

    /// <summary>Текущее состояние цены.</summary>
    public required MarketFactsPricePayload Price { get; init; }

    /// <summary>Данные деривативного рынка.</summary>
    public required MarketFactsDerivativesPayload Derivatives { get; init; }

    /// <summary>Состояние стакана заявок.</summary>
    public required MarketFactsOrderBookPayload OrderBook { get; init; }

    /// <summary>Поток сделок за скользящее окно.</summary>
    public required MarketFactsTradeFlowPayload TradeFlow { get; init; }

    /// <summary>
    /// Технический анализ по таймфреймам.
    /// Ключ — строковое обозначение таймфрейма, например <c>15m</c>, <c>1h</c>, <c>4h</c>, <c>1d</c>.
    /// </summary>
    public required IReadOnlyDictionary<string, MarketFactsTimeframePayload> Timeframes { get; init; }

    /// <summary>Агрегированные уровни поддержки и сопротивления.</summary>
    public required MarketFactsLevelsPayload Levels { get; init; }

    /// <summary>Агрегированные оценки внутреннего сентимента рынка.</summary>
    public required MarketFactsInternalSentimentPayload MarketInternalSentiment { get; init; }

    /// <summary>Теги для классификации снапшота.</summary>
    public required IReadOnlyList<string> Tags { get; init; }
}
