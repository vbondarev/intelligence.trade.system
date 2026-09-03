using Intelligence.TradeSystem.Domain.History;
using Intelligence.TradeSystem.Domain.Identity;
using System.Collections.ObjectModel;

namespace Intelligence.TradeSystem.Domain;

/// <summary>
/// Бизнес-позиция с идентичностью одного жизненного цикла.
/// </summary>
/// <remarks>
/// <see cref="Id"/>, <see cref="ExchangePositionKey"/> и <see cref="FirstDetectedAt"/> — стабильная
/// идентичность жизненного цикла и не изменяются после создания. Остальное состояние изменяется
/// только через доменные методы (<see cref="ApplyObservation"/>, <see cref="MarkUnknown"/>,
/// <see cref="RefreshFreshness"/>, <see cref="Close"/>), каждый из которых при необходимости
/// добавляет запись в историю существенных изменений (<see cref="Changes"/>).
/// </remarks>
public sealed class Position
{
    private readonly List<PositionChange> _changes = [];
    private readonly ReadOnlyCollection<PositionChange> _readOnlyChanges;

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
        TrackingState = PositionTrackingState.Active;
        _readOnlyChanges = _changes.AsReadOnly();
    }

    public PositionId Id { get; }
    public ExchangePositionKey ExchangePositionKey { get; }
    public MarketCategory MarketCategory { get; }
    public decimal Size { get; private set; }
    public decimal? AverageEntryPrice { get; private set; }
    public decimal? PositionValue { get; private set; }
    public decimal? Leverage { get; private set; }
    public decimal? MarkPrice { get; private set; }
    public decimal? BreakEvenPrice { get; private set; }
    public decimal? LiquidationPrice { get; private set; }
    public decimal? UnrealizedPnl { get; private set; }
    public decimal? TakeProfit { get; private set; }
    public decimal? StopLoss { get; private set; }
    public decimal? TrailingStop { get; private set; }
    public DateTimeOffset FirstDetectedAt { get; }
    public DateTimeOffset LastObservedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>Долговременное состояние жизненного цикла позиции.</summary>
    public PositionTrackingState TrackingState { get; private set; }

    /// <summary>История существенных изменений позиции. Только для чтения.</summary>
    public IReadOnlyList<PositionChange> Changes => _readOnlyChanges;

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
        decimal? trailingStop = null,
        PositionChangeCause cause = PositionChangeCause.InitialObservation)
    {
        if (exchangePositionKey == default)
            throw new ArgumentException("ExchangePositionKey must be initialized.", nameof(exchangePositionKey));

        if (marketCategory is not (MarketCategory.Linear or MarketCategory.Inverse))
            throw new ArgumentOutOfRangeException(
                nameof(marketCategory), marketCategory, "Position market category must be Linear or Inverse.");

        if (size <= 0m)
            throw new ArgumentOutOfRangeException(nameof(size), size, "Position size must be greater than zero.");

        ValidateNonNegative(averageEntryPrice, nameof(averageEntryPrice));
        ValidateNonNegative(positionValue, nameof(positionValue));
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

        var position = new Position(
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

        var snapshot = position.CreateSnapshot();
        position._changes.Add(new PositionChange(
            position.Id, PositionChangeKind.New, cause, lastObservedAt, position.TrackingState, null, snapshot));

        return position;
    }

    /// <summary>
    /// Применяет новое достоверное наблюдение той же биржевой позиции (сопоставленной по
    /// <see cref="ExchangePositionKey"/>): обновляет текущее состояние и, если изменение
    /// существенно, добавляет запись в историю.
    /// </summary>
    /// <returns>
    /// Добавленная запись истории, либо <see langword="null"/>, если наблюдение не привело
    /// к существенному изменению (идемпотентный повтор того же наблюдения).
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Позиция уже закрыта, либо <paramref name="observedAt"/> старше последнего подтверждённого
    /// наблюдения.
    /// </exception>
    public PositionChange? ApplyObservation(
        decimal size,
        DateTimeOffset observedAt,
        decimal? averageEntryPrice = null,
        decimal? positionValue = null,
        decimal? leverage = null,
        decimal? markPrice = null,
        decimal? breakEvenPrice = null,
        decimal? liquidationPrice = null,
        decimal? unrealizedPnl = null,
        decimal? takeProfit = null,
        decimal? stopLoss = null,
        decimal? trailingStop = null,
        PositionChangeCause cause = PositionChangeCause.ExchangeObservation)
    {
        if (TrackingState == PositionTrackingState.Closed)
            throw new InvalidOperationException(
                "Cannot apply an observation to a closed position lifecycle. Create a new Position instead.");

        if (size <= 0m)
            throw new ArgumentOutOfRangeException(nameof(size), size, "Position size must be greater than zero.");

        ValidateNonNegative(averageEntryPrice, nameof(averageEntryPrice));
        ValidateNonNegative(positionValue, nameof(positionValue));
        ValidateNonNegative(markPrice, nameof(markPrice));
        ValidateNonNegative(breakEvenPrice, nameof(breakEvenPrice));
        ValidateNonNegative(liquidationPrice, nameof(liquidationPrice));
        ValidateNonNegative(takeProfit, nameof(takeProfit));
        ValidateNonNegative(stopLoss, nameof(stopLoss));
        ValidateNonNegative(trailingStop, nameof(trailingStop));

        if (leverage is <= 0m)
            throw new ArgumentOutOfRangeException(nameof(leverage), leverage, "Leverage must be greater than zero.");

        if (observedAt < LastObservedAt)
            throw new InvalidOperationException(
                $"Observation at {observedAt:O} is older than the last confirmed observation at " +
                $"{LastObservedAt:O} for position {Id}.");

        var before = CreateSnapshot();
        var wasRecovering = TrackingState is PositionTrackingState.Unknown or PositionTrackingState.Stale;

        var sizeIncreased = size > Size;
        var sizeReduced = size < Size;
        var materialChanged =
            AverageEntryPrice != averageEntryPrice ||
            Leverage != leverage ||
            BreakEvenPrice != breakEvenPrice ||
            LiquidationPrice != liquidationPrice ||
            TakeProfit != takeProfit ||
            StopLoss != stopLoss ||
            TrailingStop != trailingStop;

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
        LastObservedAt = observedAt;
        TrackingState = PositionTrackingState.Active;

        PositionChangeKind? kind = sizeIncreased
            ? PositionChangeKind.Increased
            : sizeReduced
                ? PositionChangeKind.Reduced
                : materialChanged
                    ? PositionChangeKind.Updated
                    : wasRecovering
                        ? PositionChangeKind.Recovered
                        : null;

        if (kind is null)
            return null;

        var effectiveCause = wasRecovering ? PositionChangeCause.ObservationRestored : cause;
        var change = new PositionChange(
            Id, kind.Value, effectiveCause, observedAt, TrackingState, before, CreateSnapshot());
        _changes.Add(change);
        return change;
    }

    /// <summary>
    /// Отмечает, что последняя попытка получить состояние позиции не дала достаточных данных.
    /// Идемпотентно: повторный вызов при уже установленном <see cref="PositionTrackingState.Unknown"/>
    /// не создаёт дубликат записи истории. Не изменяет <see cref="LastObservedAt"/>.
    /// </summary>
    public PositionChange? MarkUnknown(
        DateTimeOffset asOf, PositionChangeCause cause = PositionChangeCause.PositionsObservationFailed)
    {
        if (asOf < LastObservedAt)
            throw new InvalidOperationException(
                $"Observation at {asOf:O} is older than the last confirmed observation at " +
                $"{LastObservedAt:O} for position {Id}.");

        if (TrackingState is PositionTrackingState.Closed or PositionTrackingState.Unknown)
            return null;

        var snapshot = CreateSnapshot();
        TrackingState = PositionTrackingState.Unknown;
        var change = new PositionChange(Id, PositionChangeKind.MarkedUnknown, cause, asOf, TrackingState, snapshot, snapshot);
        _changes.Add(change);
        return change;
    }

    /// <summary>
    /// Переводит позицию в состояние <see cref="PositionTrackingState.Stale"/>, если последнее
    /// подтверждённое наблюдение старше <paramref name="staleAfter"/> относительно
    /// <paramref name="now"/>. Не влияет на закрытые позиции и не переопределяет
    /// <see cref="PositionTrackingState.Unknown"/> (восстановление из этих состояний
    /// возможно только новым подтверждённым наблюдением).
    /// </summary>
    public PositionChange? RefreshFreshness(
        DateTimeOffset now, TimeSpan staleAfter, PositionChangeCause cause = PositionChangeCause.FreshnessExpired)
    {
        if (staleAfter < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(staleAfter), staleAfter, "Staleness threshold cannot be negative.");

        if (now < LastObservedAt)
            throw new InvalidOperationException(
                $"Freshness cannot be evaluated at {now:O} before the last confirmed observation at " +
                $"{LastObservedAt:O} for position {Id}.");

        if (TrackingState != PositionTrackingState.Active)
            return null;

        if (now - LastObservedAt <= staleAfter)
            return null;

        var snapshot = CreateSnapshot();
        TrackingState = PositionTrackingState.Stale;
        var change = new PositionChange(Id, PositionChangeKind.MarkedStale, cause, now, TrackingState, snapshot, snapshot);
        _changes.Add(change);
        return change;
    }

    /// <summary>
    /// Закрывает позицию: она достоверно отсутствовала в полном снимке своей области (scope).
    /// Идемпотентно: повторное закрытие уже закрытой позиции не создаёт дубликат записи истории.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="observedAt"/> старше последнего подтверждённого наблюдения.
    /// </exception>
    public PositionChange? Close(
        DateTimeOffset observedAt, PositionChangeCause cause = PositionChangeCause.MissingFromCompleteObservation)
    {
        if (TrackingState == PositionTrackingState.Closed)
            return null;

        if (observedAt < LastObservedAt)
            throw new InvalidOperationException(
                $"Observation at {observedAt:O} is older than the last confirmed observation at " +
                $"{LastObservedAt:O} for position {Id}.");

        var snapshot = CreateSnapshot();
        TrackingState = PositionTrackingState.Closed;
        ClosedAt = observedAt;
        var change = new PositionChange(Id, PositionChangeKind.Closed, cause, observedAt, TrackingState, snapshot, snapshot);
        _changes.Add(change);
        return change;
    }

    private PositionStateSnapshot CreateSnapshot() => new(
        Size,
        AverageEntryPrice,
        PositionValue,
        Leverage,
        MarkPrice,
        BreakEvenPrice,
        LiquidationPrice,
        UnrealizedPnl,
        TakeProfit,
        StopLoss,
        TrailingStop);

    private static void ValidateNonNegative(decimal? value, string parameterName)
    {
        if (value < 0m)
            throw new ArgumentOutOfRangeException(parameterName, value, "Price cannot be negative.");
    }
}
