namespace Intelligence.TradeSystem.Domain;

/// <summary>
/// Результат наблюдения открытых позиций на бирже для конкретной области запроса (scope).
/// </summary>
/// <remarks>
/// Явно различает успешный снимок без позиций (<see cref="OpenPositionsObservationStatus.Complete"/>
/// с пустым <see cref="Positions"/>) от неудачной попытки получить данные
/// (<see cref="OpenPositionsObservationStatus.Failed"/>). Наблюдение всегда знает область
/// запроса (<see cref="Category"/> и опциональный <see cref="Symbol"/>): <c>Complete</c> для
/// одного символа не является доказательством отсутствия позиций по другим символам или
/// категориям рынка того же аккаунта.
/// </remarks>
public sealed record OpenPositionsObservation
{
    /// <summary>Достоверность и полнота результата.</summary>
    public required OpenPositionsObservationStatus Status { get; init; }

    /// <summary>Категория рынка, для которой запрашивались позиции.</summary>
    public required MarketCategory Category { get; init; }

    /// <summary>
    /// Символ инструмента, если запрос был ограничен одним инструментом.
    /// <see langword="null"/> означает отсутствие фильтра — область охватывает все
    /// инструменты запрошенной категории на этом аккаунте.
    /// </summary>
    public string? Symbol { get; init; }

    /// <summary>Момент времени, к которому относится наблюдение.</summary>
    public required DateTimeOffset ObservedAt { get; init; }

    /// <summary>
    /// Достоверно наблюдаемые открытые позиции. При <see cref="OpenPositionsObservationStatus.Failed"/>
    /// всегда пуст, но это не означает отсутствие открытых позиций.
    /// </summary>
    public required IReadOnlyList<OpenPosition> Positions { get; init; }

    /// <summary>Описание ошибки для <see cref="OpenPositionsObservationStatus.Failed"/> и
    /// <see cref="OpenPositionsObservationStatus.Partial"/>, если применимо.</summary>
    public string? Error { get; init; }

    public static OpenPositionsObservation Complete(
        MarketCategory category,
        string? symbol,
        DateTimeOffset observedAt,
        IReadOnlyList<OpenPosition> positions) =>
        new()
        {
            Status = OpenPositionsObservationStatus.Complete,
            Category = category,
            Symbol = symbol,
            ObservedAt = observedAt,
            Positions = positions,
        };

    public static OpenPositionsObservation Partial(
        MarketCategory category,
        string? symbol,
        DateTimeOffset observedAt,
        IReadOnlyList<OpenPosition> positions,
        string? error = null) =>
        new()
        {
            Status = OpenPositionsObservationStatus.Partial,
            Category = category,
            Symbol = symbol,
            ObservedAt = observedAt,
            Positions = positions,
            Error = error,
        };

    public static OpenPositionsObservation Failed(
        MarketCategory category,
        string? symbol,
        DateTimeOffset observedAt,
        string error) =>
        new()
        {
            Status = OpenPositionsObservationStatus.Failed,
            Category = category,
            Symbol = symbol,
            ObservedAt = observedAt,
            Positions = [],
            Error = error,
        };
}
