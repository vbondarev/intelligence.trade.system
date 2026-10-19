using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Events;
using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Application.Realtime.Observations;
using Intelligence.TradeSystem.Application.Realtime.Ordering;
using Intelligence.TradeSystem.Application.Time;
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
/// Новизна определяется временем фактически принятого капитала в последней сводке. Balance watermark
/// продвигается и неуспешным REST-запросом баланса, который капитал не меняет, поэтому сам по себе не
/// доказывает, что более новое значение уже принято. Watermark только продвигается вперёд, так что
/// более старый REST-снимок баланса не перезаписывает потоковое значение. Наблюдение со временем
/// принятого капитала является повтором только при совпадении всех четырёх значений; иначе порядок
/// неоднозначен и требуется recovery. Watermark позиций и рыночные показатели позиций не изменяются.
/// </para>
/// </remarks>
public sealed class RealtimeWalletObservationService(
    IExchangeAccountRepository accountRepository,
    IPositionRepository positionRepository,
    IPortfolioStateRepository portfolioStateRepository,
    IRealtimeObservationStateStore observationStateStore,
    IExchangeRestOrderingEvidenceStore restOrderingEvidenceStore,
    IExchangeAccountSyncTransaction persistenceTransaction,
    IApplicationEventOutbox applicationEventOutbox,
    TimeProvider? timeProvider = null)
{
    private const int MaxPersistenceAttempts = 3;
    private const int StoredCapitalScale = 18;
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
                                    observation.MessageCreatedAt,
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
        DateTimeOffset? messageCreatedAt,
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

        var previous = await portfolioStateRepository
            .GetLatestAsync(userId, exchangeAccountId, cancellationToken)
            .ConfigureAwait(false);
        var account = latest.Value;

        // Persisted Balance intervals читаются после блокировок: silent Stale запрещён, если биржевая
        // метка или ObservedAt попадает в интервал REST-чтения другого экземпляра, либо если порядок
        // относительно REST-капитала недоказан из-за отсутствия evidence.
        var balanceEvidence = await restOrderingEvidenceStore
            .GetBalanceEvidenceAsync(
                userId,
                exchangeAccountId,
                observedAt,
                messageCreatedAt,
                cancellationToken)
            .ConfigureAwait(false);
        if (RestOrderingDecision.WalletOverlapsBalanceRead(observedAt, messageCreatedAt, balanceEvidence))
        {
            return RealtimeObservationApplyResult.RecoveryRequired(
                RealtimeObservationApplyResult.AmbiguousWithRestBalance);
        }

        if (previous?.Capital.ObservedAt is { } acceptedCapitalAt)
        {
            var capitalOrder = await CompareWalletObservationOrderAsync(
                    userId,
                    exchangeAccountId,
                    account,
                    observedAt,
                    messageCreatedAt,
                    acceptedCapitalAt,
                    previous.Capital,
                    capital,
                    cancellationToken)
                .ConfigureAwait(false);
            if (capitalOrder is not null)
            {
                return capitalOrder;
            }
        }

        var watermarkDisposition = account.AdvanceObservationWatermark(
            ExchangeAccountObservationResource.Balance,
            observedAt);

        var tracked = await positionRepository
            .GetByExchangeAccountAsync(userId, exchangeAccountId, cancellationToken)
            .ConfigureAwait(false);
        if (!HasSamePositionWatermark(watermarkBeforeLocks, tracked))
        {
            throw new ConcurrencyConflictException(
                "The position set changed while the realtime wallet observation acquired its serialization locks.");
        }

        var positions = tracked.Select(versioned => versioned.Value).ToArray();
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

        calculatedAt = PortfolioStateCalculatedAt.Resolve(calculatedAt, previous?.CalculatedAt);
        var portfolioState = PortfolioStateAssembler.AssembleWithCapital(
            capital,
            positions,
            exchangeAccountId,
            calculatedAt,
            PortfolioStaleAfter,
            previous?.PositionsFullyReconciled ?? false);

        if (watermarkDisposition == ExchangeAccountObservationDisposition.Applied)
        {
            await accountRepository
                .SaveAsync(userId, account, latest.Version, cancellationToken)
                .ConfigureAwait(false);
        }

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

    /// <summary>
    /// Сравнивает wallet-наблюдение с принятым капиталом. Ограниченный ObservedAt сам по себе не
    /// доказывает устарелость, если биржевая отметка сообщения новее принятого капитала, и не
    /// доказывает silent Stale, если капитал мог быть принят REST без доступной interval history.
    /// </summary>
    private async Task<RealtimeObservationApplyResult?> CompareWalletObservationOrderAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        ExchangeAccount account,
        DateTimeOffset observedAt,
        DateTimeOffset? messageCreatedAt,
        DateTimeOffset acceptedCapitalAt,
        PortfolioCapitalState acceptedCapital,
        PortfolioCapitalState observedCapital,
        CancellationToken cancellationToken)
    {
        var normalizedObservedAt = TimestampCanonicalizer.ToUtcMicroseconds(observedAt);
        var normalizedCapitalAt = TimestampCanonicalizer.ToUtcMicroseconds(acceptedCapitalAt);
        if (normalizedObservedAt < normalizedCapitalAt)
        {
            if (messageCreatedAt is { } exchangeTime &&
                TimestampCanonicalizer.ToUtcMicroseconds(exchangeTime) > normalizedCapitalAt)
            {
                return RealtimeObservationApplyResult.RecoveryRequired(
                    RealtimeObservationApplyResult.AmbiguousObservationOrder);
            }

            if (await IsAmbiguousOlderWalletWithoutRestEvidenceAsync(
                        userId,
                        exchangeAccountId,
                        account,
                        acceptedCapitalAt,
                        cancellationToken)
                    .ConfigureAwait(false))
            {
                return RealtimeObservationApplyResult.RecoveryRequired(
                    RealtimeObservationApplyResult.AmbiguousWithRestBalance);
            }

            return RealtimeObservationApplyResult.Stale(RealtimeObservationApplyResult.OlderThanBalance);
        }

        if (normalizedObservedAt == normalizedCapitalAt)
        {
            // Равное время не доказывает повтор: разные значения одного момента делают порядок
            // неоднозначным, и принятый капитал не перезаписывается.
            return HasSameValues(acceptedCapital, observedCapital)
                ? RealtimeObservationApplyResult.Duplicate()
                : RealtimeObservationApplyResult.RecoveryRequired(RealtimeObservationApplyResult.BalanceConflict);
        }

        return null;
    }

    /// <summary>
    /// REST-синхронизация обычно поднимает оба watermark; капитал с тем же Balance watermark при
    /// полном отсутствии Balance evidence выглядит как принятый REST-снимок с утерянной историей
    /// интервалов. Silent Stale в этом случае запрещён. Чистый wallet-поток (positions watermark
    /// отсутствует) по-прежнему может получить Stale.
    /// </summary>
    private async Task<bool> IsAmbiguousOlderWalletWithoutRestEvidenceAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        ExchangeAccount account,
        DateTimeOffset acceptedCapitalAt,
        CancellationToken cancellationToken)
    {
        if (account.LastAppliedPositionsObservationAt is null ||
            account.LastAppliedBalanceObservationAt is not { } balanceWatermark)
        {
            return false;
        }

        if (TimestampCanonicalizer.ToUtcMicroseconds(balanceWatermark) !=
            TimestampCanonicalizer.ToUtcMicroseconds(acceptedCapitalAt))
        {
            return false;
        }

        if (await restOrderingEvidenceStore
                .HasBalanceEvidenceCompletingAtAsync(
                    userId,
                    exchangeAccountId,
                    acceptedCapitalAt,
                    cancellationToken)
                .ConfigureAwait(false))
        {
            return false;
        }

        return !await restOrderingEvidenceStore
            .HasAnyBalanceEvidenceAsync(userId, exchangeAccountId, cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool HasSameValues(PortfolioCapitalState accepted, PortfolioCapitalState observed) =>
        SameStoredValue(accepted.TotalEquity, observed.TotalEquity) &&
        SameStoredValue(accepted.AvailableCapital, observed.AvailableCapital) &&
        SameStoredValue(accepted.TotalWalletBalance, observed.TotalWalletBalance) &&
        SameStoredValue(accepted.AccountUnrealizedPnl, observed.AccountUnrealizedPnl);

    /// <summary>
    /// Сравнивает значения с точностью хранения капитала (18 знаков после запятой): принятое значение
    /// могло быть округлено при сохранении.
    /// </summary>
    private static bool SameStoredValue(decimal? accepted, decimal? observed) =>
        accepted is { } left && observed is { } right
            ? decimal.Round(left, StoredCapitalScale, MidpointRounding.AwayFromZero) ==
              decimal.Round(right, StoredCapitalScale, MidpointRounding.AwayFromZero)
            : accepted is null && observed is null;

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
