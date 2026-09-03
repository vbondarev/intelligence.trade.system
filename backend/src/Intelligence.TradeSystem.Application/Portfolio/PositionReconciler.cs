using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.History;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Portfolio;

/// <summary>
/// Сопоставляет <see cref="OpenPositionsObservation"/> с текущим набором бизнес-позиций
/// (<see cref="Position"/>) одного биржевого аккаунта и обновляет их жизненный цикл.
/// </summary>
/// <remarks>
/// Не обращается к базе данных. Мутирует переданные <see cref="Position"/> напрямую через
/// доменные методы; вновь созданные позиции возвращаются в результате отдельно.
/// </remarks>
public static class PositionReconciler
{
    /// <summary>
    /// Сопоставляет наблюдение с текущими позициями аккаунта.
    /// </summary>
    /// <param name="exchangeAccountId">Аккаунт, к которому относится наблюдение.</param>
    /// <param name="trackedPositions">Текущие известные бизнес-позиции (любых аккаунтов).</param>
    /// <param name="observation">Результат наблюдения открытых позиций.</param>
    /// <param name="now">Текущее время, используемое для проверки свежести данных.</param>
    /// <param name="staleAfter">Допустимый возраст последнего подтверждённого наблюдения.</param>
    public static PositionReconciliationResult Reconcile(
        ExchangeAccountId exchangeAccountId,
        IReadOnlyCollection<Position> trackedPositions,
        OpenPositionsObservation observation,
        DateTimeOffset now,
        TimeSpan staleAfter)
    {
        ArgumentNullException.ThrowIfNull(trackedPositions);
        ArgumentNullException.ThrowIfNull(observation);

        var warnings = new List<string>();
        var changes = new List<PositionChange>();
        var newPositions = new List<Position>();

        // Freshness is a generic time-based fact independent of any particular observation's
        // scope, so it is refreshed for every known position regardless of account/category/symbol.
        foreach (var position in trackedPositions)
        {
            var staleChange = position.RefreshFreshness(now, staleAfter);
            if (staleChange is not null)
                changes.Add(staleChange);
        }

        bool InScope(Position position) =>
            position.ExchangePositionKey.ExchangeAccountId == exchangeAccountId &&
            position.MarketCategory == observation.Category &&
            (observation.Symbol is null ||
             string.Equals(
                 position.ExchangePositionKey.InstrumentId.Value, observation.Symbol,
                 StringComparison.OrdinalIgnoreCase));

        if (observation.Status == OpenPositionsObservationStatus.Failed)
        {
            foreach (var position in trackedPositions)
            {
                if (position.TrackingState == PositionTrackingState.Closed || !InScope(position))
                    continue;

                var change = position.MarkUnknown(observation.ObservedAt, PositionChangeCause.PositionsObservationFailed);
                if (change is not null)
                    changes.Add(change);
            }

            return new PositionReconciliationResult(newPositions, changes, warnings);
        }

        // Only currently active (non-closed) lifecycles can be matched and updated by a new
        // observation. A previously closed position with a matching key must not be reopened.
        var activeByKey = trackedPositions
            .Where(position =>
                position.ExchangePositionKey.ExchangeAccountId == exchangeAccountId &&
                position.TrackingState != PositionTrackingState.Closed)
            .ToDictionary(position => position.ExchangePositionKey);

        var observedKeys = new HashSet<ExchangePositionKey>();
        var hasMappingIssues = false;

        foreach (var observed in observation.Positions)
        {
            if (!OpenPositionKeyMapper.TryMapKey(observed, exchangeAccountId, out var key, out var warning))
            {
                hasMappingIssues = true;
                if (warning is not null)
                    warnings.Add(warning);
                continue;
            }

            if (!observedKeys.Add(key))
            {
                hasMappingIssues = true;
                warnings.Add($"Duplicate exchange position key observed and skipped: {key}.");
                continue;
            }

            if (activeByKey.TryGetValue(key, out var existing))
            {
                var change = existing.ApplyObservation(
                    observed.Size,
                    observation.ObservedAt,
                    observed.AvgPrice,
                    observed.PositionValue,
                    observed.Leverage,
                    observed.MarkPrice,
                    observed.BreakEvenPrice,
                    observed.LiquidationPrice,
                    observed.UnrealizedPnl,
                    observed.TakeProfit,
                    observed.StopLoss,
                    observed.TrailingStop,
                    PositionChangeCause.ExchangeObservation);
                if (change is not null)
                    changes.Add(change);
            }
            else
            {
                var created = Position.Create(
                    key,
                    observed.Category,
                    observed.Size,
                    observation.ObservedAt,
                    observation.ObservedAt,
                    observed.AvgPrice,
                    observed.PositionValue,
                    observed.Leverage,
                    observed.MarkPrice,
                    observed.BreakEvenPrice,
                    observed.LiquidationPrice,
                    observed.UnrealizedPnl,
                    observed.TakeProfit,
                    observed.StopLoss,
                    observed.TrailingStop);
                newPositions.Add(created);
                changes.AddRange(created.Changes);
            }
        }

        // A Complete observation can only prove absence (and therefore closure) when it covers
        // its scope with no unresolved mapping ambiguity. Partial observations, or Complete
        // observations degraded by unmappable/duplicate entries, can never close a position.
        var canInferClosed = observation.Status == OpenPositionsObservationStatus.Complete && !hasMappingIssues;
        var missingCause = canInferClosed
            ? PositionChangeCause.MissingFromCompleteObservation
            : PositionChangeCause.PartialObservation;

        foreach (var position in trackedPositions)
        {
            if (position.TrackingState == PositionTrackingState.Closed || !InScope(position))
                continue;

            if (observedKeys.Contains(position.ExchangePositionKey))
                continue;

            var change = canInferClosed
                ? position.Close(observation.ObservedAt, missingCause)
                : position.MarkUnknown(observation.ObservedAt, missingCause);
            if (change is not null)
                changes.Add(change);
        }

        return new PositionReconciliationResult(newPositions, changes, warnings);
    }
}
