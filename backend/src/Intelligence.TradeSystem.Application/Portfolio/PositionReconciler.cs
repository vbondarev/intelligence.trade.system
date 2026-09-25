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
        TimeSpan staleAfter,
        bool applyObservation = true)
    {
        ArgumentNullException.ThrowIfNull(trackedPositions);
        ArgumentNullException.ThrowIfNull(observation);

        var warnings = new List<string>();
        var changes = new List<PositionChange>();
        var newPositions = new List<Position>();
        var positionsToPersist = new HashSet<Position>();

        bool InScope(Position position) =>
            position.ExchangePositionKey.ExchangeAccountId == exchangeAccountId &&
            position.MarketCategory == observation.Category &&
            (observation.Symbol is null ||
             string.Equals(
                 position.ExchangePositionKey.InstrumentId.Value, observation.Symbol.Trim(),
                 StringComparison.OrdinalIgnoreCase));

        void RefreshAccountFreshness()
        {
            // Свежесть не зависит от category и symbol scope, но reconciliation не должна
            // изменять позицию, принадлежащую другому биржевому аккаунту.
            foreach (var position in trackedPositions.Concat(newPositions))
            {
                if (position.ExchangePositionKey.ExchangeAccountId != exchangeAccountId)
                    continue;

                var staleChange = position.RefreshFreshness(now, staleAfter);
                if (staleChange is not null)
                {
                    changes.Add(staleChange);
                    if (trackedPositions.Contains(position))
                    {
                        positionsToPersist.Add(position);
                    }
                }
            }
        }

        if (!applyObservation)
        {
            RefreshAccountFreshness();
            return new PositionReconciliationResult(newPositions, changes, warnings)
            {
                PositionsToPersist = positionsToPersist.ToArray(),
                IsFullyReconciled = false,
            };
        }

        // Ответ, который старше активного lifecycle, не может подтверждать новое состояние или
        // отсутствие. Его данные игнорируются, но свежесть по-прежнему оценивается.
        var scopedActivePositions = trackedPositions
            .Where(position =>
                position.TrackingState != PositionTrackingState.Closed &&
                InScope(position))
            .ToArray();
        var hasNewerScopedObservation = scopedActivePositions
            .Any(position => position.LastObservedAt > observation.ObservedAt);
        if (hasNewerScopedObservation)
        {
            RefreshAccountFreshness();
            return new PositionReconciliationResult(newPositions, changes, warnings)
            {
                PositionsToPersist = positionsToPersist.ToArray(),
                IsFullyReconciled = false,
            };
        }

        if (observation.Status == OpenPositionsObservationStatus.Failed)
        {
            foreach (var position in trackedPositions)
            {
                if (position.TrackingState == PositionTrackingState.Closed || !InScope(position))
                    continue;

                if (observation.ObservedAt <= position.LastObservedAt)
                    continue;

                var change = position.MarkUnknown(observation.ObservedAt, PositionChangeCause.PositionsObservationFailed);
                if (change is not null)
                {
                    changes.Add(change);
                    positionsToPersist.Add(position);
                }
            }

            RefreshAccountFreshness();
            return new PositionReconciliationResult(newPositions, changes, warnings)
            {
                PositionsToPersist = positionsToPersist.ToArray(),
                IsFullyReconciled = false,
            };
        }

        // С новым наблюдением можно сопоставлять и обновлять только активные (не закрытые)
        // lifecycles. Ранее закрытая позиция с совпадающим ключом не должна открываться повторно.
        var activeByKey = trackedPositions
            .Where(position =>
                position.TrackingState != PositionTrackingState.Closed &&
                InScope(position))
            .ToDictionary(position => position.ExchangePositionKey);

        var observedKeys = new HashSet<ExchangePositionKey>();
        var hasMappingIssues = false;

        foreach (var observed in observation.Positions)
        {
            if (observed.Category != observation.Category)
            {
                hasMappingIssues = true;
                warnings.Add(
                    $"Skipped {observed.Symbol} ({observed.Category}): position category does not match observation category {observation.Category}.");
                continue;
            }

            if (!OpenPositionKeyMapper.TryMapKey(observed, exchangeAccountId, out var key, out var warning))
            {
                hasMappingIssues = true;
                if (warning is not null)
                    warnings.Add(warning);
                continue;
            }

            if (observation.Symbol is not null &&
                !string.Equals(key.InstrumentId.Value, observation.Symbol.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                hasMappingIssues = true;
                warnings.Add(
                    $"Skipped {observed.Symbol} ({observed.Category}): position symbol does not match observation symbol {observation.Symbol}.");
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
                if (observation.ObservedAt <= existing.LastObservedAt)
                    continue;

                positionsToPersist.Add(existing);
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
                var latestClosedLifecycle = trackedPositions
                    .Where(position =>
                        position.TrackingState == PositionTrackingState.Closed &&
                        position.ExchangePositionKey == key &&
                        position.MarketCategory == observation.Category)
                    .OrderByDescending(position => position.ClosedAt)
                    .FirstOrDefault();

                if (latestClosedLifecycle?.ClosedAt is { } closedAt && observation.ObservedAt <= closedAt)
                {
                    hasMappingIssues = true;
                    warnings.Add(
                        $"Skipped {key}: observation at {observation.ObservedAt:O} is not newer than the previous lifecycle closure at {closedAt:O}.");
                    continue;
                }

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

        // Наблюдение Complete может подтверждать отсутствие (и тем самым закрытие), только если оно охватывает
        // свою область без неразрешённой неоднозначности mapping. Partial-наблюдения и Complete-наблюдения,
        // ухудшенные из-за несопоставимых или дублирующихся записей, никогда не закрывают позицию.
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

            if (observation.ObservedAt <= position.LastObservedAt)
                continue;

            var change = canInferClosed
                ? position.Close(observation.ObservedAt, missingCause)
                : position.MarkUnknown(observation.ObservedAt, missingCause);
            if (change is not null)
            {
                changes.Add(change);
                positionsToPersist.Add(position);
            }
        }

        RefreshAccountFreshness();
        return new PositionReconciliationResult(newPositions, changes, warnings)
        {
            PositionsToPersist = positionsToPersist.ToArray(),
            IsFullyReconciled = canInferClosed,
        };
    }
}
