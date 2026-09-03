using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Domain;

/// <summary>
/// Бизнес-позиция с идентичностью одного жизненного цикла.
/// </summary>
public sealed class Position
{
    private Position(
        PositionId id,
        ExchangePositionKey exchangePositionKey,
        MarketCategory marketCategory,
        decimal size,
        decimal? averageEntryPrice,
        decimal? positionValue,
        decimal? leverage,
        decimal? markPrice,
        decimal? breakEvenPrice,
        decimal? liquidationPrice,
        decimal? unrealizedPnl,
        decimal? takeProfit,
        decimal? stopLoss,
        decimal? trailingStop,
        DateTimeOffset firstDetectedAt,
        DateTimeOffset lastObservedAt)
    {
        Id = id;
        ExchangePositionKey = exchangePositionKey;
        MarketCategory = marketCategory;
        Size = size;
        AverageEntryPrice = averageEntryPrice;
        PositionValue = positionValue;
        Leverage = leverage;
        MarkPrice = markPrice;
        BreakEvenPrice = breakEvenPrice;
        LiquidationPrice = liquidationPrice;
        UnrealizedPnl = unrealizedPnl;
        TakeProfit = takeProfit;
        StopLoss = stopLoss;
        TrailingStop = trailingStop;
        FirstDetectedAt = firstDetectedAt;
        LastObservedAt = lastObservedAt;
    }

    public PositionId Id { get; }
    public ExchangePositionKey ExchangePositionKey { get; }
    public MarketCategory MarketCategory { get; }
    public decimal Size { get; }
    public decimal? AverageEntryPrice { get; }
    public decimal? PositionValue { get; }
    public decimal? Leverage { get; }
    public decimal? MarkPrice { get; }
    public decimal? BreakEvenPrice { get; }
    public decimal? LiquidationPrice { get; }
    public decimal? UnrealizedPnl { get; }
    public decimal? TakeProfit { get; }
    public decimal? StopLoss { get; }
    public decimal? TrailingStop { get; }
    public DateTimeOffset FirstDetectedAt { get; }
    public DateTimeOffset LastObservedAt { get; }

    public static Position Create(
        ExchangePositionKey exchangePositionKey,
        MarketCategory marketCategory,
        decimal size,
        DateTimeOffset firstDetectedAt,
        DateTimeOffset lastObservedAt,
        decimal? averageEntryPrice = null,
        decimal? positionValue = null,
        decimal? leverage = null,
        decimal? markPrice = null,
        decimal? breakEvenPrice = null,
        decimal? liquidationPrice = null,
        decimal? unrealizedPnl = null,
        decimal? takeProfit = null,
        decimal? stopLoss = null,
        decimal? trailingStop = null)
    {
        if (exchangePositionKey == default)
            throw new ArgumentException("ExchangePositionKey must be initialized.", nameof(exchangePositionKey));

        if (size <= 0m)
            throw new ArgumentOutOfRangeException(nameof(size), size, "Position size must be greater than zero.");

        ValidateNonNegative(averageEntryPrice, nameof(averageEntryPrice));
        ValidateNonNegative(markPrice, nameof(markPrice));
        ValidateNonNegative(breakEvenPrice, nameof(breakEvenPrice));
        ValidateNonNegative(liquidationPrice, nameof(liquidationPrice));
        ValidateNonNegative(takeProfit, nameof(takeProfit));
        ValidateNonNegative(stopLoss, nameof(stopLoss));
        ValidateNonNegative(trailingStop, nameof(trailingStop));

        if (leverage is <= 0m)
            throw new ArgumentOutOfRangeException(nameof(leverage), leverage, "Leverage must be greater than zero.");

        if (lastObservedAt < firstDetectedAt)
            throw new ArgumentException(
                "LastObservedAt must be greater than or equal to FirstDetectedAt.", nameof(lastObservedAt));

        return new Position(
            PositionId.New(),
            exchangePositionKey,
            marketCategory,
            size,
            averageEntryPrice,
            positionValue,
            leverage,
            markPrice,
            breakEvenPrice,
            liquidationPrice,
            unrealizedPnl,
            takeProfit,
            stopLoss,
            trailingStop,
            firstDetectedAt,
            lastObservedAt);
    }

    private static void ValidateNonNegative(decimal? value, string parameterName)
    {
        if (value < 0m)
            throw new ArgumentOutOfRangeException(parameterName, value, "Price cannot be negative.");
    }
}
