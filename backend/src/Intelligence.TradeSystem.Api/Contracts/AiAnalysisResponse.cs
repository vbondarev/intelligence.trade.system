namespace Intelligence.TradeSystem.Api.Contracts;

/// <summary>
/// Минимальный HTTP response contract для AI-analysis.
/// На текущем этапе возвращает итоговый текст AI-анализа и echo ключевых входных параметров.
/// </summary>
public sealed record AiAnalysisResponse
{
    /// <summary>Идентификатор биржи, по которой строился анализ.</summary>
    public required string Exchange { get; init; }

    /// <summary>Тикер инструмента.</summary>
    public required string Symbol { get; init; }

    /// <summary>Категория рынка.</summary>
    public required string Category { get; init; }

    /// <summary>Итоговый текстовый ответ AI-сервиса.</summary>
    public required string Analysis { get; init; }
}

