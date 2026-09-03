using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Domain.Assessments;

/// <summary>
/// Воспроизводимая идентичность входов оценки. До появления постоянных version IDs
/// используется идентификатор сущности вместе с timestamp соответствующего snapshot.
/// </summary>
public readonly record struct PositionAssessmentInputVersions
{
    public PositionId PositionId { get; }
    public ExchangeAccountId ExchangeAccountId { get; }
    public InstrumentId InstrumentId { get; }
    public DateTimeOffset PositionObservedAt { get; }
    public DateTimeOffset PortfolioCalculatedAt { get; }
    public DateTimeOffset MarketCapturedAt { get; }

    public PositionAssessmentInputVersions(
        PositionId positionId,
        ExchangeAccountId exchangeAccountId,
        InstrumentId instrumentId,
        DateTimeOffset positionObservedAt,
        DateTimeOffset portfolioCalculatedAt,
        DateTimeOffset marketCapturedAt)
    {
        if (positionId == default)
            throw new ArgumentException("PositionId must be initialized.", nameof(positionId));
        if (exchangeAccountId == default)
            throw new ArgumentException("ExchangeAccountId must be initialized.", nameof(exchangeAccountId));
        if (instrumentId.Value is null)
            throw new ArgumentException("InstrumentId must be initialized.", nameof(instrumentId));

        PositionId = positionId;
        ExchangeAccountId = exchangeAccountId;
        InstrumentId = instrumentId;
        PositionObservedAt = positionObservedAt;
        PortfolioCalculatedAt = portfolioCalculatedAt;
        MarketCapturedAt = marketCapturedAt;
    }

    public static PositionAssessmentInputVersions Create(
        PositionId positionId,
        ExchangeAccountId exchangeAccountId,
        InstrumentId instrumentId,
        DateTimeOffset positionObservedAt,
        DateTimeOffset portfolioCalculatedAt,
        DateTimeOffset marketCapturedAt) =>
        new(positionId, exchangeAccountId, instrumentId, positionObservedAt,
            portfolioCalculatedAt, marketCapturedAt);
}
