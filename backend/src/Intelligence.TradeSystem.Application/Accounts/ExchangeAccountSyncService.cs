using Intelligence.TradeSystem.Application.Accounts.Credentials;
using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Application.Users;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Portfolio;

namespace Intelligence.TradeSystem.Application.Accounts;

/// <summary>
/// Coordinates one user-scoped, read-only exchange account synchronization.
/// </summary>
public sealed class ExchangeAccountSyncService(
    ICurrentUserContext currentUserContext,
    IExchangeAccountRepository accountRepository,
    IExchangeAccountCredentialStore credentialStore,
    IPrivateAccountProviderFactory providerFactory,
    IPositionRepository positionRepository,
    IPortfolioStateRepository portfolioStateRepository,
    IExchangeAccountSyncTransaction persistenceTransaction)
    : IExchangeAccountSyncService
{
    private static readonly TimeSpan PortfolioStaleAfter = TimeSpan.FromMinutes(5);

    public async Task<ExchangeAccountSyncResult> SynchronizeAsync(
        ExchangeAccountId exchangeAccountId,
        CancellationToken cancellationToken = default)
    {
        if (exchangeAccountId == default)
        {
            throw new ArgumentException(
                "ExchangeAccountId must be initialized.",
                nameof(exchangeAccountId));
        }

        var userId = currentUserContext.UserId;
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

        if (balanceObservation.Status != AccountBalanceObservationStatus.Complete ||
            balanceObservation.Balance is not { AccountType: AccountType.Unified } ||
            balanceObservation.ObservedAt == default)
        {
            return ExchangeAccountSyncResult.ExchangeUnavailable();
        }

        var positionsObservation = await providerLease.Provider
            .GetOpenPositionsAsync(
                MarketCategory.Linear,
                symbol: null,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (positionsObservation.Status != OpenPositionsObservationStatus.Complete ||
            positionsObservation.Category != MarketCategory.Linear ||
            positionsObservation.Symbol is not null ||
            positionsObservation.ObservedAt == default)
        {
            return ExchangeAccountSyncResult.ExchangeUnavailable();
        }

        var calculatedAt = Max(
            DateTimeOffset.UtcNow,
            balanceObservation.ObservedAt,
            positionsObservation.ObservedAt);
        PortfolioState? portfolioState = null;

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

                    var reconciliation = PositionReconciler.Reconcile(
                        exchangeAccountId,
                        trackedPositions,
                        positionsObservation,
                        calculatedAt,
                        PortfolioStaleAfter);

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

                    portfolioState = PortfolioStateAssembler.Assemble(
                        balanceObservation.Balance,
                        balanceObservation.ObservedAt,
                        reconciledPositions,
                        exchangeAccountId,
                        calculatedAt,
                        PortfolioStaleAfter);
                    await portfolioStateRepository
                        .SaveAsync(userId, portfolioState, persistenceCancellationToken)
                        .ConfigureAwait(false);

                    account.RecordSuccessfulSync(DateTimeOffset.UtcNow);
                    await accountRepository
                        .SaveAsync(
                            userId,
                            account,
                            loadedAccount.Version,
                            persistenceCancellationToken)
                        .ConfigureAwait(false);
                },
                cancellationToken)
            .ConfigureAwait(false);

        return ExchangeAccountSyncResult.Synchronized(
            account,
            portfolioState ?? throw new InvalidOperationException(
                "The synchronization persistence boundary completed without a portfolio state."));
    }

    private static DateTimeOffset Max(
        DateTimeOffset first,
        DateTimeOffset second,
        DateTimeOffset third) =>
        first >= second
            ? (first >= third ? first : third)
            : (second >= third ? second : third);
}
