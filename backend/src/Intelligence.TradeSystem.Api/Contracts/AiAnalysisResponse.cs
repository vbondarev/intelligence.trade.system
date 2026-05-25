namespace Intelligence.TradeSystem.Api.Contracts;

/// <summary>
/// Ответ API с результатом AI-анализа по указанному инструменту.
/// </summary>
public sealed record AiAnalysisResponse
{
    /// <summary>Идентификатор биржи, по которой был построен анализ.</summary>
    public required string Exchange { get; init; }

    /// <summary>Тикер торгового инструмента.</summary>
    public required string Symbol { get; init; }

    /// <summary>Категория рынка инструмента.</summary>
    public required string Category { get; init; }

    /// <summary>Итоговый текстовый вывод AI-сервиса.</summary>
    public required string Analysis { get; init; }
}
