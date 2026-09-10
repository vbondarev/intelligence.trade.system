using Intelligence.TradeSystem.Application.Accounts.Credentials;
using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Events;
using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Portfolio;

namespace Intelligence.TradeSystem.Application.Accounts;

/// <summary>
/// Координирует одну явно ограниченную синхронизацию биржевой учётной записи в режиме только для чтения.
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

        var credentials = await credentialStore
            .GetAsync(userId, exchangeAccountId, cancellationToken)
            .ConfigureAwait(false);
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
        var calculatedAt = Max(
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

                            var accountForPersistence = CopyAccount(currentAccount);
                            balanceDisposition = accountForPersistence.AdvanceObservationWatermark(
                                ExchangeAccountObservationResource.Balance,
                                balanceObservationAt);
                            positionsDisposition = accountForPersistence.AdvanceObservationWatermark(
                                ExchangeAccountObservationResource.Positions,
                                positionsObservationAt);

                            if (!IsApplied(balanceDisposition) && !IsApplied(positionsDisposition))
                            {
                                attemptResult = CreateNoOpResult(
                                    currentAccount,
                                    balanceDisposition,
                                    positionsDisposition);
                                return;
                            }

                            var tracked = await positionRepository
                                .GetByExchangeAccountAsync(
                                    userId,
                                    exchangeAccountId,
                                    persistenceCancellationToken)
                                .ConfigureAwait(false);
                            var trackedPositions = tracked
                                .Select(versioned => versioned.Value)
                                .ToArray();

                            PortfolioState? previousPortfolioState = null;
                            if (!IsApplied(balanceDisposition) ||
                                !IsApplied(positionsDisposition) ||
                                !hasFreshBalance)
                            {
                                previousPortfolioState = await portfolioStateRepository
                                    .GetLatestAsync(
                                        userId,
                                        exchangeAccountId,
                                        persistenceCancellationToken)
                                    .ConfigureAwait(false);
                            }

                            persistenceCancellationToken.ThrowIfCancellationRequested();

                            var positionsAreCurrent =
                                IsApplied(positionsDisposition);
                            var reconciliation = PositionReconciler.Reconcile(
                                exchangeAccountId,
                                trackedPositions,
                                normalizedPositionsObservation,
                                calculatedAt,
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

                            var capital = IsApplied(balanceDisposition) && hasFreshBalance
                                ? new PortfolioCapitalState(
                                    balanceObservation.Balance!.TotalEquity,
                                    balanceObservation.Balance.TotalAvailableBalance,
                                    balanceObservation.ObservedAt,
                                    balanceObservation.Balance.TotalWalletBalance)
                                : previousPortfolioState?.Capital
                                    ?? new PortfolioCapitalState(null, null, null);
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

                            persistedFailureReason = GetFailureReason(
                                IsApplied(balanceDisposition),
                                hasFreshBalance,
                                positionsAreCurrent,
                                normalizedPositionsObservation,
                                reconciliation,
                                positionsFullyReconciled);
                            persistedFullSync =
                                IsApplied(balanceDisposition) &&
                                hasFreshBalance &&
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
                                applicationEvents.Add(
                                    PositionApplicationEventFactory.CreateSyncDegraded(
                                        userId,
                                        accountForPersistence,
                                        persistedFailureReason,
                                        clock.GetUtcNow()));
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
                if (persistedFullSync)
                {
                    var synchronizedAt = persistedAccount?.LastSyncedAt
                        ?? throw new InvalidOperationException(
                            "A successful synchronization did not persist account metadata.");
                    account.RecordSuccessfulSync(synchronizedAt);
                }

                return ExchangeAccountSyncResult.Synchronized(
                    account,
                    persistedPortfolioState
                        ?? throw new InvalidOperationException(
                            "A successful synchronization did not persist a portfolio state."));
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
