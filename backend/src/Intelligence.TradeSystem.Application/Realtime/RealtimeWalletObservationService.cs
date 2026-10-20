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
/// Биржевой порядок wallet определяется persisted <c>MessageCreatedAt</c> курсором независимо от
/// <c>ReceivedAt</c> и нормализованного backend <c>ObservedAt</c>. Balance watermark продвигается и
/// неуспешным REST-запросом баланса, который капитал не меняет, поэтому сам по себе не доказывает,
/// что более новое значение уже принято. Watermark только продвигается вперёд, так что более старый
/// REST-снимок баланса не перезаписывает потоковое значение. Наблюдение с тем же биржевым временем
/// является повтором только при совпадении всех четырёх значений; иначе порядок неоднозначен и
/// требуется recovery. Watermark позиций и рыночные показатели позиций не изменяются.
/// </para>
/// </remarks>
public sealed class RealtimeWalletObservationService(
    IExchangeAccountRepository accountRepository,
    IPositionRepository positionRepository,
    IPortfolioStateRepository portfolioStateRepository,
    IRealtimeObservationStateStore observationStateStore,
    IExchangeRestOrderingEvidenceStore restOrderingEvidenceStore,
    IWalletObservationCursorStore walletCursorStore,
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

        // Без достоверного MessageCreatedAt нельзя продвигать биржевой cursor и нельзя делать
        // предположение о новизне относительно persisted exchange ordering.
        if (observation.MessageCreatedAt is not { } messageCreatedAt)
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
                                    messageCreatedAt,
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
        DateTimeOffset messageCreatedAt,
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

        var stateLock = await observationStateStore
            .TryLockGenerationAsync(userId, exchangeAccountId, generation, cancellationToken)
            .ConfigureAwait(false);
        if (stateLock == RealtimeObservationStateLockResult.GenerationSuperseded)
        {
            return RealtimeObservationApplyResult.GenerationSuperseded();
        }

        if (stateLock == RealtimeObservationStateLockResult.RecoveryRequired)
        {
            return RealtimeObservationApplyResult.RecoveryRequired(
                RealtimeObservationApplyResult.PersistedRecoveryRequired);
        }

        var previous = await portfolioStateRepository
            .GetLatestAsync(userId, exchangeAccountId, cancellationToken)
            .ConfigureAwait(false);
        var account = latest.Value;

        // Persisted Balance evidence читается после блокировок: непересечение интервала не доказывает
        // новизну wallet относительно принятого REST-капитала при несопоставимых часах источников.
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

        var acceptedWalletExchangeAt = await walletCursorStore
            .GetLastAcceptedMessageCreatedAtAsync(userId, exchangeAccountId, cancellationToken)
            .ConfigureAwait(false);
        var capitalOrder = await CompareWalletObservationOrderAsync(
                userId,
                exchangeAccountId,
                account,
                observedAt,
                messageCreatedAt,
                acceptedWalletExchangeAt,
                previous?.Capital,
                capital,
                cancellationToken)
            .ConfigureAwait(false);
        if (capitalOrder is not null)
        {
            return capitalOrder;
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
        await walletCursorStore
            .SaveAcceptedAsync(userId, exchangeAccountId, messageCreatedAt, cancellationToken)
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
    /// Сравнивает wallet-наблюдение с принятым биржевым курсором и капиталом. Биржевой
    /// <paramref name="messageCreatedAt"/> задаёт порядок между wallet-сообщениями, но не относительно
    /// REST-времени; совпадающее с текущим REST-капиталом evidence требует подтверждения при конфликте.
    /// </summary>
    private async Task<RealtimeObservationApplyResult?> CompareWalletObservationOrderAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        ExchangeAccount account,
        DateTimeOffset observedAt,
        DateTimeOffset messageCreatedAt,
        DateTimeOffset? acceptedWalletExchangeAt,
        PortfolioCapitalState? acceptedCapital,
        PortfolioCapitalState observedCapital,
        CancellationToken cancellationToken)
    {
        var normalizedMessageCreatedAt = TimestampCanonicalizer.ToUtcMicroseconds(messageCreatedAt);

        if (acceptedWalletExchangeAt is { } acceptedExchangeAt)
        {
            var normalizedAcceptedExchangeAt = TimestampCanonicalizer.ToUtcMicroseconds(acceptedExchangeAt);
            if (normalizedMessageCreatedAt < normalizedAcceptedExchangeAt)
            {
                return RealtimeObservationApplyResult.Stale(RealtimeObservationApplyResult.OlderThanBalance);
            }

            if (normalizedMessageCreatedAt == normalizedAcceptedExchangeAt)
            {
                // Одинаковый MessageCreatedAt сам по себе не доказывает идентичность сообщений Bybit;
                // повтор подтверждаем совпадением всех финансовых значений.
                return acceptedCapital is not null && HasSameValues(acceptedCapital, observedCapital)
                    ? RealtimeObservationApplyResult.Duplicate()
                    : RealtimeObservationApplyResult.RecoveryRequired(RealtimeObservationApplyResult.BalanceConflict);
            }

            // Более новый биржевой факт — кандидат на применение; ниже сохраняются проверки
            // относительно REST-капитала и неоднозначного ObservedAt.
        }

        if (acceptedCapital?.ObservedAt is not { } acceptedCapitalAt)
        {
            return null;
        }

        var normalizedObservedAt = TimestampCanonicalizer.ToUtcMicroseconds(observedAt);
        var normalizedCapitalAt = TimestampCanonicalizer.ToUtcMicroseconds(acceptedCapitalAt);
        if (await HasAcceptedRestBalanceEvidenceAsync(
                userId,
                exchangeAccountId,
                account,
                acceptedCapitalAt,
                cancellationToken)
            .ConfigureAwait(false))
        {
            if (acceptedWalletExchangeAt is null && normalizedObservedAt == normalizedCapitalAt)
            {
                return HasSameValues(acceptedCapital, observedCapital)
                    ? RealtimeObservationApplyResult.Duplicate()
                    : RealtimeObservationApplyResult.RecoveryRequired(RealtimeObservationApplyResult.BalanceConflict);
            }

            return RealtimeObservationApplyResult.RecoveryRequired(
                RealtimeObservationApplyResult.AmbiguousObservationOrder);
        }

        if (normalizedObservedAt < normalizedCapitalAt)
        {
            // Биржевая отметка новее принятого капитала при откате ObservedAt — порядок неоднозначен.
            if (normalizedMessageCreatedAt > normalizedCapitalAt)
            {
                return RealtimeObservationApplyResult.RecoveryRequired(
                    RealtimeObservationApplyResult.AmbiguousObservationOrder);
            }

            // При известном биржевом курсоре более новое MessageCreatedAt уже пропущено выше; здесь
            // ObservedAt отстаёт, а биржевое время не доказывает новизну относительно капитала.
            if (acceptedWalletExchangeAt is null &&
                await IsAmbiguousOlderWalletWithoutRestEvidenceAsync(
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

            // Когда биржевой cursor ещё unknown, ограниченный ObservedAt остаётся доказательством
            // устарелости относительно принятого капитала (с сохранением P1-B при отсутствии evidence).
            if (acceptedWalletExchangeAt is null)
            {
                return RealtimeObservationApplyResult.Stale(RealtimeObservationApplyResult.OlderThanBalance);
            }

            // Cursor новее по MessageCreatedAt, но ObservedAt отстаёт от капитала — нужен recovery,
            // а не прямое применение с конфликтующей backend timeline.
            return RealtimeObservationApplyResult.RecoveryRequired(
                RealtimeObservationApplyResult.AmbiguousObservationOrder);
        }

        if (normalizedObservedAt == normalizedCapitalAt)
        {
            // Равное backend-время без биржевого cursor: повтор только при совпадении значений.
            if (acceptedWalletExchangeAt is null)
            {
                return HasSameValues(acceptedCapital, observedCapital)
                    ? RealtimeObservationApplyResult.Duplicate()
                    : RealtimeObservationApplyResult.RecoveryRequired(RealtimeObservationApplyResult.BalanceConflict);
            }

            // Cursor уже доказал более новый MessageCreatedAt; равный ObservedAt из-за clock skew не
            // отменяет кандидата — применение продолжается.
            return null;
        }

        return null;
    }

    private async Task<bool> HasAcceptedRestBalanceEvidenceAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        ExchangeAccount account,
        DateTimeOffset acceptedCapitalAt,
        CancellationToken cancellationToken)
    {
        if (account.LastAppliedBalanceObservationAt is not { } balanceWatermark ||
            TimestampCanonicalizer.ToUtcMicroseconds(balanceWatermark) !=
            TimestampCanonicalizer.ToUtcMicroseconds(acceptedCapitalAt))
        {
            return false;
        }

        // Только evidence, завершившееся в момент текущего капитала, связывает его с REST;
        // исторический интервал сам по себе не устанавливает происхождение состояния.
        return await restOrderingEvidenceStore
            .HasBalanceEvidenceCompletingAtAsync(
                userId,
                exchangeAccountId,
                acceptedCapitalAt,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// REST-синхронизация обычно поднимает оба watermark; капитал с тем же Balance watermark без
    /// Balance evidence, завершающегося ровно в <paramref name="acceptedCapitalAt"/>, выглядит как
    /// принятый REST-снимок без доказанной связи с актуальной сверкой. Исторический Balance-интервал
    /// другой сверки происхождения капитала не доказывает — silent Stale запрещён. Чистый
    /// wallet-поток (positions watermark отсутствует) по-прежнему может получить Stale.
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

        return !await restOrderingEvidenceStore
            .HasBalanceEvidenceCompletingAtAsync(
                userId,
                exchangeAccountId,
                acceptedCapitalAt,
                cancellationToken)
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
