using Intelligence.TradeSystem.Application.Accounts.Credentials;
using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Events;
using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Portfolio;

namespace Intelligence.TradeSystem.Application.Accounts;

/// <summary>
/// Координирует одну синхронизацию биржевой учётной записи с явно заданной пользовательской областью в режиме только для чтения.
/// </summary>
public sealed class ExchangeAccountSyncService(
    IExchangeAccountRepository accountRepository,
    IExchangeAccountCredentialStore credentialStore,
    IPrivateAccountProviderFactory providerFactory,
    IPositionRepository positionRepository,
    IPortfolioStateRepository portfolioStateRepository,
    IExchangeAccountSyncTransaction persistenceTransaction,
    IApplicationEventOutbox applicationEventOutbox,
    TimeProvider? timeProvider = null)
    : IExchangeAccountSyncService
{
    private const int MaxPersistenceAttempts = 3;
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private static readonly TimeSpan PortfolioStaleAfter = TimeSpan.FromMinutes(5);

    public async Task<ExchangeAccountSyncResult> SynchronizeAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        CancellationToken cancellationToken = default)
    {
        if (userId == default)
        {
            throw new ArgumentException("UserId must be initialized.", nameof(userId));
        }

        if (exchangeAccountId == default)
        {
            throw new ArgumentException(
                "ExchangeAccountId must be initialized.",
                nameof(exchangeAccountId));
        }

        var loadedAccount = await accountRepository
            .GetByIdAsync(userId, exchangeAccountId, cancellationToken)
            .ConfigureAwait(false);

        if (loadedAccount is null)
        {
            return ExchangeAccountSyncResult.NotFound();
        }

        var account = loadedAccount.Value;
        if (account.ConnectionStatus == ExchangeAccountConnectionStatus.Disabled)
        {
            return ExchangeAccountSyncResult.AccountDisabled();
        }

        ExchangeAccountCredential? credentials;
        try
        {
            credentials = await credentialStore
                .GetAsync(userId, exchangeAccountId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ExchangeAccountCredentialsUnavailableException)
        {
            return ExchangeAccountSyncResult.CredentialsUnavailable();
        }
        if (credentials is null)
        {
            return ExchangeAccountSyncResult.CredentialsUnavailable();
        }

        using var providerLease = providerFactory.Create(account.ExchangeId, credentials);
        var balanceObservation = await providerLease.Provider
            .GetWalletBalanceAsync(AccountType.Unified, cancellationToken)
            .ConfigureAwait(false);

        var positionsObservation = await providerLease.Provider
            .GetOpenPositionsAsync(
                MarketCategory.Linear,
                symbol: null,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var hasFreshBalance = IsCompleteBalance(balanceObservation);
        var normalizedPositionsObservation = NormalizePositionsObservation(positionsObservation);
        var balanceObservationAt = NormalizeObservationTimestamp(balanceObservation.ObservedAt);
        var positionsObservationAt = normalizedPositionsObservation.ObservedAt;
        var observationCompletedAt = Max(
            clock.GetUtcNow(),
            balanceObservation.ObservedAt,
            positionsObservationAt);

        for (var attempt = 1; attempt <= MaxPersistenceAttempts; attempt++)
        {
            ExchangeAccountSyncResult? attemptResult = null;
            ExchangeAccount? persistedAccount = null;
            PortfolioState? persistedPortfolioState = null;
            string? persistedFailureReason = null;
            var persistedFullSync = false;
            var attemptedNewPositionKeys = Array.Empty<ExchangePositionKey>();
            var balanceDisposition = ExchangeAccountObservationDisposition.Superseded;
            var positionsDisposition = ExchangeAccountObservationDisposition.Superseded;

            try
            {
                await persistenceTransaction
                    .ExecuteAsync(
                        async persistenceCancellationToken =>
                        {
                            var latest = await accountRepository
                                .GetByIdAsync(
                                    userId,
                                    exchangeAccountId,
                                    persistenceCancellationToken)
                                .ConfigureAwait(false);

                            if (latest is null)
                            {
                                attemptResult = ExchangeAccountSyncResult.NotFound();
                                return;
                            }

                            var currentAccount = latest.Value;
                            if (currentAccount.ConnectionStatus ==
                                ExchangeAccountConnectionStatus.Disabled)
                            {
                                attemptResult = ExchangeAccountSyncResult.AccountDisabled();
                                return;
                            }

                            var watermarkBeforeLocks = await positionRepository
                                .GetVersionWatermarkByExchangeAccountAsync(
                                    userId,
                                    exchangeAccountId,
                                    persistenceCancellationToken)
                                .ConfigureAwait(false);
                            await persistenceTransaction
                                .LockPositionsAsync(
                                    userId,
                                    exchangeAccountId,
                                    persistenceCancellationToken)
                                .ConfigureAwait(false);
                            await persistenceTransaction
                                .LockAccountAsync(
                                    userId,
                                    exchangeAccountId,
                                    persistenceCancellationToken)
                                .ConfigureAwait(false);

                            var accountForPersistence = CopyAccount(currentAccount);

                            var tracked = await positionRepository
                                .GetByExchangeAccountAsync(
                                    userId,
                                    exchangeAccountId,
                                    persistenceCancellationToken)
                                .ConfigureAwait(false);
                            if (!HasSamePositionWatermark(watermarkBeforeLocks, tracked))
                            {
                                throw new ConcurrencyConflictException(
                                    "The position set changed while the synchronization acquired its serialization locks.");
                            }

                            var trackedPositions = tracked
                                .Select(versioned => versioned.Value)
                                .ToArray();

                            // Читается под блокировкой аккаунта при каждой попытке: новая сводка должна
                            // стать последней и после сводки потока, записанной экземпляром с опережающими
                            // часами. Повтор после CAS перечитывает капитал до решения о REST-балансе.
                            var previousPortfolioState = await portfolioStateRepository
                                .GetLatestAsync(
                                    userId,
                                    exchangeAccountId,
                                    persistenceCancellationToken)
                                .ConfigureAwait(false);

                            persistenceCancellationToken.ThrowIfCancellationRequested();

                            positionsDisposition = accountForPersistence.AdvanceObservationWatermark(
                                ExchangeAccountObservationResource.Positions,
                                positionsObservationAt);

                            // ObservedAt ответа не доказывает новизну относительно капитала, сохранённого
                            // во время REST-чтения другим экземпляром. Проверка — только когда watermark
                            // иначе принял бы наблюдение; устаревший REST остаётся обычным Superseded.
                            var restBalanceOrderAmbiguous = false;
                            var tentativeBalanceDisposition = PeekObservationWatermarkDisposition(
                                accountForPersistence.LastAppliedBalanceObservationAt,
                                balanceObservationAt);
                            if (IsApplied(tentativeBalanceDisposition) &&
                                IsRestBalanceOrderAmbiguous(
                                    balanceObservation,
                                    previousPortfolioState?.Capital))
                            {
                                restBalanceOrderAmbiguous = true;
                                // Непринятое REST-наблюдение не продвигает watermark баланса.
                                balanceDisposition = ExchangeAccountObservationDisposition.Superseded;
                            }
                            else
                            {
                                balanceDisposition = accountForPersistence.AdvanceObservationWatermark(
                                    ExchangeAccountObservationResource.Balance,
                                    balanceObservationAt);
                            }

                            // Неоднозначный REST-баланс нельзя отбрасывать как обычный no-op:
                            // признак balance_ambiguous должен сохраниться, даже если позиции
                            // тоже не приняты (Superseded / AlreadyApplied).
                            if (!IsApplied(balanceDisposition) &&
                                !IsApplied(positionsDisposition) &&
                                !restBalanceOrderAmbiguous)
                            {
                                attemptResult = CreateNoOpResult(
                                    currentAccount,
                                    balanceDisposition,
                                    positionsDisposition);
                                return;
                            }

                            // Свежесть оценивается не раньше последнего подтверждённого наблюдения позиций:
                            // факт потока мог быть записан при опережающих часах. Время REST-наблюдения при
                            // этом не меняется и определяет только применимость снимка.
                            var freshnessEvaluatedAt = trackedPositions
                                .Where(position => position.TrackingState != PositionTrackingState.Closed)
                                .Select(position => position.LastObservedAt)
                                .Append(observationCompletedAt)
                                .Max();
                            var positionsAreCurrent =
                                IsApplied(positionsDisposition);
                            var acceptRestBalance = IsApplied(balanceDisposition) && hasFreshBalance;
                            var reconciliation = PositionReconciler.Reconcile(
                                exchangeAccountId,
                                trackedPositions,
                                normalizedPositionsObservation,
                                freshnessEvaluatedAt,
                                PortfolioStaleAfter,
                                applyObservation: positionsAreCurrent);
                            attemptedNewPositionKeys = reconciliation.NewPositions
                                .Select(position => position.ExchangePositionKey)
                                .ToArray();

                            var positionsFullyReconciled = positionsAreCurrent
                                ? reconciliation.IsFullyReconciled &&
                                  CoversAllActivePositions(
                                      trackedPositions.Concat(reconciliation.NewPositions),
                                      normalizedPositionsObservation)
                                : previousPortfolioState?.PositionsFullyReconciled ?? false;

                            var trackedById = tracked.ToDictionary(versioned => versioned.Value.Id);
                            foreach (var position in reconciliation.PositionsToPersist)
                            {
                                var versioned = trackedById[position.Id];
                                await positionRepository
                                    .SaveAsync(
                                        userId,
                                        versioned.Value,
                                        versioned.Version,
                                        persistenceCancellationToken)
                                    .ConfigureAwait(false);
                            }

                            foreach (var newPosition in reconciliation.NewPositions)
                            {
                                await positionRepository
                                    .SaveAsync(
                                        userId,
                                        newPosition,
                                        expectedVersion: null,
                                        persistenceCancellationToken)
                                    .ConfigureAwait(false);
                            }

                            var reconciledPositions = trackedPositions
                                .Concat(reconciliation.NewPositions)
                                .ToArray();

                            var capital = acceptRestBalance
                                ? PortfolioStateAssembler.CreateCapital(
                                    balanceObservation.Balance!,
                                    balanceObservation.ObservedAt)
                                : previousPortfolioState?.Capital
                                    ?? new PortfolioCapitalState(null, null, null);
                            var calculatedAt = PortfolioStateCalculatedAt.Resolve(
                                capital.ObservedAt is { } capitalObservedAt && capitalObservedAt > freshnessEvaluatedAt
                                    ? capitalObservedAt
                                    : freshnessEvaluatedAt,
                                previousPortfolioState?.CalculatedAt);
                            var portfolioState = PortfolioStateAssembler.AssembleWithCapital(
                                capital,
                                reconciledPositions,
                                exchangeAccountId,
                                calculatedAt,
                                PortfolioStaleAfter,
                                positionsFullyReconciled);
                            await portfolioStateRepository
                                .SaveAsync(
                                    userId,
                                    portfolioState,
                                    persistenceCancellationToken)
                                .ConfigureAwait(false);

                            persistedFailureReason = restBalanceOrderAmbiguous
                                ? "balance_ambiguous"
                                : GetFailureReason(
                                    IsApplied(balanceDisposition),
                                    hasFreshBalance,
                                    positionsAreCurrent,
                                    normalizedPositionsObservation,
                                    reconciliation,
                                    positionsFullyReconciled);
                            persistedFullSync =
                                acceptRestBalance &&
                                positionsAreCurrent &&
                                positionsFullyReconciled;

                            if (persistedFullSync)
                            {
                                accountForPersistence.RecordSuccessfulSync(clock.GetUtcNow());
                            }
                            else if (persistedFailureReason is not null)
                            {
                                accountForPersistence.RecordSyncFailure(persistedFailureReason);
                            }

                            persistedAccount = accountForPersistence;
                            persistedPortfolioState = portfolioState;
                            attemptResult = persistedFailureReason is null
                                ? ExchangeAccountSyncResult.Synchronized(
                                    accountForPersistence,
                                    portfolioState)
                                : ExchangeAccountSyncResult.ExchangeUnavailable(
                                    accountForPersistence);

                            await accountRepository
                                .SaveAsync(
                                    userId,
                                    accountForPersistence,
                                    latest.Version,
                                    persistenceCancellationToken)
                                .ConfigureAwait(false);

                            var positionsById = trackedById
                                .ToDictionary(pair => pair.Key, pair => pair.Value.Value);
                            foreach (var newPosition in reconciliation.NewPositions)
                            {
                                positionsById.Add(newPosition.Id, newPosition);
                            }

                            var applicationEvents = reconciliation.Changes
                                .Select(change =>
                                {
                                    if (!positionsById.TryGetValue(change.PositionId, out var position))
                                    {
                                        throw new InvalidOperationException(
                                            $"Position change {change.PositionId} has no position in the reconciliation result.");
                                    }

                                    return PositionApplicationEventFactory.Create(
                                        userId,
                                        accountForPersistence,
                                        position,
                                        change);
                                })
                                .ToList<IApplicationEvent>();
                            if (persistedFailureReason is not null)
                            {
                                var degradedAt = clock.GetUtcNow();
                                applicationEvents.Add(
                                    PositionApplicationEventFactory.CreateSyncDegraded(
                                        userId,
                                        accountForPersistence,
                                        persistedFailureReason,
                                        degradedAt));
                                applicationEvents.Add(
                                    new PortfolioUpdatedEventV1(
                                        Guid.NewGuid(),
                                        degradedAt,
                                        userId.Value,
                                        exchangeAccountId.Value));
                            }
                            else
                            {
                                var synchronizedAt = clock.GetUtcNow();
                                applicationEvents.Add(
                                    new ExchangeAccountUpdatedEventV1(
                                        Guid.NewGuid(),
                                        synchronizedAt,
                                        userId.Value,
                                        exchangeAccountId.Value));
                                applicationEvents.Add(
                                    new PortfolioUpdatedEventV1(
                                        Guid.NewGuid(),
                                        synchronizedAt,
                                        userId.Value,
                                        exchangeAccountId.Value));
                            }

                            await applicationEventOutbox
                                .AddRangeAsync(applicationEvents, persistenceCancellationToken)
                                .ConfigureAwait(false);
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
                when (exception is IPositionActiveKeyViolation)
            {
                var isSynchronizationRace =
                    attemptedNewPositionKeys.Length > 0 &&
                    await HasActivePositionForKeysAsync(
                        userId,
                        exchangeAccountId,
                        attemptedNewPositionKeys,
                        cancellationToken)
                        .ConfigureAwait(false);
                if (!isSynchronizationRace)
                {
                    throw;
                }

                if (attempt == MaxPersistenceAttempts)
                {
                    throw new ConcurrencyConflictException(
                        $"Synchronization for exchange account {exchangeAccountId} lost an active position insert race.",
                        exception);
                }

                cancellationToken.ThrowIfCancellationRequested();
                continue;
            }
            catch (ConcurrencyConflictException)
            {
                if (attempt == MaxPersistenceAttempts)
                {
                    throw;
                }

                cancellationToken.ThrowIfCancellationRequested();
                continue;
            }

            if (attemptResult is null)
            {
                throw new InvalidOperationException(
                    "The synchronization persistence boundary completed without an outcome.");
            }

            if (attemptResult.Outcome == ExchangeAccountSyncOutcome.Synchronized)
            {
                AdvanceAcceptedWatermarks(
                    account,
                    balanceDisposition,
                    balanceObservationAt,
                    positionsDisposition,
                    positionsObservationAt);
                var synchronizedPortfolioState = persistedPortfolioState
                    ?? throw new InvalidOperationException(
                        "A successful synchronization did not persist a portfolio state.");
                if (persistedFullSync)
                {
                    var synchronizedAt = persistedAccount?.LastSyncedAt
                        ?? throw new InvalidOperationException(
                            "A successful synchronization did not persist account metadata.");
                    account.RecordSuccessfulSync(synchronizedAt);
                    return AttachAcceptedResourceEvidence(
                        ExchangeAccountSyncResult.FullyReconciled(account, synchronizedPortfolioState),
                        hasFreshBalance,
                        balanceDisposition,
                        balanceObservation,
                        positionsDisposition,
                        normalizedPositionsObservation);
                }

                return AttachAcceptedResourceEvidence(
                    ExchangeAccountSyncResult.Synchronized(account, synchronizedPortfolioState),
                    hasFreshBalance,
                    balanceDisposition,
                    balanceObservation,
                    positionsDisposition,
                    normalizedPositionsObservation);
            }

            if (attemptResult.Outcome == ExchangeAccountSyncOutcome.ExchangeUnavailable)
            {
                AdvanceAcceptedWatermarks(
                    account,
                    balanceDisposition,
                    balanceObservationAt,
                    positionsDisposition,
                    positionsObservationAt);
                account.RecordSyncFailure(
                    persistedFailureReason
                    ?? throw new InvalidOperationException(
                        "The synchronization persistence boundary completed without a failure reason."));
                // Деградированная сверка могла принять баланс и/или позиции: окна порядка нужны
                // независимо от общего Outcome, иначе coordinator теряет доказательство.
                return AttachAcceptedResourceEvidence(
                    attemptResult,
                    hasFreshBalance,
                    balanceDisposition,
                    balanceObservation,
                    positionsDisposition,
                    normalizedPositionsObservation);
            }

            return attemptResult;
        }

        throw new ConcurrencyConflictException(
            $"Synchronization for exchange account {exchangeAccountId} exceeded the persistence retry limit.");
    }

    private async Task<bool> HasActivePositionForKeysAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        IReadOnlyCollection<ExchangePositionKey> keys,
        CancellationToken cancellationToken)
    {
        var current = await positionRepository
            .GetByExchangeAccountAsync(userId, exchangeAccountId, cancellationToken)
            .ConfigureAwait(false);
        var activeKeys = current
            .Select(versioned => versioned.Value)
            .Where(position => position.TrackingState != PositionTrackingState.Closed)
            .Select(position => position.ExchangePositionKey)
            .ToHashSet();
        return keys.Any(activeKeys.Contains);
    }

    private static ExchangeAccountSyncResult CreateNoOpResult(
        ExchangeAccount account,
        ExchangeAccountObservationDisposition balanceDisposition,
        ExchangeAccountObservationDisposition positionsDisposition)
    {
        if (balanceDisposition == ExchangeAccountObservationDisposition.AlreadyApplied &&
            positionsDisposition == ExchangeAccountObservationDisposition.AlreadyApplied)
        {
            return ExchangeAccountSyncResult.AlreadyApplied(account);
        }

        return ExchangeAccountSyncResult.Superseded(account);
    }

    private static bool IsApplied(ExchangeAccountObservationDisposition disposition) =>
        disposition == ExchangeAccountObservationDisposition.Applied;

    /// <summary>
    /// Предсказывает результат <see cref="ExchangeAccount.AdvanceObservationWatermark"/> без мутации.
    /// </summary>
    private static ExchangeAccountObservationDisposition PeekObservationWatermarkDisposition(
        DateTimeOffset? currentWatermark,
        DateTimeOffset observationAt)
    {
        if (currentWatermark is { } currentValue)
        {
            if (observationAt < currentValue)
                return ExchangeAccountObservationDisposition.Superseded;

            if (observationAt == currentValue)
                return ExchangeAccountObservationDisposition.AlreadyApplied;
        }

        return ExchangeAccountObservationDisposition.Applied;
    }

    private static bool HasSamePositionWatermark(
        IReadOnlyCollection<PositionVersionWatermark> expected,
        IReadOnlyCollection<Versioned<Position>> actual)
    {
        if (expected.Count != actual.Count)
            return false;

        var expectedOrdered = expected
            .OrderBy(watermark => watermark.PositionId.Value)
            .ToArray();
        var actualOrdered = actual
            .OrderBy(versioned => versioned.Value.Id.Value)
            .ToArray();

        return expectedOrdered
            .Zip(actualOrdered)
            .All(pair =>
                pair.First.PositionId == pair.Second.Value.Id &&
                pair.First.Version == pair.Second.Version);
    }

    private static void AdvanceAcceptedWatermarks(
        ExchangeAccount account,
        ExchangeAccountObservationDisposition balanceDisposition,
        DateTimeOffset balanceObservationAt,
        ExchangeAccountObservationDisposition positionsDisposition,
        DateTimeOffset positionsObservationAt)
    {
        if (IsApplied(balanceDisposition))
        {
            account.AdvanceObservationWatermark(
                ExchangeAccountObservationResource.Balance,
                balanceObservationAt);
        }

        if (IsApplied(positionsDisposition))
        {
            account.AdvanceObservationWatermark(
                ExchangeAccountObservationResource.Positions,
                positionsObservationAt);
        }
    }

    private static bool IsCompleteBalance(AccountBalanceObservation observation) =>
        observation.Status == AccountBalanceObservationStatus.Complete &&
        observation.Balance is { AccountType: AccountType.Unified } &&
        observation.ObservedAt != default;

    /// <summary>
    /// Порядок REST-баланса относительно persisted капитала недоказан, если последнее достоверное
    /// наблюдение капитала попадает в интервал чтения <c>[RequestStartedAt, ObservedAt]</c>
    /// (границы включительно) или интервал сам по себе некорректен.
    /// </summary>
    /// <remarks>
    /// Отсутствие ранее подтверждённого капитала не считается неоднозначностью: первичная
    /// REST-синхронизация остаётся допустимой. При <see cref="AccountBalanceObservation.RequestStartedAt"/>
    /// = <see langword="null"/> интервал вырождается в точку <see cref="AccountBalanceObservation.ObservedAt"/>.
    /// </remarks>
    private static bool IsRestBalanceOrderAmbiguous(
        AccountBalanceObservation balanceObservation,
        PortfolioCapitalState? previousCapital)
    {
        if (!IsCompleteBalance(balanceObservation))
            return false;

        if (previousCapital?.ObservedAt is not { } previousObservedAt)
            return false;

        var completedAt = balanceObservation.ObservedAt;
        if (balanceObservation.RequestStartedAt is { } startedAt)
        {
            if (startedAt > completedAt)
                return true;

            return previousObservedAt >= startedAt && previousObservedAt <= completedAt;
        }

        return previousObservedAt == completedAt;
    }

    /// <summary>
    /// Интервал успешного чтения баланса, принятого в капитал. Неуспешное или устаревшее чтение
    /// окно не образует и не запрещает последующие wallet-наблюдения.
    /// </summary>
    private static (DateTimeOffset StartedAt, DateTimeOffset CompletedAt)? ResolveAcceptedBalanceReadWindow(
        bool hasFreshBalance,
        ExchangeAccountObservationDisposition balanceDisposition,
        AccountBalanceObservation balanceObservation)
    {
        if (!hasFreshBalance || !IsApplied(balanceDisposition))
        {
            return null;
        }

        var completedAt = balanceObservation.ObservedAt;
        var startedAt = balanceObservation.RequestStartedAt ?? completedAt;
        return (startedAt, completedAt);
    }

    /// <summary>
    /// Передаёт coordinator сведения о фактически принятых REST-ресурсах без подмены общего Outcome.
    /// </summary>
    private static ExchangeAccountSyncResult AttachAcceptedResourceEvidence(
        ExchangeAccountSyncResult result,
        bool hasFreshBalance,
        ExchangeAccountObservationDisposition balanceDisposition,
        AccountBalanceObservation balanceObservation,
        ExchangeAccountObservationDisposition positionsDisposition,
        OpenPositionsObservation positionsObservation)
    {
        var balanceRead = ResolveAcceptedBalanceReadWindow(
            hasFreshBalance,
            balanceDisposition,
            balanceObservation);
        return result
            .WithAcceptedBalanceRead(balanceRead?.StartedAt, balanceRead?.CompletedAt)
            .WithAcceptedPositionsObservation(
                IsApplied(positionsDisposition) &&
                IsAcceptedPositionsStatus(positionsObservation.Status));
    }

    /// <summary>
    /// Failed не даёт доказательств порядка: watermark мог продвинуться, но окно позиций не создаётся.
    /// </summary>
    private static bool IsAcceptedPositionsStatus(OpenPositionsObservationStatus status) =>
        status is OpenPositionsObservationStatus.Complete or OpenPositionsObservationStatus.Partial;

    private OpenPositionsObservation NormalizePositionsObservation(
        OpenPositionsObservation observation)
    {
        if (Enum.IsDefined(observation.Status) &&
            observation.Category == MarketCategory.Linear &&
            observation.Symbol is null &&
            observation.ObservedAt != default)
        {
            return observation;
        }

        return OpenPositionsObservation.Failed(
            MarketCategory.Linear,
            symbol: null,
            observedAt: NormalizeObservationTimestamp(observation.ObservedAt),
            error: "invalid_positions_observation");
    }

    private DateTimeOffset NormalizeObservationTimestamp(DateTimeOffset observedAt) =>
        observedAt == default ? clock.GetUtcNow() : observedAt;

    private static string? GetFailureReason(
        bool balanceIsCurrent,
        bool hasFreshBalance,
        bool positionsAreCurrent,
        OpenPositionsObservation positionsObservation,
        PositionReconciliationResult reconciliation,
        bool positionsFullyReconciled)
    {
        if (balanceIsCurrent && !hasFreshBalance)
        {
            return "balance_failed";
        }

        if (!positionsAreCurrent)
        {
            return null;
        }

        return positionsObservation.Status switch
        {
            OpenPositionsObservationStatus.Failed => "positions_failed",
            OpenPositionsObservationStatus.Partial => "positions_partial",
            OpenPositionsObservationStatus.Complete when
                !reconciliation.IsFullyReconciled || !positionsFullyReconciled =>
                "positions_ambiguous",
            _ => null,
        };
    }

    private static bool CoversAllActivePositions(
        IEnumerable<Position> positions,
        OpenPositionsObservation observation) =>
        positions
            .Where(position => position.TrackingState != PositionTrackingState.Closed)
            .All(position =>
                position.MarketCategory == observation.Category &&
                (observation.Symbol is null ||
                 string.Equals(
                     position.ExchangePositionKey.InstrumentId.Value,
                     observation.Symbol.Trim(),
                     StringComparison.OrdinalIgnoreCase)));

    private static ExchangeAccount CopyAccount(ExchangeAccount account) =>
        ExchangeAccount.Create(
            account.Id,
            account.UserId,
            account.ExchangeId,
            account.ProviderIdentity,
            account.DisplayName,
            account.ConnectionStatus,
            account.Capabilities,
            account.LastSyncedAt,
            account.LastError,
            account.LastAppliedBalanceObservationAt,
            account.LastAppliedPositionsObservationAt);

    private static DateTimeOffset Max(
        DateTimeOffset first,
        DateTimeOffset second,
        DateTimeOffset third) =>
        first >= second
            ? (first >= third ? first : third)
            : (second >= third ? second : third);
}
