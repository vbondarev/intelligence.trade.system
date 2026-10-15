using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Events;
using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Application.Realtime.Observations;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Portfolio;

namespace Intelligence.TradeSystem.Application.Realtime;

/// <summary>
/// Применяет приватное потоковое наблюдение кошелька к капиталу в сводке аккаунта.
/// </summary>
/// <remarks>
/// <para>
/// Принимается только подтверждённый набор account-level значений Unified-аккаунта; отсутствующее
/// значение не заменяется нулём и не смешивается с прежним наблюдением другого времени.
/// </para>
/// <para>
/// Порядок определяется независимым balance watermark аккаунта, поэтому более старый REST-снимок
/// баланса не перезаписывает потоковое значение. Watermark позиций и рыночные показатели позиций
/// не изменяются.
/// </para>
/// </remarks>
public sealed class RealtimeWalletObservationService(
    IExchangeAccountRepository accountRepository,
    IPositionRepository positionRepository,
    IPortfolioStateRepository portfolioStateRepository,
    IRealtimeObservationStateStore observationStateStore,
    IExchangeAccountSyncTransaction persistenceTransaction,
    IApplicationEventOutbox applicationEventOutbox,
    TimeProvider? timeProvider = null)
{
    private const int MaxPersistenceAttempts = 3;
    private static readonly TimeSpan PortfolioStaleAfter = TimeSpan.FromMinutes(5);
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    /// <summary>
    /// Применяет наблюдение от имени поколения наблюдателя <paramref name="generation"/>.
    /// </summary>
    /// <exception cref="ConcurrencyConflictException">Исчерпан лимит повторов сохранения.</exception>
    public async Task<RealtimeObservationApplyResult> ApplyAsync(
        UserId userId,
        long generation,
        PrivateWalletObservation observation,
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

        if (observation.Kind != PrivateWalletObservationKind.PartialUpdate)
        {
            return RealtimeObservationApplyResult.RecoveryRequired(RealtimeObservationApplyResult.WalletUnconfirmed);
        }

        if (ResolveObservedAt(observation) is not { } observedAt)
        {
            return RealtimeObservationApplyResult.RecoveryRequired(RealtimeObservationApplyResult.MissingObservationTime);
        }

        var unified = observation.Balances
            .Where(balance => balance.AccountType == AccountType.Unified)
            .ToArray();
        if (unified is not [{ } balance] ||
            balance.TotalEquity is null ||
            balance.TotalWalletBalance is null ||
            balance.TotalAvailableBalance is null ||
            balance.TotalPerpUnrealizedPnl is null)
        {
            return RealtimeObservationApplyResult.RecoveryRequired(RealtimeObservationApplyResult.WalletIncomplete);
        }

        PortfolioCapitalState capital;
        try
        {
            capital = PortfolioStateAssembler.CreateCapital(balance, observedAt);
        }
        catch (ArgumentException)
        {
            return RealtimeObservationApplyResult.RecoveryRequired(RealtimeObservationApplyResult.WalletIncomplete);
        }

        var exchangeAccountId = observation.ExchangeAccountId;
        for (var attempt = 1; ; attempt++)
        {
            RealtimeObservationApplyResult? result = null;
            try
            {
                await persistenceTransaction
                    .ExecuteAsync(
                        async token =>
                        {
                            result = await PersistAsync(
                                    userId,
                                    exchangeAccountId,
                                    generation,
                                    observedAt,
                                    capital,
                                    token)
                                .ConfigureAwait(false);
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ConcurrencyConflictException) when (attempt < MaxPersistenceAttempts)
            {
                continue;
            }

            return result ?? throw new InvalidOperationException(
                "The realtime wallet persistence boundary completed without an outcome.");
        }
    }

    /// <summary>
    /// Момент наблюдения по часам backend: время формирования сообщения биржей, но не позже
    /// получения. <see langword="null"/>, если биржевая отметка недоступна.
    /// </summary>
    internal static DateTimeOffset? ResolveObservedAt(PrivateWalletObservation observation)
    {
        if (observation.MessageCreatedAt is not { } createdAt || observation.ReceivedAt == default)
        {
            return null;
        }

        return createdAt < observation.ReceivedAt ? createdAt : observation.ReceivedAt;
    }

    private async Task<RealtimeObservationApplyResult> PersistAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        long generation,
        DateTimeOffset observedAt,
        PortfolioCapitalState capital,
        CancellationToken cancellationToken)
    {
        var beforeLocks = await accountRepository
            .GetByIdAsync(userId, exchangeAccountId, cancellationToken)
            .ConfigureAwait(false);
        if (beforeLocks is null || beforeLocks.Value.ConnectionStatus == ExchangeAccountConnectionStatus.Disabled)
        {
            return RealtimeObservationApplyResult.AccountUnavailable();
        }

        var watermarkBeforeLocks = await positionRepository
            .GetVersionWatermarkByExchangeAccountAsync(userId, exchangeAccountId, cancellationToken)
            .ConfigureAwait(false);
        await persistenceTransaction
            .LockPositionsAsync(userId, exchangeAccountId, cancellationToken)
            .ConfigureAwait(false);
        await persistenceTransaction
            .LockAccountAsync(userId, exchangeAccountId, cancellationToken)
            .ConfigureAwait(false);

        var latest = await accountRepository
            .GetByIdAsync(userId, exchangeAccountId, cancellationToken)
            .ConfigureAwait(false);
        if (latest is null || latest.Value.ConnectionStatus == ExchangeAccountConnectionStatus.Disabled)
        {
            return RealtimeObservationApplyResult.AccountUnavailable();
        }

        if (!await observationStateStore
                .TryLockGenerationAsync(userId, exchangeAccountId, generation, cancellationToken)
                .ConfigureAwait(false))
        {
            return RealtimeObservationApplyResult.GenerationSuperseded();
        }

        var account = latest.Value;
        switch (account.AdvanceObservationWatermark(ExchangeAccountObservationResource.Balance, observedAt))
        {
            case ExchangeAccountObservationDisposition.AlreadyApplied:
                return RealtimeObservationApplyResult.Duplicate();
            case ExchangeAccountObservationDisposition.Superseded:
                return RealtimeObservationApplyResult.Stale(RealtimeObservationApplyResult.OlderThanBalance);
        }

        var tracked = await positionRepository
            .GetByExchangeAccountAsync(userId, exchangeAccountId, cancellationToken)
            .ConfigureAwait(false);
        if (!HasSamePositionWatermark(watermarkBeforeLocks, tracked))
        {
            throw new ConcurrencyConflictException(
                "The position set changed while the realtime wallet observation acquired its serialization locks.");
        }

        var positions = tracked.Select(versioned => versioned.Value).ToArray();
        var previous = await portfolioStateRepository
            .GetLatestAsync(userId, exchangeAccountId, cancellationToken)
            .ConfigureAwait(false);
        var calculatedAt = clock.GetUtcNow();
        if (observedAt > calculatedAt)
        {
            calculatedAt = observedAt;
        }

        foreach (var position in positions)
        {
            if (position.TrackingState != PositionTrackingState.Closed && position.LastObservedAt > calculatedAt)
            {
                calculatedAt = position.LastObservedAt;
            }
        }

        var portfolioState = PortfolioStateAssembler.AssembleWithCapital(
            capital,
            positions,
            exchangeAccountId,
            calculatedAt,
            PortfolioStaleAfter,
            previous?.PositionsFullyReconciled ?? false);

        await accountRepository
            .SaveAsync(userId, account, latest.Version, cancellationToken)
            .ConfigureAwait(false);
        await portfolioStateRepository
            .SaveAsync(userId, portfolioState, cancellationToken)
            .ConfigureAwait(false);
        await applicationEventOutbox
            .AddAsync(
                new PortfolioUpdatedEventV1(
                    Guid.NewGuid(),
                    clock.GetUtcNow(),
                    userId.Value,
                    exchangeAccountId.Value),
                cancellationToken)
            .ConfigureAwait(false);
        return RealtimeObservationApplyResult.Applied();
    }

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
}
