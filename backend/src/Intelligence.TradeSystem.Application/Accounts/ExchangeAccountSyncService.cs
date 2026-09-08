using Intelligence.TradeSystem.Application.Accounts.Credentials;
using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Portfolio;

namespace Intelligence.TradeSystem.Application.Accounts;

/// <summary>
/// Coordinates one explicitly scoped, read-only exchange account synchronization.
/// </summary>
public sealed class ExchangeAccountSyncService(
    IExchangeAccountRepository accountRepository,
    IExchangeAccountCredentialStore credentialStore,
    IPrivateAccountProviderFactory providerFactory,
    IPositionRepository positionRepository,
    IPortfolioStateRepository portfolioStateRepository,
    IExchangeAccountSyncTransaction persistenceTransaction,
    TimeProvider? timeProvider = null)
    : IExchangeAccountSyncService
{
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

        var calculatedAt = Max(
            clock.GetUtcNow(),
            balanceObservation.ObservedAt,
            normalizedPositionsObservation.ObservedAt);
        PortfolioState? portfolioState = null;
        var failureReason = default(string);
        var successfulSyncAt = default(DateTimeOffset?);
        var accountForPersistence = CopyAccount(account);

        await persistenceTransaction
            .ExecuteAsync(
                async persistenceCancellationToken =>
                {
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
                    if (!hasFreshBalance)
                    {
                        previousPortfolioState = await portfolioStateRepository
                            .GetLatestAsync(
                                userId,
                                exchangeAccountId,
                                persistenceCancellationToken)
                            .ConfigureAwait(false);
                    }
                    persistenceCancellationToken.ThrowIfCancellationRequested();

                    var reconciliation = PositionReconciler.Reconcile(
                        exchangeAccountId,
                        trackedPositions,
                        normalizedPositionsObservation,
                        calculatedAt,
                        PortfolioStaleAfter);
                    var positionsFullyReconciled =
                        reconciliation.IsFullyReconciled &&
                        CoversAllActivePositions(
                            trackedPositions.Concat(reconciliation.NewPositions),
                            normalizedPositionsObservation);

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

                    var capital = hasFreshBalance
                        ? new PortfolioCapitalState(
                            balanceObservation.Balance!.TotalEquity,
                            balanceObservation.Balance.TotalAvailableBalance,
                            balanceObservation.ObservedAt,
                            balanceObservation.Balance.TotalWalletBalance)
                        : previousPortfolioState?.Capital
                            ?? new PortfolioCapitalState(null, null, null);
                    portfolioState = PortfolioStateAssembler.AssembleWithCapital(
                        capital,
                        reconciledPositions,
                        exchangeAccountId,
                        calculatedAt,
                        PortfolioStaleAfter,
                        positionsFullyReconciled);
                    await portfolioStateRepository
                        .SaveAsync(userId, portfolioState, persistenceCancellationToken)
                        .ConfigureAwait(false);

                    var fullySynchronized =
                        hasFreshBalance && positionsFullyReconciled;
                    if (fullySynchronized)
                    {
                        successfulSyncAt = clock.GetUtcNow();
                        accountForPersistence.RecordSuccessfulSync(successfulSyncAt.Value);
                    }
                    else
                    {
                        failureReason = GetFailureReason(
                            hasFreshBalance,
                            normalizedPositionsObservation,
                            reconciliation,
                            positionsFullyReconciled);
                        accountForPersistence.RecordSyncFailure(failureReason);
                    }

                    await accountRepository
                        .SaveAsync(
                            userId,
                            accountForPersistence,
                            loadedAccount.Version,
                            persistenceCancellationToken)
                        .ConfigureAwait(false);
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (successfulSyncAt is { } syncedAt)
        {
            account.RecordSuccessfulSync(syncedAt);
            return ExchangeAccountSyncResult.Synchronized(
                account,
                portfolioState ?? throw new InvalidOperationException(
                    "The synchronization persistence boundary completed without a portfolio state."));
        }

        account.RecordSyncFailure(
            failureReason ?? throw new InvalidOperationException(
                "The synchronization persistence boundary completed without a sync outcome."));
        return ExchangeAccountSyncResult.ExchangeUnavailable();
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
            observedAt: observation.ObservedAt == default ? clock.GetUtcNow() : observation.ObservedAt,
            error: "invalid_positions_observation");
    }

    private static string GetFailureReason(
        bool hasFreshBalance,
        OpenPositionsObservation positionsObservation,
        PositionReconciliationResult reconciliation,
        bool positionsFullyReconciled)
    {
        if (!hasFreshBalance)
        {
            return "balance_failed";
        }

        return positionsObservation.Status switch
        {
            OpenPositionsObservationStatus.Failed => "positions_failed",
            OpenPositionsObservationStatus.Partial => "positions_partial",
            OpenPositionsObservationStatus.Complete when
                !reconciliation.IsFullyReconciled || !positionsFullyReconciled =>
                "positions_ambiguous",
            _ => "positions_failed",
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
            account.LastError);

    private static DateTimeOffset Max(
        DateTimeOffset first,
        DateTimeOffset second,
        DateTimeOffset third) =>
        first >= second
            ? (first >= third ? first : third)
            : (second >= third ? second : third);
}
