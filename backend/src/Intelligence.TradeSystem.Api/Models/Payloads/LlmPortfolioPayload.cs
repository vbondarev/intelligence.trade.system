namespace Intelligence.TradeSystem.Api.Models.Payloads;

/// <summary>Данные портфеля и открытых позиций.</summary>
public sealed record LlmPortfolioPayload
{
    /// <summary>
    /// Признак доступности данных портфеля.
    /// <c>false</c> — секция запрошена, но данные временно недоступны.
    /// </summary>
    public required bool IsAvailable { get; init; }

    public required decimal TotalEquityUsd { get; init; }
    public required decimal TotalUnrealizedPnlUsd { get; init; }
    public required IReadOnlyList<LlmOpenPositionPayload> OpenPositions { get; init; }
}
