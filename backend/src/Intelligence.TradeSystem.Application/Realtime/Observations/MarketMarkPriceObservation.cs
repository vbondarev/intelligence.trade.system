using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Application.Realtime.Observations;

/// <summary>
/// Публичное наблюдение mark price инструмента. Не содержит данных пользователя или аккаунта.
/// </summary>
public sealed record MarketMarkPriceObservation
{
    public required ExchangeId Exchange { get; init; }

    public required MarketCategory Category { get; init; }

    public required string Symbol { get; init; }

    /// <summary>
    /// Mark price; <see langword="null"/>, если сообщение его не содержит (например, delta без
    /// <c>markPrice</c>) или значение невозможно. Ранее подтверждённая цена при этом не сбрасывается.
    /// </summary>
    public decimal? MarkPrice { get; init; }

    public required MarketPriceUpdateKind UpdateKind { get; init; }

    /// <summary>Время данных по часам биржи; <see langword="null"/>, если отметка недостоверна.</summary>
    public DateTimeOffset? ObservedAt { get; init; }

    public required DateTimeOffset ReceivedAt { get; init; }
}
