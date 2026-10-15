using System.Globalization;
using System.Text;
using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Accounts.Credentials;
using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Events;
using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Application.Realtime.Observations;
using Intelligence.TradeSystem.Application.Realtime.State;
using Intelligence.TradeSystem.Application.Time;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.History;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Portfolio;
using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Application.Realtime;

/// <summary>
/// Применяет одно приватное потоковое наблюдение позиции к бизнес-состоянию аккаунта.
/// </summary>
/// <remarks>
/// <para>
/// Порядок наблюдений определяется per-position курсором и никогда не сравнивается с
/// <see cref="ExchangeAccount.LastAppliedPositionsObservationAt"/> как с общей последовательностью.
/// Позиция, сводка, курсор и события outbox сохраняются одной транзакцией с блокировками в порядке
/// позиции → аккаунт → состояние наблюдения.
/// </para>
/// <para>
/// Нулевой размер и смена направления в слоте позиции не применяются напрямую: состояние слота
/// подтверждается scoped REST-наблюдением, а закрытие выполняется существующим
/// <see cref="PositionReconciler"/> только по полному снимку. Автоматическая evaluation не запускается.
/// Открытие, не более новое, чем watermark REST-наблюдения позиций аккаунта, тоже подтверждается
/// scoped REST-наблюдением: watermark не отличает полный снимок от неуспешной попытки.
/// Снимок подтверждает событие, только если запрошен не раньше получения сообщения и новее этого
/// watermark; иначе состояние не меняется и требуется recovery.
/// </para>
/// </remarks>
public sealed class RealtimePositionReconciliationService(
    IExchangeAccountRepository accountRepository,
    IExchangeAccountCredentialStore credentialStore,
    IPrivateAccountProviderFactory providerFactory,
    IPositionRepository positionRepository,
    IPortfolioStateRepository portfolioStateRepository,
    IPositionObservationCursorStore cursorStore,
    IRealtimeObservationStateStore observationStateStore,
    IExchangeAccountSyncTransaction persistenceTransaction,
    IApplicationEventOutbox applicationEventOutbox,
    TimeProvider? timeProvider = null)
{
    private const int MaxPersistenceAttempts = 3;
    private const string FingerprintVersion = "p1";
    private static readonly TimeSpan PortfolioStaleAfter = TimeSpan.FromMinutes(5);
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    /// <summary>
    /// Применяет наблюдение от имени поколения наблюдателя <paramref name="generation"/>.
    /// </summary>
    /// <param name="confirmWithRest">
    /// Подтвердить состояние слота scoped REST-наблюдением даже для открытой позиции: используется,
    /// когда наблюдение могло быть уже отражено конкурентным REST-снимком.
    /// </param>
    /// <exception cref="ConcurrencyConflictException">Исчерпан лимит повторов сохранения.</exception>
    public async Task<RealtimeObservationApplyResult> ApplyAsync(
        UserId userId,
        long generation,
        PrivatePositionObservation observation,
        bool confirmWithRest = false,
        CancellationToken cancellationToken = default)
    {
        if (userId == default)
        {
            throw new ArgumentException("UserId must be initialized.", nameof(userId));
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(generation, 0);
        ArgumentNullException.ThrowIfNull(observation);
        if (observation.ExchangeAccountId == default)
        {
            throw new ArgumentException("ExchangeAccountId must be initialized.", nameof(observation));
        }

        var rejection = TryPrepare(observation, out var prepared);
        if (rejection is not null)
        {
            return rejection;
        }

        if (!confirmWithRest)
        {
            var direct = await ApplyDirectAsync(userId, generation, prepared!, cancellationToken)
                .ConfigureAwait(false);
            if (direct is not null)
            {
                return direct;
            }
        }

        return await ConfirmWithRestAsync(userId, generation, prepared!, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Момент, к которому относится наблюдение, по часам backend: время формирования сообщения или
    /// изменения позиции биржей, но не позже получения. <see langword="null"/>, если биржевые
    /// отметки недоступны.
    /// </summary>
    internal static DateTimeOffset? ResolveObservedAt(PrivatePositionObservation observation)
    {
        var exchangeTime = observation.MessageCreatedAt ?? observation.ProviderUpdatedAt;
        if (exchangeTime is not { } value || observation.ReceivedAt == default)
        {
            return null;
        }

        return value < observation.ReceivedAt ? value : observation.ReceivedAt;
    }

    private static RealtimeObservationApplyResult? TryPrepare(
        PrivatePositionObservation observation,
        out PreparedObservation? prepared)
    {
        prepared = null;
        switch (observation.Kind)
        {
            case PrivatePositionObservationKind.Duplicate:
                return RealtimeObservationApplyResult.Duplicate();
            case PrivatePositionObservationKind.Unsupported:
                return RealtimeObservationApplyResult.Ignored(RealtimeObservationApplyResult.UnsupportedObservation);
            case PrivatePositionObservationKind.Ambiguous:
                return RealtimeObservationApplyResult.RecoveryRequired(RealtimeObservationApplyResult.AmbiguousObservation);
            case PrivatePositionObservationKind.Open:
            case PrivatePositionObservationKind.PotentialClose:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(observation), observation.Kind, "Unknown observation kind.");
        }

        if (observation.Category != MarketCategory.Linear)
        {
            return RealtimeObservationApplyResult.Ignored(RealtimeObservationApplyResult.UnsupportedCategory);
        }

        PositionObservationCursorKey cursorKey;
        try
        {
            cursorKey = PositionObservationCursorKey.Create(
                observation.ExchangeAccountId,
                MarketCategory.Linear,
                InstrumentId.From(observation.Symbol),
                observation.PositionIdx);
        }
        catch (ArgumentException)
        {
            return RealtimeObservationApplyResult.RecoveryRequired(RealtimeObservationApplyResult.InvalidIdentity);
        }

        if (ResolveObservedAt(observation) is not { } observedAt)
        {
            return RealtimeObservationApplyResult.RecoveryRequired(RealtimeObservationApplyResult.MissingObservationTime);
        }

        var sequence = observation.ProviderSequence is > 0 ? observation.ProviderSequence : null;
        if (sequence is null && observation.ProviderUpdatedAt is null)
        {
            return RealtimeObservationApplyResult.RecoveryRequired(RealtimeObservationApplyResult.MissingRevision);
        }

        ExchangePositionKey? key = null;
        if (observation.Kind == PrivatePositionObservationKind.Open)
        {
            if (observation.Position is not { } position ||
                observation.SettlementAsset is not { } settlementAsset ||
                position.Size <= 0m ||
                position.Category != MarketCategory.Linear ||
                position.PositionIdx != observation.PositionIdx ||
                position.SettlementAsset is { } positionSettlement && positionSettlement != settlementAsset ||
                !string.Equals(position.Symbol, observation.Symbol, StringComparison.Ordinal))
            {
                return RealtimeObservationApplyResult.RecoveryRequired(RealtimeObservationApplyResult.IncompleteObservation);
            }

            if (!OpenPositionKeyMapper.TryMapKey(position, observation.ExchangeAccountId, out var mappedKey, out _))
            {
                return RealtimeObservationApplyResult.RecoveryRequired(RealtimeObservationApplyResult.InvalidIdentity);
            }

            key = mappedKey;
        }

        prepared = new PreparedObservation(
            observation,
            cursorKey,
            key,
            observedAt,
            sequence,
            observation.ProviderUpdatedAt,
            ComputeFingerprint(observation));
        return null;
    }

    /// <returns><see langword="null"/>, если состояние слота нужно подтвердить REST-наблюдением.</returns>
    private async Task<RealtimeObservationApplyResult?> ApplyDirectAsync(
        UserId userId,
        long generation,
        PreparedObservation prepared,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            RealtimeObservationApplyResult? result = null;
            var requiresRestConfirmation = false;
            try
            {
                await persistenceTransaction
                    .ExecuteAsync(
                        async token =>
                        {
                            var (locked, rejection) = await LockAsync(userId, generation, prepared, token)
                                .ConfigureAwait(false);
                            if (locked is null)
                            {
                                result = rejection;
                                return;
                            }

                            var slot = locked.Positions
                                .Select(versioned => versioned.Value)
                                .Where(position =>
                                    position.TrackingState != PositionTrackingState.Closed &&
                                    IsInSlot(position, prepared))
                                .ToArray();

                            if (prepared.Observation.Kind == PrivatePositionObservationKind.PotentialClose)
                            {
                                if (slot.Length > 0)
                                {
                                    requiresRestConfirmation = true;
                                    return;
                                }

                                await SaveCursorAsync(userId, generation, prepared, locked.Cursor, token)
                                    .ConfigureAwait(false);
                                result = RealtimeObservationApplyResult.Applied();
                                return;
                            }

                            var key = prepared.Key!.Value;
                            if (slot.Any(position => position.ExchangePositionKey != key))
                            {
                                requiresRestConfirmation = true;
                                return;
                            }

                            if (slot.Length == 0)
                            {
                                result = await OpenLifecycleAsync(userId, generation, prepared, locked, token)
                                    .ConfigureAwait(false);
                                requiresRestConfirmation = result is null;
                                return;
                            }

                            result = await UpdateLifecycleAsync(userId, generation, prepared, locked, slot[0], token)
                                .ConfigureAwait(false);
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (IsRetryable(exception, attempt))
            {
                continue;
            }
            catch (Exception exception) when (exception is IPositionActiveKeyViolation)
            {
                throw ActiveKeyRaceLost(prepared, exception);
            }

            if (requiresRestConfirmation)
            {
                return null;
            }

            return result ?? throw new InvalidOperationException(
                "The realtime position persistence boundary completed without an outcome.");
        }
    }

    /// <returns><see langword="null"/>, если открытие нужно подтвердить REST-наблюдением.</returns>
    private async Task<RealtimeObservationApplyResult?> OpenLifecycleAsync(
        UserId userId,
        long generation,
        PreparedObservation prepared,
        LockedState locked,
        CancellationToken cancellationToken)
    {
        // Закрытие lifecycle выполняется только по полному снимку, поэтому сообщение не новее закрытия
        // уже опровергнуто им. Курсор при этом уже доказал, что ревизия новее закрытия: если биржевое
        // время факта тоже новее, а ограниченный ObservedAt — нет, порядок неоднозначен.
        var latestClosure = locked.Positions
            .Select(versioned => versioned.Value)
            .Where(position => position.TrackingState == PositionTrackingState.Closed && IsInSlot(position, prepared))
            .Select(position => position.ClosedAt)
            .Max();
        if (latestClosure is { } closedAt && prepared.ObservedAt <= closedAt)
        {
            if (IsExchangeFactAfter(prepared, closedAt))
            {
                return RealtimeObservationApplyResult.RecoveryRequired(
                    RealtimeObservationApplyResult.AmbiguousObservationOrder);
            }

            return RealtimeObservationApplyResult.Stale(RealtimeObservationApplyResult.OlderThanClosure);
        }

        // Watermark продвигается и неуспешными или частичными REST-попытками, поэтому не доказывает
        // отсутствие позиции: открытие не новее него подтверждается свежим scoped-снимком.
        if (locked.Account.LastAppliedPositionsObservationAt is { } positionsWatermark &&
            prepared.ObservedAt <= positionsWatermark)
        {
            return null;
        }

        var observed = prepared.Observation.Position!;
        var created = Position.Create(
            prepared.Key!.Value,
            MarketCategory.Linear,
            prepared.Observation.SettlementAsset!.Value,
            observed.Size,
            prepared.ObservedAt,
            prepared.ObservedAt,
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
        await positionRepository
            .SaveAsync(userId, created, expectedVersion: null, cancellationToken)
            .ConfigureAwait(false);

        var positions = locked.Positions
            .Select(versioned => versioned.Value)
            .Append(created)
            .ToArray();
        await PersistDerivedStateAsync(
                userId,
                generation,
                prepared,
                locked,
                positions,
                created.Changes.Select(change => (created, change)),
                cancellationToken)
            .ConfigureAwait(false);
        return RealtimeObservationApplyResult.Applied();
    }

    private async Task<RealtimeObservationApplyResult> UpdateLifecycleAsync(
        UserId userId,
        long generation,
        PreparedObservation prepared,
        LockedState locked,
        Position existing,
        CancellationToken cancellationToken)
    {
        // Сюда попадают только наблюдения с ревизией новее курсора. Если ограниченный ObservedAt
        // оказывается не новее принятого состояния, биржевой порядок и время записи противоречат друг
        // другу — прямое применение запрещено, нужна полная сверка.
        if (prepared.ObservedAt < existing.LastObservedAt)
        {
            return RealtimeObservationApplyResult.RecoveryRequired(
                RealtimeObservationApplyResult.AmbiguousObservationOrder);
        }

        // Пометки Unknown и Stale фиксируются временем REST-попытки или оценки свежести, которое может быть
        // позже последнего наблюдения. Более раннее сообщение не опровергает пометку и не может быть
        // записано в append-only историю перед ней; актуальное состояние восстанавливает полная сверка.
        if (prepared.ObservedAt < existing.Changes[^1].OccurredAt)
        {
            return RealtimeObservationApplyResult.RecoveryRequired(
                RealtimeObservationApplyResult.OlderThanPositionHistory);
        }

        if (existing.SettlementAsset != prepared.Observation.SettlementAsset)
        {
            return RealtimeObservationApplyResult.RecoveryRequired(RealtimeObservationApplyResult.SettlementMismatch);
        }

        var observed = prepared.Observation.Position!;
        var change = existing.ApplyObservation(
            observed.Size,
            prepared.ObservedAt,
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
        var version = locked.Positions.Single(versioned => versioned.Value.Id == existing.Id).Version;
        await positionRepository
            .SaveAsync(userId, existing, version, cancellationToken)
            .ConfigureAwait(false);

        await PersistDerivedStateAsync(
                userId,
                generation,
                prepared,
                locked,
                locked.Positions.Select(versioned => versioned.Value).ToArray(),
                change is null ? [] : [(existing, change)],
                cancellationToken)
            .ConfigureAwait(false);
        return RealtimeObservationApplyResult.Applied();
    }

    private async Task<RealtimeObservationApplyResult> ConfirmWithRestAsync(
        UserId userId,
        long generation,
        PreparedObservation prepared,
        CancellationToken cancellationToken)
    {
        var accountId = prepared.Observation.ExchangeAccountId;
        var loaded = await accountRepository
            .GetByIdAsync(userId, accountId, cancellationToken)
            .ConfigureAwait(false);
        if (loaded is null || loaded.Value.ConnectionStatus == ExchangeAccountConnectionStatus.Disabled)
        {
            return RealtimeObservationApplyResult.AccountUnavailable();
        }

        // Недоступные credentials включённого аккаунта — недоступный REST, а не недоступный аккаунт:
        // неподтверждённое событие остаётся потерянным, и его восстанавливает только полная сверка.
        ExchangeAccountCredential? credentials;
        try
        {
            credentials = await credentialStore
                .GetAsync(userId, accountId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ExchangeAccountCredentialsUnavailableException)
        {
            return RealtimeObservationApplyResult.RecoveryRequired(
                RealtimeObservationApplyResult.RestConfirmationUnavailable);
        }

        if (credentials is null)
        {
            return RealtimeObservationApplyResult.RecoveryRequired(
                RealtimeObservationApplyResult.RestConfirmationUnavailable);
        }

        OpenPositionsObservation snapshot;
        using (var providerLease = providerFactory.Create(loaded.Value.ExchangeId, credentials))
        {
            snapshot = await providerLease.Provider
                .GetOpenPositionsAsync(MarketCategory.Linear, prepared.Observation.Symbol, cancellationToken)
                .ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (snapshot.Status != OpenPositionsObservationStatus.Complete ||
            snapshot.Category != MarketCategory.Linear ||
            snapshot.ObservedAt == default ||
            !string.Equals(snapshot.Symbol?.Trim(), prepared.Observation.Symbol.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return RealtimeObservationApplyResult.RecoveryRequired(RealtimeObservationApplyResult.RestConfirmationIncomplete);
        }

        // ObservedAt REST-снимка — момент отправки запроса по часам backend, то есть нижняя граница
        // чтения состояния биржей; ReceivedAt — момент получения сообщения теми же часами. Только снимок,
        // запрошенный не раньше получения сообщения, гарантированно отражает подтверждаемое событие.
        // Время завершения REST-запроса этого не доказывает и заменой не служит.
        if (snapshot.ObservedAt < prepared.Observation.ReceivedAt)
        {
            return RealtimeObservationApplyResult.RecoveryRequired(RealtimeObservationApplyResult.RestConfirmationStale);
        }

        for (var attempt = 1; ; attempt++)
        {
            RealtimeObservationApplyResult? result = null;
            try
            {
                await persistenceTransaction
                    .ExecuteAsync(
                        async token =>
                        {
                            var (locked, rejection) = await LockAsync(userId, generation, prepared, token)
                                .ConfigureAwait(false);
                            if (locked is null)
                            {
                                result = rejection;
                                return;
                            }

                            // REST-наблюдение позиций аккаунта, применённое после запроса подтверждения, могло
                            // отразить более новое состояние; более старый снимок не должен его откатить.
                            // Неуспешная попытка тоже продвигает watermark, поэтому итог консервативен — recovery.
                            if (locked.Account.LastAppliedPositionsObservationAt is { } fullSyncAt &&
                                TimestampCanonicalizer.ToUtcMicroseconds(snapshot.ObservedAt) <= fullSyncAt)
                            {
                                result = RealtimeObservationApplyResult.RecoveryRequired(
                                    RealtimeObservationApplyResult.RestConfirmationStale);
                                return;
                            }

                            var trackedPositions = locked.Positions.Select(versioned => versioned.Value).ToArray();
                            var now = Max(clock.GetUtcNow(), snapshot.ObservedAt);
                            foreach (var position in trackedPositions)
                            {
                                // Наблюдение могло быть записано экземпляром с опережающими часами;
                                // оценка свежести раньше последнего наблюдения недопустима.
                                if (position.TrackingState != PositionTrackingState.Closed)
                                {
                                    now = Max(now, position.LastObservedAt);
                                }
                            }

                            var reconciliation = PositionReconciler.Reconcile(
                                accountId,
                                trackedPositions,
                                snapshot,
                                now,
                                PortfolioStaleAfter);
                            if (!reconciliation.IsFullyReconciled)
                            {
                                result = RealtimeObservationApplyResult.RecoveryRequired(
                                    RealtimeObservationApplyResult.RestConfirmationIncomplete);
                                return;
                            }

                            var versions = locked.Positions.ToDictionary(
                                versioned => versioned.Value.Id,
                                versioned => versioned.Version);
                            foreach (var position in reconciliation.PositionsToPersist)
                            {
                                await positionRepository
                                    .SaveAsync(userId, position, versions[position.Id], token)
                                    .ConfigureAwait(false);
                            }

                            foreach (var position in reconciliation.NewPositions)
                            {
                                await positionRepository
                                    .SaveAsync(userId, position, expectedVersion: null, token)
                                    .ConfigureAwait(false);
                            }

                            var positions = trackedPositions.Concat(reconciliation.NewPositions).ToArray();
                            var positionsById = positions.ToDictionary(position => position.Id);
                            await PersistDerivedStateAsync(
                                    userId,
                                    generation,
                                    prepared,
                                    locked,
                                    positions,
                                    reconciliation.Changes.Select(change => (positionsById[change.PositionId], change)),
                                    token)
                                .ConfigureAwait(false);
                            result = RealtimeObservationApplyResult.Applied(confirmedByRest: true);
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (IsRetryable(exception, attempt))
            {
                continue;
            }
            catch (Exception exception) when (exception is IPositionActiveKeyViolation)
            {
                throw ActiveKeyRaceLost(prepared, exception);
            }

            return result ?? throw new InvalidOperationException(
                "The realtime position confirmation boundary completed without an outcome.");
        }
    }

    /// <summary>
    /// Блокирует позиции и аккаунт, проверяет поколение и порядок наблюдения относительно курсора.
    /// </summary>
    /// <returns>Заблокированное состояние либо итог, при котором наблюдение не применяется.</returns>
    private async Task<(LockedState? Locked, RealtimeObservationApplyResult? Rejection)> LockAsync(
        UserId userId,
        long generation,
        PreparedObservation prepared,
        CancellationToken cancellationToken)
    {
        var accountId = prepared.Observation.ExchangeAccountId;
        var beforeLocks = await accountRepository
            .GetByIdAsync(userId, accountId, cancellationToken)
            .ConfigureAwait(false);
        if (beforeLocks is null || beforeLocks.Value.ConnectionStatus == ExchangeAccountConnectionStatus.Disabled)
        {
            return (null, RealtimeObservationApplyResult.AccountUnavailable());
        }

        var watermarkBeforeLocks = await positionRepository
            .GetVersionWatermarkByExchangeAccountAsync(userId, accountId, cancellationToken)
            .ConfigureAwait(false);
        await persistenceTransaction.LockPositionsAsync(userId, accountId, cancellationToken).ConfigureAwait(false);
        await persistenceTransaction.LockAccountAsync(userId, accountId, cancellationToken).ConfigureAwait(false);

        var account = await accountRepository
            .GetByIdAsync(userId, accountId, cancellationToken)
            .ConfigureAwait(false);
        if (account is null || account.Value.ConnectionStatus == ExchangeAccountConnectionStatus.Disabled)
        {
            return (null, RealtimeObservationApplyResult.AccountUnavailable());
        }

        if (!await observationStateStore
                .TryLockGenerationAsync(userId, accountId, generation, cancellationToken)
                .ConfigureAwait(false))
        {
            return (null, RealtimeObservationApplyResult.GenerationSuperseded());
        }

        var cursor = await cursorStore
            .GetAsync(userId, prepared.CursorKey, cancellationToken)
            .ConfigureAwait(false);
        var rejection = CompareRevision(cursor?.Value, prepared) switch
        {
            RevisionOrder.Duplicate => RealtimeObservationApplyResult.Duplicate(),
            RevisionOrder.Older => RealtimeObservationApplyResult.Stale(RealtimeObservationApplyResult.OlderRevision),
            RevisionOrder.Conflict => RealtimeObservationApplyResult.RecoveryRequired(
                RealtimeObservationApplyResult.RevisionConflict),
            _ => null,
        };
        if (rejection is not null)
        {
            return (null, rejection);
        }

        var positions = await positionRepository
            .GetByExchangeAccountAsync(userId, accountId, cancellationToken)
            .ConfigureAwait(false);
        if (!HasSamePositionWatermark(watermarkBeforeLocks, positions))
        {
            throw new ConcurrencyConflictException(
                "The position set changed while the realtime observation acquired its serialization locks.");
        }

        return (new LockedState(account.Value, cursor, positions), null);
    }

    private async Task PersistDerivedStateAsync(
        UserId userId,
        long generation,
        PreparedObservation prepared,
        LockedState locked,
        IReadOnlyCollection<Position> positions,
        IEnumerable<(Position Position, PositionChange Change)> changes,
        CancellationToken cancellationToken)
    {
        var accountId = prepared.Observation.ExchangeAccountId;
        var previous = await portfolioStateRepository
            .GetLatestAsync(userId, accountId, cancellationToken)
            .ConfigureAwait(false);
        var capital = previous?.Capital ?? new PortfolioCapitalState(null, null, null);
        var calculatedAt = clock.GetUtcNow();
        if (capital.ObservedAt is { } capitalObservedAt && capitalObservedAt > calculatedAt)
        {
            calculatedAt = capitalObservedAt;
        }

        foreach (var position in positions)
        {
            if (position.TrackingState != PositionTrackingState.Closed && position.LastObservedAt > calculatedAt)
            {
                calculatedAt = position.LastObservedAt;
            }
        }

        calculatedAt = PortfolioStateCalculatedAt.Resolve(calculatedAt, previous?.CalculatedAt);
        var portfolioState = PortfolioStateAssembler.AssembleWithCapital(
            capital,
            positions,
            accountId,
            calculatedAt,
            PortfolioStaleAfter,
            previous?.PositionsFullyReconciled ?? false);
        await portfolioStateRepository
            .SaveAsync(userId, portfolioState, cancellationToken)
            .ConfigureAwait(false);

        await SaveCursorAsync(userId, generation, prepared, locked.Cursor, cancellationToken).ConfigureAwait(false);

        var applicationEvents = changes
            .Select(pair => PositionApplicationEventFactory.Create(userId, locked.Account, pair.Position, pair.Change))
            .ToList();
        applicationEvents.Add(
            new PortfolioUpdatedEventV1(
                Guid.NewGuid(),
                clock.GetUtcNow(),
                userId.Value,
                accountId.Value));
        await applicationEventOutbox
            .AddRangeAsync(applicationEvents, cancellationToken)
            .ConfigureAwait(false);
    }

    private Task<ConcurrencyVersion> SaveCursorAsync(
        UserId userId,
        long generation,
        PreparedObservation prepared,
        Versioned<PositionObservationCursor>? current,
        CancellationToken cancellationToken)
    {
        // Курсор не допускает регресса известных компонентов ревизии, поэтому неизвестный в
        // наблюдении компонент переносится из текущего курсора.
        var cursor = new PositionObservationCursor
        {
            Key = prepared.CursorKey,
            LastProviderSequence = MaxNullable(current?.Value.LastProviderSequence, prepared.Sequence),
            LastProviderUpdatedAt = MaxNullable(current?.Value.LastProviderUpdatedAt, prepared.ProviderUpdatedAt),
            LastMessageCreatedAt = prepared.Observation.MessageCreatedAt ?? current?.Value.LastMessageCreatedAt,
            LastAcceptedFingerprint = prepared.Fingerprint,
        };
        return cursorStore.SaveAsync(userId, cursor, current?.Version, generation, cancellationToken);
    }

    /// <summary>
    /// Сравнивает ревизию наблюдения с курсором. Компоненты <c>seq</c> и время изменения позиции
    /// сравниваются только между собой; противоречие компонентов или совпадение ревизии с другим
    /// содержанием означает неопределённый порядок.
    /// </summary>
    private static RevisionOrder CompareRevision(PositionObservationCursor? cursor, PreparedObservation prepared)
    {
        if (cursor is null)
        {
            return RevisionOrder.Newer;
        }

        int? bySequence = prepared.Sequence is { } sequence && cursor.LastProviderSequence is { } cursorSequence
            ? sequence.CompareTo(cursorSequence)
            : null;
        int? byUpdatedAt = prepared.ProviderUpdatedAt is { } updatedAt && cursor.LastProviderUpdatedAt is { } cursorUpdatedAt
            ? updatedAt.CompareTo(cursorUpdatedAt)
            : null;

        if (bySequence is null && byUpdatedAt is null)
        {
            return RevisionOrder.Conflict;
        }

        var newer = bySequence > 0 || byUpdatedAt > 0;
        var older = bySequence < 0 || byUpdatedAt < 0;
        if (newer && older)
        {
            return RevisionOrder.Conflict;
        }

        if (newer)
        {
            return RevisionOrder.Newer;
        }

        if (older)
        {
            return RevisionOrder.Older;
        }

        return string.Equals(cursor.LastAcceptedFingerprint, prepared.Fingerprint, StringComparison.Ordinal)
            ? RevisionOrder.Duplicate
            : RevisionOrder.Conflict;
    }

    /// <summary>
    /// Отпечаток бизнес-содержания наблюдения. Рыночные показатели (mark price, PnL, стоимость
    /// позиции) не входят: их изменение без изменения ревизии не является новым фактом позиции.
    /// Отпечаток только различает содержимое одной ревизии и не защищает от подделки, поэтому
    /// используется некриптографический FNV-1a (128 бит).
    /// </summary>
    private static string ComputeFingerprint(PrivatePositionObservation observation)
    {
        var position = observation.Position;
        var builder = new StringBuilder()
            .Append(observation.Kind).Append('|')
            .Append(observation.Side).Append('|')
            .Append(Format(position?.Size ?? observation.Size)).Append('|')
            .Append(observation.SettlementAsset?.ToString()).Append('|')
            .Append(Format(position?.AvgPrice)).Append('|')
            .Append(Format(position?.Leverage)).Append('|')
            .Append(Format(position?.BreakEvenPrice)).Append('|')
            .Append(Format(position?.LiquidationPrice)).Append('|')
            .Append(Format(position?.TakeProfit)).Append('|')
            .Append(Format(position?.StopLoss)).Append('|')
            .Append(Format(position?.TrailingStop));
        var hash = new UInt128(0x6c62272e07bb0142, 0x62b821756295c58d);
        var prime = new UInt128(0x0000000001000000, 0x000000000000013b);
        foreach (var value in Encoding.UTF8.GetBytes(builder.ToString()))
        {
            hash = (hash ^ value) * prime;
        }

        return $"{FingerprintVersion}:{hash.ToString("x32", CultureInfo.InvariantCulture)}";

        static string Format(decimal? value) =>
            value?.ToString("G29", CultureInfo.InvariantCulture) ?? "-";
    }

    private static bool IsInSlot(Position position, PreparedObservation prepared) =>
        position.MarketCategory == MarketCategory.Linear &&
        position.ExchangePositionKey.PositionIdx == prepared.CursorKey.PositionIdx &&
        string.Equals(
            position.ExchangePositionKey.InstrumentId.Value,
            prepared.CursorKey.Symbol.Value,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Биржевое время факта (не ограниченное <see cref="PrivatePositionObservation.ReceivedAt"/>)
    /// строго позже указанной отметки.
    /// </summary>
    private static bool IsExchangeFactAfter(PreparedObservation prepared, DateTimeOffset threshold)
    {
        var exchangeTime = prepared.Observation.MessageCreatedAt ?? prepared.ProviderUpdatedAt;
        return exchangeTime is { } value && value > threshold;
    }

    /// <summary>
    /// Повтор с перечитыванием допустим после проигранного CAS или гонки вставки активной позиции
    /// с конкурентной синхронизацией, пока не исчерпан лимит попыток.
    /// </summary>
    private static bool IsRetryable(Exception exception, int attempt) =>
        attempt < MaxPersistenceAttempts &&
        exception is ConcurrencyConflictException or IPositionActiveKeyViolation;

    private static ConcurrencyConflictException ActiveKeyRaceLost(
        PreparedObservation prepared,
        Exception exception) =>
        new(
            $"Realtime observation for {prepared.CursorKey} lost an active position insert race.",
            exception);

    private static bool HasSamePositionWatermark(
        IReadOnlyCollection<PositionVersionWatermark> expected,
        IReadOnlyCollection<Versioned<Position>> actual)
    {
        if (expected.Count != actual.Count)
        {
            return false;
        }

        var expectedById = expected.ToDictionary(watermark => watermark.PositionId, watermark => watermark.Version);
        return actual.All(versioned =>
            expectedById.TryGetValue(versioned.Value.Id, out var version) && version == versioned.Version);
    }

    private static T? MaxNullable<T>(T? first, T? second)
        where T : struct, IComparable<T>
    {
        if (first is not { } left)
        {
            return second;
        }

        if (second is not { } right)
        {
            return left;
        }

        return left.CompareTo(right) >= 0 ? left : right;
    }

    private static DateTimeOffset Max(DateTimeOffset first, DateTimeOffset second) =>
        first >= second ? first : second;

    private enum RevisionOrder
    {
        Newer,
        Duplicate,
        Older,
        Conflict,
    }

    private sealed record PreparedObservation(
        PrivatePositionObservation Observation,
        PositionObservationCursorKey CursorKey,
        ExchangePositionKey? Key,
        DateTimeOffset ObservedAt,
        long? Sequence,
        DateTimeOffset? ProviderUpdatedAt,
        string Fingerprint);

    private sealed record LockedState(
        ExchangeAccount Account,
        Versioned<PositionObservationCursor>? Cursor,
        IReadOnlyCollection<Versioned<Position>> Positions);
}
