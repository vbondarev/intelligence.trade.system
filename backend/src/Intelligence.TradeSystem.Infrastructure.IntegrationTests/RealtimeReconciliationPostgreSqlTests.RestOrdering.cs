using System.Runtime.ExceptionServices;
using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Events;
using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Application.Realtime.Ordering;
using Intelligence.TradeSystem.Application.Realtime.State;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Portfolio;
using Intelligence.TradeSystem.Domain.Snapshots;
using Intelligence.TradeSystem.Infrastructure.Persistence;
using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;
using Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests;

public sealed partial class RealtimeReconciliationPostgreSqlTests
{
    [Fact]
    public async Task Rest_balance_evidence_uses_strict_normalized_interiors_and_account_ownership()
    {
        var (account, _) = await CreateObservedAccountAsync();
        var (otherUserAccount, _) = await CreateObservedAccountAsync();
        var sameUserAccount = ExchangeAccount.Create(
            ExchangeAccountId.New(),
            account.UserId,
            ExchangeId.Bybit,
            ExchangeAccountProviderIdentity.From($"provider-{Guid.NewGuid():N}"),
            "Другой аккаунт",
            ExchangeAccountConnectionStatus.Connected,
            ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions);
        await using (var context = fixture.CreateContext())
        {
            await new ExchangeAccountRepository(context).SaveAsync(
                sameUserAccount.UserId,
                sameUserAccount,
                expectedVersion: null);
        }

        var startedAt = new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero);
        var completedAt = startedAt.AddSeconds(10);
        var inside = startedAt.AddSeconds(5);
        var zeroLengthAt = startedAt.AddMinutes(1);
        ExchangeRestOrderingInterval Evidence(
            ExchangeAccount owner,
            ExchangeRestOrderingKind kind,
            DateTimeOffset started,
            DateTimeOffset completed) =>
            new()
            {
                Id = Guid.NewGuid(),
                ExchangeAccountId = owner.Id,
                Kind = kind,
                Symbol = null,
                StartedAt = started,
                CompletedAt = completed,
                RecordedAt = completedAt.AddMinutes(1),
            };

        var balance = Evidence(account, ExchangeRestOrderingKind.Balance, startedAt, completedAt);
        var zeroLength = Evidence(account, ExchangeRestOrderingKind.Balance, zeroLengthAt, zeroLengthAt);
        var positions = Evidence(
            account,
            ExchangeRestOrderingKind.FullPositionsComplete,
            startedAt,
            completedAt);
        var sameUserBalance = Evidence(
            sameUserAccount,
            ExchangeRestOrderingKind.Balance,
            startedAt,
            completedAt);
        var otherUserBalance = Evidence(
            otherUserAccount,
            ExchangeRestOrderingKind.Balance,
            startedAt,
            completedAt);

        await using (var context = fixture.CreateContext())
        {
            var store = new ExchangeRestOrderingEvidenceStore(context);
            await store.AppendAsync(account.UserId, balance);
            await store.AppendAsync(account.UserId, zeroLength);
            await store.AppendAsync(account.UserId, positions);
            await store.AppendAsync(account.UserId, sameUserBalance);
            await store.AppendAsync(otherUserAccount.UserId, otherUserBalance);
        }

        var invalidInterval = Evidence(
            account,
            ExchangeRestOrderingKind.Balance,
            completedAt,
            startedAt);
        await using (var validationContext = fixture.CreateContext())
        {
            await Assert.ThrowsAsync<ArgumentException>(() =>
                new ExchangeRestOrderingEvidenceStore(validationContext)
                    .AppendAsync(account.UserId, invalidInterval));
        }

        await using (var constraintContext = fixture.CreateContext())
        {
            constraintContext.ExchangeRestOrderingIntervals.Add(new ExchangeRestOrderingIntervalEntity
            {
                Id = invalidInterval.Id,
                ExchangeAccountId = account.Id.Value,
                Kind = invalidInterval.Kind,
                StartedAt = invalidInterval.StartedAt,
                CompletedAt = invalidInterval.CompletedAt,
                RecordedAt = invalidInterval.RecordedAt,
            });
            var exception = await Assert.ThrowsAsync<DbUpdateException>(
                () => constraintContext.SaveChangesAsync());
            var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
            Assert.Equal("ck_exchange_rest_ordering_intervals_bounds", postgresException.ConstraintName);
        }

        var cases = new (string Name, DateTimeOffset? ObservedAt, DateTimeOffset? MessageCreatedAt, bool Expected)[]
        {
            ("оба timestamp null", null, null, false),
            ("observedAt внутри", inside, null, true),
            ("messageCreatedAt внутри", null, inside, true),
            ("observedAt внутри, messageCreatedAt снаружи", inside, completedAt.AddSeconds(1), true),
            ("observedAt снаружи, messageCreatedAt внутри", startedAt.AddSeconds(-1), inside, true),
            ("обе метки внутри одного интервала", inside, inside.AddSeconds(1), true),
            ("обе метки снаружи", startedAt.AddSeconds(-1), completedAt.AddSeconds(1), false),
            ("совпадение с началом", startedAt, null, false),
            ("совпадение с концом", completedAt, null, false),
            ("нулевой интервал", zeroLengthAt, null, false),
            ("ненулевой timezone offset", inside.ToOffset(TimeSpan.FromHours(3)), null, true),
            ("на одну микросекунду после начала", startedAt.AddTicks(TimeSpan.TicksPerMicrosecond), null, true),
            ("на одну микросекунду перед концом", completedAt.AddTicks(-TimeSpan.TicksPerMicrosecond), null, true),
            ("усечение sub-microsecond timestamp до начала", startedAt.AddTicks(1), null, false),
        };

        foreach (var testCase in cases)
        {
            await using var context = fixture.CreateContext();
            var store = new ExchangeRestOrderingEvidenceStore(context);
            var evidence = await store.GetBalanceEvidenceAsync(
                account.UserId,
                account.Id,
                testCase.ObservedAt,
                testCase.MessageCreatedAt);
            var expectedIds = testCase.Expected ? new[] { balance.Id } : [];

            Assert.True(
                expectedIds.SequenceEqual(evidence.Select(interval => interval.Id).OrderBy(id => id)),
                $"Неверный REST Balance evidence для сценария: {testCase.Name}.");
        }

        await using (var context = fixture.CreateContext())
        {
            var store = new ExchangeRestOrderingEvidenceStore(context);
            Assert.Equal(
                new[] { sameUserBalance.Id },
                (await store.GetBalanceEvidenceAsync(
                    account.UserId,
                    sameUserAccount.Id,
                    inside,
                    messageCreatedAt: null))
                    .Select(interval => interval.Id));
            Assert.Empty(await store.GetBalanceEvidenceAsync(
                otherUserAccount.UserId,
                account.Id,
                inside,
                messageCreatedAt: null));
            Assert.Empty(await store.GetBalanceEvidenceAsync(
                account.UserId,
                otherUserAccount.Id,
                inside,
                messageCreatedAt: null));
        }
    }

    [Fact]
    public async Task Rest_ordering_evidence_enforces_user_and_account_isolation_for_reads_and_writes()
    {
        var (accountA, _) = await CreateObservedAccountAsync();
        var (accountB, _) = await CreateObservedAccountAsync();
        Assert.NotEqual(accountA.UserId, accountB.UserId);
        Assert.NotEqual(accountA.Id, accountB.Id);

        ExchangeRestOrderingInterval CreateEvidence(
            ExchangeAccount account,
            ExchangeRestOrderingKind kind,
            string? symbol,
            DateTimeOffset startedAt,
            DateTimeOffset completedAt) =>
            new()
            {
                Id = Guid.NewGuid(),
                ExchangeAccountId = account.Id,
                Kind = kind,
                Symbol = symbol,
                StartedAt = startedAt,
                CompletedAt = completedAt,
                RecordedAt = T1.AddSeconds(100),
            };

        var balanceA = CreateEvidence(
            accountA,
            ExchangeRestOrderingKind.Balance,
            null,
            T1.AddSeconds(10),
            T1.AddSeconds(20));
        var fullPositionsA = CreateEvidence(
            accountA,
            ExchangeRestOrderingKind.FullPositionsComplete,
            null,
            T1.AddSeconds(30),
            T1.AddSeconds(40));
        var scopedSymbolA = CreateEvidence(
            accountA,
            ExchangeRestOrderingKind.ScopedSymbol,
            "ETHUSDT",
            T1.AddSeconds(50),
            T1.AddSeconds(60));
        var balanceB = CreateEvidence(
            accountB,
            ExchangeRestOrderingKind.Balance,
            null,
            T1.AddSeconds(14),
            T1.AddSeconds(16));

        await using (var writeContext = fixture.CreateContext())
        {
            var store = new ExchangeRestOrderingEvidenceStore(writeContext);
            await store.AppendAsync(accountA.UserId, balanceA);
            await store.AppendAsync(accountA.UserId, fullPositionsA);
            await store.AppendAsync(accountA.UserId, scopedSymbolA);
            await store.AppendAsync(accountB.UserId, balanceB);
        }

        var balanceObservedAt = T1.AddSeconds(15);
        var fullPositionsObservedAt = T1.AddSeconds(35);
        var scopedSymbolObservedAt = T1.AddSeconds(55);
        var balanceEvidenceAt = T1.AddSeconds(20);

        await using (var readContext = fixture.CreateContext())
        {
            var store = new ExchangeRestOrderingEvidenceStore(readContext);

            var ownerBalance = Assert.Single(await store.GetBalanceEvidenceAsync(
                accountA.UserId,
                accountA.Id,
                balanceObservedAt,
                messageCreatedAt: null));
            Assert.Equal(balanceA.Id, ownerBalance.Id);
            Assert.Equal(ExchangeRestOrderingKind.Balance, ownerBalance.Kind);

            var ownerFullPositions = Assert.Single(await store.GetPositionEvidenceAsync(
                accountA.UserId,
                accountA.Id,
                "BTCUSDT",
                fullPositionsObservedAt,
                exchangeFactAt: null));
            Assert.Equal(fullPositionsA.Id, ownerFullPositions.Id);
            Assert.Equal(ExchangeRestOrderingKind.FullPositionsComplete, ownerFullPositions.Kind);

            var ownerScopedSymbol = Assert.Single(await store.GetPositionEvidenceAsync(
                accountA.UserId,
                accountA.Id,
                "ETHUSDT",
                scopedSymbolObservedAt,
                exchangeFactAt: null));
            Assert.Equal(scopedSymbolA.Id, ownerScopedSymbol.Id);
            Assert.Equal(ExchangeRestOrderingKind.ScopedSymbol, ownerScopedSymbol.Kind);

            Assert.True(await store.HasAcceptedPositionRestEvidenceCompletedBeforeAsync(
                accountA.UserId,
                accountA.Id,
                "BTCUSDT",
                T1.AddSeconds(30),
                T1.AddSeconds(41)));
            Assert.True(await store.HasAcceptedPositionRestEvidenceCompletedBeforeAsync(
                accountA.UserId,
                accountA.Id,
                "ETHUSDT",
                T1.AddSeconds(50),
                T1.AddSeconds(61)));
            Assert.True(await store.HasBalanceEvidenceCompletingAtAsync(
                accountA.UserId,
                accountA.Id,
                balanceEvidenceAt));

            Assert.Empty(await store.GetPositionEvidenceAsync(
                accountB.UserId,
                accountA.Id,
                "BTCUSDT",
                fullPositionsObservedAt,
                exchangeFactAt: null));
            Assert.Empty(await store.GetPositionEvidenceAsync(
                accountB.UserId,
                accountA.Id,
                "ETHUSDT",
                scopedSymbolObservedAt,
                exchangeFactAt: null));
            Assert.Empty(await store.GetBalanceEvidenceAsync(
                accountB.UserId,
                accountA.Id,
                balanceObservedAt,
                messageCreatedAt: null));
            Assert.False(await store.HasAcceptedPositionRestEvidenceCompletedBeforeAsync(
                accountB.UserId,
                accountA.Id,
                "BTCUSDT",
                T1.AddSeconds(30),
                T1.AddSeconds(41)));
            Assert.False(await store.HasAcceptedPositionRestEvidenceCompletedBeforeAsync(
                accountB.UserId,
                accountA.Id,
                "ETHUSDT",
                T1.AddSeconds(50),
                T1.AddSeconds(61)));
            Assert.False(await store.HasBalanceEvidenceCompletingAtAsync(
                accountB.UserId,
                accountA.Id,
                balanceEvidenceAt));

            Assert.Empty(await store.GetPositionEvidenceAsync(
                accountA.UserId,
                accountB.Id,
                "BTCUSDT",
                fullPositionsObservedAt,
                exchangeFactAt: null));
            Assert.Empty(await store.GetPositionEvidenceAsync(
                accountA.UserId,
                accountB.Id,
                "ETHUSDT",
                scopedSymbolObservedAt,
                exchangeFactAt: null));
            Assert.Empty(await store.GetBalanceEvidenceAsync(
                accountA.UserId,
                accountB.Id,
                balanceObservedAt,
                messageCreatedAt: null));
            Assert.False(await store.HasAcceptedPositionRestEvidenceCompletedBeforeAsync(
                accountA.UserId,
                accountB.Id,
                "BTCUSDT",
                T1.AddSeconds(30),
                T1.AddSeconds(41)));
            Assert.False(await store.HasAcceptedPositionRestEvidenceCompletedBeforeAsync(
                accountA.UserId,
                accountB.Id,
                "ETHUSDT",
                T1.AddSeconds(50),
                T1.AddSeconds(61)));
            Assert.False(await store.HasBalanceEvidenceCompletingAtAsync(
                accountA.UserId,
                accountB.Id,
                balanceEvidenceAt));

            var ownerBBalance = Assert.Single(await store.GetBalanceEvidenceAsync(
                accountB.UserId,
                accountB.Id,
                balanceObservedAt,
                messageCreatedAt: null));
            Assert.Equal(balanceB.Id, ownerBBalance.Id);
            Assert.Equal(ExchangeRestOrderingKind.Balance, ownerBBalance.Kind);
            Assert.True(await store.HasBalanceEvidenceCompletingAtAsync(
                accountB.UserId,
                accountB.Id,
                balanceB.CompletedAt));
        }

        async Task<(
            int EvidenceCount,
            DateTimeOffset? LastSyncedAt,
            DateTimeOffset? LastAppliedBalanceObservationAt,
            DateTimeOffset? LastAppliedPositionsObservationAt,
            ExchangeAccountConnectionStatus ConnectionStatus,
            string? LastError,
            int PortfolioCount,
            int OutboxCount)> ReadAppendSideEffectSnapshotAsync(ExchangeAccount account)
        {
            await using var context = fixture.CreateContext();
            var persistedAccount = await new ExchangeAccountRepository(context)
                .GetByIdAsync(account.UserId, account.Id);
            Assert.NotNull(persistedAccount);
            var accountState = persistedAccount.Value;

            return (
                await context.ExchangeRestOrderingIntervals.AsNoTracking()
                    .CountAsync(interval => interval.ExchangeAccountId == account.Id.Value),
                accountState.LastSyncedAt,
                accountState.LastAppliedBalanceObservationAt,
                accountState.LastAppliedPositionsObservationAt,
                accountState.ConnectionStatus,
                accountState.LastError,
                await CountPortfoliosAsync(context, account),
                await CountOutboxAsync(context, account));
        }

        var accountAStateBeforeRejectedAppend = await ReadAppendSideEffectSnapshotAsync(accountA);
        var accountBStateBeforeRejectedAppend = await ReadAppendSideEffectSnapshotAsync(accountB);
        Assert.Equal(3, accountAStateBeforeRejectedAppend.EvidenceCount);
        Assert.Equal(1, accountBStateBeforeRejectedAppend.EvidenceCount);

        var rejectedEvidence = CreateEvidence(
            accountA,
            ExchangeRestOrderingKind.Balance,
            null,
            T1.AddSeconds(70),
            T1.AddSeconds(80));
        var ownershipException = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var unauthorizedContext = fixture.CreateContext();
            await new ExchangeRestOrderingEvidenceStore(unauthorizedContext)
                .AppendAsync(accountB.UserId, rejectedEvidence);
        });
        Assert.Equal(
            "The exchange account is unavailable in the user scope for REST ordering evidence.",
            ownershipException.Message);

        await using (var verificationContext = fixture.CreateContext())
        {
            var persistedAEntities = await verificationContext.ExchangeRestOrderingIntervals.AsNoTracking()
                .Where(interval => interval.ExchangeAccountId == accountA.Id.Value)
                .OrderBy(interval => interval.StartedAt)
                .ToArrayAsync();
            var persistedA = persistedAEntities
                .Select(interval => (
                    interval.Id,
                    interval.Kind,
                    interval.Symbol,
                    interval.StartedAt,
                    interval.CompletedAt,
                    interval.RecordedAt))
                .ToArray();
            var expectedA = new[]
            {
                (balanceA.Id, balanceA.Kind, balanceA.Symbol, balanceA.StartedAt, balanceA.CompletedAt, balanceA.RecordedAt),
                (fullPositionsA.Id, fullPositionsA.Kind, fullPositionsA.Symbol, fullPositionsA.StartedAt, fullPositionsA.CompletedAt, fullPositionsA.RecordedAt),
                (scopedSymbolA.Id, scopedSymbolA.Kind, scopedSymbolA.Symbol, scopedSymbolA.StartedAt, scopedSymbolA.CompletedAt, scopedSymbolA.RecordedAt),
            }.OrderBy(interval => interval.StartedAt).ToArray();
            Assert.Equal(expectedA, persistedA);
            Assert.DoesNotContain(persistedA, interval => interval.Id == rejectedEvidence.Id);

            var persistedBEntity = Assert.Single(await verificationContext.ExchangeRestOrderingIntervals.AsNoTracking()
                .Where(interval => interval.ExchangeAccountId == accountB.Id.Value)
                .ToArrayAsync());
            var persistedB = (
                persistedBEntity.Id,
                persistedBEntity.Kind,
                persistedBEntity.Symbol,
                persistedBEntity.StartedAt,
                persistedBEntity.CompletedAt,
                persistedBEntity.RecordedAt);
            Assert.Equal(
                (balanceB.Id, balanceB.Kind, balanceB.Symbol, balanceB.StartedAt, balanceB.CompletedAt, balanceB.RecordedAt),
                persistedB);
        }

        Assert.Equal(accountAStateBeforeRejectedAppend, await ReadAppendSideEffectSnapshotAsync(accountA));
        Assert.Equal(accountBStateBeforeRejectedAppend, await ReadAppendSideEffectSnapshotAsync(accountB));

        await using var finalReadContext = fixture.CreateContext();
        var finalStore = new ExchangeRestOrderingEvidenceStore(finalReadContext);
        Assert.Equal(
            new[] { balanceA.Id },
            (await finalStore.GetBalanceEvidenceAsync(
                accountA.UserId,
                accountA.Id,
                balanceObservedAt,
                messageCreatedAt: null))
                .Select(interval => interval.Id));
        Assert.Equal(
            new[] { fullPositionsA.Id },
            (await finalStore.GetPositionEvidenceAsync(
                accountA.UserId,
                accountA.Id,
                "BTCUSDT",
                fullPositionsObservedAt,
                exchangeFactAt: null))
                .Select(interval => interval.Id));
        Assert.Equal(
            new[] { scopedSymbolA.Id },
            (await finalStore.GetPositionEvidenceAsync(
                accountA.UserId,
                accountA.Id,
                "ETHUSDT",
                scopedSymbolObservedAt,
                exchangeFactAt: null))
                .Select(interval => interval.Id));
        Assert.Empty(await finalStore.GetBalanceEvidenceAsync(
            accountB.UserId,
            accountA.Id,
            balanceObservedAt,
            messageCreatedAt: null));
    }

    /// <summary>
    /// H-09: две конкурентные REST-сверки одной учётной записи принимают разные ресурсы
    /// (A — Complete balance / Failed positions, B — Failed balance / Complete positions).
    /// Независимые watermarks и evidence сохраняются; ложного FullReconciliationCommitted нет.
    /// </summary>
    /// <remarks>
    /// Параметр задаёт порядок PostgreSQL commit после пересечения REST-чтений, а не порядок
    /// получения HTTP-ответов: оба снимка фиксируются до освобождения persistence gate.
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task H09_Concurrent_rest_accepting_balance_vs_positions_keeps_independent_watermarks_and_evidence(
        bool aPersistsFirst)
    {
        var (account, _) = await CreateObservedAccountAsync();
        var balanceAcceptedAt = T1.AddSeconds(20);
        var balanceFailedAt = T1.AddSeconds(10);
        var positionsFailedAt = T1.AddSeconds(10);
        var positionsAcceptedAt = T1.AddSeconds(20);
        var backendClock = new FixedTimeProvider(T1.AddSeconds(30));

        using var restOverlap = new Barrier(2);
        var providerA = new TestPrivateProvider(restOverlap)
        {
            Clock = new MutableTimeProvider(balanceAcceptedAt),
            PositionsObservedAt = positionsFailedAt,
            Status = OpenPositionsObservationStatus.Failed,
            Balance = new AccountBalance(AccountType.Unified, 3_000m, 2_900m, 2_500m, 100m, []),
        };
        var providerB = new TestPrivateProvider(restOverlap)
        {
            Clock = new MutableTimeProvider(balanceFailedAt),
            PositionsObservedAt = positionsAcceptedAt,
            BalanceFails = true,
        };
        providerB.SetPosition(OpenPosition(size: 3m));

        var secondEnteredPersistence = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecondPersistence = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await using var servicesA = CreateApplicationServices(
            account,
            providerA,
            backendClock,
            decorateAccountRepository: aPersistsFirst
                ? null
                : inner => new GateSecondAccountReadRepository(
                    inner,
                    secondEnteredPersistence,
                    releaseSecondPersistence));
        await using var servicesB = CreateApplicationServices(
            account,
            providerB,
            backendClock,
            decorateAccountRepository: aPersistsFirst
                ? inner => new GateSecondAccountReadRepository(
                    inner,
                    secondEnteredPersistence,
                    releaseSecondPersistence)
                : null);

        await using var scopeA = servicesA.CreateAsyncScope();
        await using var scopeB = servicesB.CreateAsyncScope();
        Assert.NotSame(
            scopeA.ServiceProvider.GetRequiredService<TradeSystemDbContext>(),
            scopeB.ServiceProvider.GetRequiredService<TradeSystemDbContext>());

        var syncA = scopeA.ServiceProvider.GetRequiredService<ExchangeAccountSyncService>();
        var syncB = scopeB.ServiceProvider.GetRequiredService<ExchangeAccountSyncService>();

        using var cancellation = new CancellationTokenSource();
        var taskA = Task.Run(
            () => syncA.SynchronizeAsync(account.UserId, account.Id, cancellation.Token),
            cancellation.Token);
        var taskB = Task.Run(
            () => syncB.SynchronizeAsync(account.UserId, account.Id, cancellation.Token),
            cancellation.Token);

        var (resultA, resultB) = await AwaitGatedConcurrentRestSyncsAsync(
            taskA,
            taskB,
            secondEnteredPersistence,
            releaseSecondPersistence,
            aPersistsFirst,
            cancellation);

        // Failed-наблюдение, чьи watermark Applied, даёт ExchangeUnavailable; если Failed
        // оказался Superseded после принятия Complete другого экземпляра — Outcome=Synchronized
        // при частичном принятии ресурса. FullReconciliationCommitted в обоих случаях false.
        Assert.False(resultA.FullReconciliationCommitted);
        Assert.False(resultB.FullReconciliationCommitted);
        if (aPersistsFirst)
        {
            Assert.Equal(ExchangeAccountSyncOutcome.ExchangeUnavailable, resultA.Outcome);
            Assert.Equal(ExchangeAccountSyncOutcome.Synchronized, resultB.Outcome);
        }
        else
        {
            Assert.Equal(ExchangeAccountSyncOutcome.Synchronized, resultA.Outcome);
            Assert.Equal(ExchangeAccountSyncOutcome.ExchangeUnavailable, resultB.Outcome);
        }

        Assert.Equal(balanceAcceptedAt, resultA.BalanceReadStartedAt);
        Assert.Equal(balanceAcceptedAt, resultA.BalanceReadCompletedAt);
        Assert.False(resultA.AcceptedPositionsObservation);
        Assert.False(resultA.FullPositionsCompleteAccepted);
        Assert.Null(resultB.BalanceReadStartedAt);
        Assert.Null(resultB.BalanceReadCompletedAt);
        Assert.True(resultB.AcceptedPositionsObservation);
        // Полный снимок позиций принят независимо от Outcome/Balance: evidence и результат совпадают.
        Assert.True(resultB.FullPositionsCompleteAccepted);

        await using var verification = fixture.CreateContext();
        var portfolio = await new PortfolioStateRepository(verification)
            .GetLatestAsync(account.UserId, account.Id);
        Assert.NotNull(portfolio);
        Assert.Equal(3_000m, portfolio!.Capital.TotalEquity);
        Assert.Equal(balanceAcceptedAt, portfolio.Capital.ObservedAt);

        var positions = await new PositionRepository(verification)
            .GetByExchangeAccountAsync(account.UserId, account.Id);
        var position = Assert.Single(positions).Value;
        Assert.Equal("BTCUSDT", position.ExchangePositionKey.InstrumentId.Value);
        Assert.Equal(PositionSide.Long, position.ExchangePositionKey.PositionSide);
        Assert.Equal(3m, position.Size);
        Assert.Equal(PositionTrackingState.Active, position.TrackingState);
        Assert.Equal(
            1,
            positions.Count(entry => entry.Value.TrackingState == PositionTrackingState.Active));

        var stored = await new ExchangeAccountRepository(verification)
            .GetByIdAsync(account.UserId, account.Id);
        Assert.NotNull(stored);
        Assert.Equal(balanceAcceptedAt, stored!.Value.LastAppliedBalanceObservationAt);
        Assert.Equal(positionsAcceptedAt, stored.Value.LastAppliedPositionsObservationAt);
        Assert.Null(stored.Value.LastSyncedAt);

        var intervals = await verification.ExchangeRestOrderingIntervals.AsNoTracking()
            .Where(interval => interval.ExchangeAccountId == account.Id.Value)
            .ToListAsync();
        Assert.Equal(2, intervals.Count);
        var balanceEvidence = Assert.Single(
            intervals,
            interval => interval.Kind == ExchangeRestOrderingKind.Balance);
        Assert.Equal(balanceAcceptedAt, balanceEvidence.StartedAt);
        Assert.Equal(balanceAcceptedAt, balanceEvidence.CompletedAt);
        var positionsEvidence = Assert.Single(
            intervals,
            interval => interval.Kind == ExchangeRestOrderingKind.FullPositionsComplete);
        Assert.Equal(positionsAcceptedAt, positionsEvidence.StartedAt);
        Assert.Equal(T1.AddSeconds(30), positionsEvidence.CompletedAt);
        Assert.DoesNotContain(
            intervals,
            interval => interval.Kind is ExchangeRestOrderingKind.ScopedSymbol);

        Assert.Equal(1, await CountOutboxAsync(verification, account, ApplicationEventTypes.PositionOpened));
        Assert.Equal(0, await CountOutboxAsync(verification, account, ApplicationEventTypes.PositionClosed));
        // Экземпляр с Applied Failed → sync-degraded; второй (Failed Superseded) →
        // Synchronized без FullReconciliationCommitted и публикует exchange-account.updated.
        Assert.Equal(
            1,
            await CountOutboxAsync(verification, account, ApplicationEventTypes.ExchangeAccountSyncDegraded));
        Assert.Equal(
            1,
            await CountOutboxAsync(verification, account, ApplicationEventTypes.ExchangeAccountUpdated));
        Assert.Equal(2, await CountOutboxAsync(verification, account, ApplicationEventTypes.PortfolioUpdated));
        Assert.Equal(
            1,
            await verification.PositionChanges.CountAsync(change => change.PositionId == position.Id.Value));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task H09_rest_failure_before_persistence_gate_releases_barrier_and_preserves_cause(
        bool aPersistsFirst)
    {
        var (account, _) = await CreateObservedAccountAsync();
        var injected = new InvalidOperationException("Инъекция сбоя REST до persistence gate.");
        using var restOverlap = new Barrier(2);
        var providerA = new TestPrivateProvider(restOverlap)
        {
            Clock = new MutableTimeProvider(T1.AddSeconds(20)),
            PositionsObservedAt = T1.AddSeconds(10),
            Status = OpenPositionsObservationStatus.Failed,
            Balance = new AccountBalance(AccountType.Unified, 3_000m, 2_900m, 2_500m, 100m, []),
        };
        var providerB = new TestPrivateProvider(restOverlap)
        {
            Clock = new MutableTimeProvider(T1.AddSeconds(10)),
            PositionsObservedAt = T1.AddSeconds(20),
            BalanceFails = true,
        };
        providerB.SetPosition(OpenPosition(size: 3m));
        if (aPersistsFirst)
        {
            providerB.BeforePositionsResponse = () => throw injected;
        }
        else
        {
            providerA.BeforePositionsResponse = () => throw injected;
        }

        var secondEnteredPersistence = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecondPersistence = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await using var servicesA = CreateApplicationServices(
            account,
            providerA,
            new FixedTimeProvider(T1.AddSeconds(30)),
            decorateAccountRepository: aPersistsFirst
                ? null
                : inner => new GateSecondAccountReadRepository(
                    inner,
                    secondEnteredPersistence,
                    releaseSecondPersistence));
        await using var servicesB = CreateApplicationServices(
            account,
            providerB,
            new FixedTimeProvider(T1.AddSeconds(30)),
            decorateAccountRepository: aPersistsFirst
                ? inner => new GateSecondAccountReadRepository(
                    inner,
                    secondEnteredPersistence,
                    releaseSecondPersistence)
                : null);

        await using var scopeA = servicesA.CreateAsyncScope();
        await using var scopeB = servicesB.CreateAsyncScope();
        var syncA = scopeA.ServiceProvider.GetRequiredService<ExchangeAccountSyncService>();
        var syncB = scopeB.ServiceProvider.GetRequiredService<ExchangeAccountSyncService>();
        using var cancellation = new CancellationTokenSource();
        var taskA = Task.Run(
            () => syncA.SynchronizeAsync(account.UserId, account.Id, cancellation.Token),
            cancellation.Token);
        var taskB = Task.Run(
            () => syncB.SynchronizeAsync(account.UserId, account.Id, cancellation.Token),
            cancellation.Token);

        var exception = await Assert.ThrowsAnyAsync<Exception>(() =>
            AwaitGatedConcurrentRestSyncsAsync(
                taskA,
                taskB,
                secondEnteredPersistence,
                releaseSecondPersistence,
                aPersistsFirst,
                cancellation));

        Assert.Contains(
            FlattenExceptions(exception),
            failure => failure.Message == injected.Message);
        Assert.True(releaseSecondPersistence.Task.IsCompletedSuccessfully);
        Assert.True(taskA.IsCompleted);
        Assert.True(taskB.IsCompleted);
        Assert.False(secondEnteredPersistence.Task.IsCompleted);
    }

    [Fact]
    public async Task H09_wait_releases_gate_when_gated_rest_fails_before_persistence()
    {
        var injected = new InvalidOperationException("Инъекция сбоя REST до persistence gate.");
        var outcome = await RunGatedConcurrentRestFailureAsync(
            first: Task.FromResult(ExchangeAccountSyncResult.NotFound()),
            second: Task.FromException<ExchangeAccountSyncResult>(injected),
            signalEntered: false);

        Assert.Contains(FlattenExceptions(outcome.Exception), failure => failure.Message == injected.Message);
        Assert.True(outcome.Release.Task.IsCompletedSuccessfully);
        Assert.True(outcome.First.IsCompleted);
        Assert.True(outcome.Second.IsCompleted);
        Assert.False(outcome.Entered.Task.IsCompleted);
        Assert.True(outcome.CancellationRequested);
    }

    [Fact]
    public async Task H09_wait_releases_gate_when_first_rest_task_fails_after_persistence_entry()
    {
        var injected = new InvalidOperationException("Инъекция сбоя первой REST-задачи.");
        var outcome = await RunGatedConcurrentRestFailureAsync(
            first: Task.FromException<ExchangeAccountSyncResult>(injected),
            second: Task.FromResult(ExchangeAccountSyncResult.NotFound()),
            signalEntered: true);

        Assert.Contains(FlattenExceptions(outcome.Exception), failure => failure.Message == injected.Message);
        Assert.True(outcome.Release.Task.IsCompletedSuccessfully);
        Assert.True(outcome.First.IsCompleted);
        Assert.True(outcome.Second.IsCompleted);
        Assert.True(outcome.CancellationRequested);
    }

    [Fact]
    public async Task H09_wait_preserves_second_rest_failure_after_first_completes()
    {
        var injected = new InvalidOperationException("Инъекция сбоя второй REST-задачи.");
        var outcome = await RunGatedConcurrentRestFailureAsync(
            first: Task.FromResult(ExchangeAccountSyncResult.NotFound()),
            second: Task.FromException<ExchangeAccountSyncResult>(injected),
            signalEntered: true);

        Assert.Contains(FlattenExceptions(outcome.Exception), failure => failure.Message == injected.Message);
        Assert.True(outcome.Release.Task.IsCompletedSuccessfully);
        Assert.True(outcome.First.IsCompleted);
        Assert.True(outcome.Second.IsCompleted);
    }

    [Fact]
    public async Task H09_wait_reports_timeout_and_unfinished_task_when_second_never_enters()
    {
        var stubborn = new TaskCompletionSource<ExchangeAccountSyncResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var outcome = await RunGatedConcurrentRestFailureAsync(
            first: Task.FromResult(ExchangeAccountSyncResult.NotFound()),
            second: stubborn.Task,
            signalEntered: false,
            waitTimeout: TimeSpan.FromMilliseconds(50),
            cleanupTimeout: TimeSpan.FromMilliseconds(50));

        var flattened = FlattenExceptions(outcome.Exception);
        Assert.Contains(flattened, failure => failure is TimeoutException);
        Assert.Contains(
            flattened,
            failure => failure.Message.Contains("осталась активной", StringComparison.Ordinal));
        Assert.True(outcome.Release.Task.IsCompletedSuccessfully);
        Assert.True(outcome.CancellationRequested);
        Assert.False(outcome.Second.IsCompleted);
        stubborn.TrySetCanceled();
    }

    [Fact]
    public async Task Rest_positions_on_instance_b_blocks_delayed_ws_size_rollback_on_instance_a()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        var restClock = new MutableTimeProvider(T1.AddSeconds(10));
        var restProvider = new TestPrivateProvider
        {
            Clock = restClock,
            PositionsObservedAt = T1.AddSeconds(10),
            BeforePositionsResponse = () =>
            {
                restClock.UtcNow = T1.AddSeconds(20);
                return Task.CompletedTask;
            },
        };
        restProvider.SetPosition(OpenPosition(size: 2m));
        var sync = await SynchronizeViaScopeAsync(account, restProvider, restClock);
        Assert.True(sync.FullPositionsCompleteAccepted);
        Assert.True(await HasRestOrderingKindAsync(account, ExchangeRestOrderingKind.FullPositionsComplete));

        await using var instanceA = CreateApplicationServices(account, restProvider);
        await using var scopeA = instanceA.CreateAsyncScope();
        var delayed = await scopeA.ServiceProvider
            .GetRequiredService<RealtimePositionReconciliationService>()
            .ApplyAsync(account.UserId, generation, OpenObservation(account, T1.AddSeconds(15), 1, OpenPosition(size: 1m)));

        Assert.True(
            delayed.Outcome is RealtimeObservationApplyOutcome.Applied or RealtimeObservationApplyOutcome.RecoveryRequired);
        if (delayed.Outcome == RealtimeObservationApplyOutcome.Applied)
        {
            Assert.True(delayed.ConfirmedByRest);
        }

        await using var verification = fixture.CreateContext();
        var position = await SinglePositionAsync(verification, account);
        Assert.Equal(2m, position.Size);
        Assert.Equal(0, await CountOutboxAsync(verification, account, ApplicationEventTypes.PositionChanged));
    }

    [Fact]
    public async Task Missing_full_positions_evidence_must_not_allow_delayed_ws_open()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        var restClock = new MutableTimeProvider(T1.AddSeconds(10));
        var restProvider = new TestPrivateProvider
        {
            Clock = restClock,
            PositionsObservedAt = T1.AddSeconds(10),
            BeforePositionsResponse = () =>
            {
                restClock.UtcNow = T1.AddSeconds(20);
                return Task.CompletedTask;
            },
        };

        var sync = await SynchronizeViaScopeAsync(account, restProvider, restClock);
        Assert.True(sync.FullPositionsCompleteAccepted);
        Assert.True(await HasRestOrderingKindAsync(account, ExchangeRestOrderingKind.FullPositionsComplete));
        await using (var context = fixture.CreateContext())
        {
            var positionsEvidence = Assert.Single(await context.ExchangeRestOrderingIntervals.AsNoTracking()
                .Where(interval =>
                    interval.ExchangeAccountId == account.Id.Value &&
                    interval.Kind == ExchangeRestOrderingKind.FullPositionsComplete)
                .ToListAsync());
            Assert.Equal(T1.AddSeconds(10), positionsEvidence.StartedAt);
            Assert.Equal(T1.AddSeconds(20), positionsEvidence.CompletedAt);
        }

        int portfoliosBefore;
        int outboxBefore;
        int portfolioUpdatedBefore;
        await using (var context = fixture.CreateContext())
        {
            Assert.Empty(await new PositionRepository(context).GetByExchangeAccountAsync(account.UserId, account.Id));
            var persistedAccount = await new ExchangeAccountRepository(context).GetByIdAsync(account.UserId, account.Id);
            Assert.Equal(T1.AddSeconds(10), persistedAccount!.Value.LastAppliedPositionsObservationAt);
            portfoliosBefore = await CountPortfoliosAsync(context, account);
            outboxBefore = await CountOutboxAsync(context, account);
            portfolioUpdatedBefore = await CountOutboxAsync(
                context,
                account,
                ApplicationEventTypes.PortfolioUpdated);
            Assert.Null(await new PositionObservationCursorStore(context).GetAsync(account.UserId, CursorKey(account)));
        }

        await RemoveFullPositionsCompleteEvidenceAsync(account);
        Assert.False(await HasRestOrderingKindAsync(account, ExchangeRestOrderingKind.FullPositionsComplete));
        Assert.True(await HasRestOrderingKindAsync(account, ExchangeRestOrderingKind.Balance));

        var confirmationClock = new MutableTimeProvider(T1.AddSeconds(21));
        var confirmationProvider = new TestPrivateProvider
        {
            Clock = confirmationClock,
            PositionsObservedAt = T1.AddSeconds(21),
            Status = OpenPositionsObservationStatus.Partial,
        };
        await using var instance = CreateApplicationServices(account, confirmationProvider, confirmationClock);
        await using var scope = instance.CreateAsyncScope();
        var delayed = await scope.ServiceProvider
            .GetRequiredService<RealtimePositionReconciliationService>()
            .ApplyAsync(
                account.UserId,
                generation,
                OpenObservation(account, T1.AddSeconds(15), 1) with
                {
                    ReceivedAt = T1.AddSeconds(21),
                });

        Assert.Equal(RealtimeObservationApplyOutcome.RecoveryRequired, delayed.Outcome);
        Assert.Equal(RealtimeObservationApplyResult.RestConfirmationIncomplete, delayed.ReasonCode);
        Assert.Equal("BTCUSDT", Assert.Single(confirmationProvider.PositionRequests));

        await using var verification = fixture.CreateContext();
        Assert.Empty(await new PositionRepository(verification).GetByExchangeAccountAsync(account.UserId, account.Id));
        Assert.Null(await new PositionObservationCursorStore(verification).GetAsync(account.UserId, CursorKey(account)));
        Assert.Equal(
            0,
            await verification.PositionChanges.CountAsync(change =>
                verification.Positions.Any(position =>
                    position.Id == change.PositionId &&
                    position.ExchangeAccountId == account.Id.Value)));
        Assert.Equal(portfoliosBefore, await CountPortfoliosAsync(verification, account));
        Assert.Equal(outboxBefore, await CountOutboxAsync(verification, account));
        Assert.Equal(
            portfolioUpdatedBefore,
            await CountOutboxAsync(verification, account, ApplicationEventTypes.PortfolioUpdated));
        Assert.Equal(
            0,
            await CountOutboxAsync(verification, account, ApplicationEventTypes.PositionOpened));
        var accountAfter = await new ExchangeAccountRepository(verification).GetByIdAsync(account.UserId, account.Id);
        Assert.Equal(T1.AddSeconds(10), accountAfter!.Value.LastAppliedPositionsObservationAt);
    }

    [Fact]
    public async Task Missing_full_positions_evidence_allows_open_after_fresh_complete_scoped_rest_confirmation()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        var restClock = new MutableTimeProvider(T1.AddSeconds(10));
        var restProvider = new TestPrivateProvider
        {
            Clock = restClock,
            PositionsObservedAt = T1.AddSeconds(10),
            BeforePositionsResponse = () =>
            {
                restClock.UtcNow = T1.AddSeconds(20);
                return Task.CompletedTask;
            },
        };

        var sync = await SynchronizeViaScopeAsync(account, restProvider, restClock);
        Assert.True(sync.FullPositionsCompleteAccepted);

        var (otherAccount, otherGeneration) = await CreateObservedAccountAsync();
        Assert.NotEqual(account.UserId, otherAccount.UserId);
        var otherOpen = await ApplyAsync(
            otherAccount,
            otherGeneration,
            OpenObservation(otherAccount, T1, 7, OpenPosition(size: 7m)));
        Assert.Equal(RealtimeObservationApplyOutcome.Applied, otherOpen.Outcome);

        int portfoliosBefore;
        int outboxBefore;
        int positionOpenedBefore;
        int portfolioUpdatedBefore;
        Position otherPositionBefore;
        PositionObservationCursor otherCursorBefore;
        int otherPortfoliosBefore;
        int otherOutboxBefore;
        int otherChangesBefore;
        await using (var context = fixture.CreateContext())
        {
            Assert.Empty(await new PositionRepository(context).GetByExchangeAccountAsync(account.UserId, account.Id));
            var persistedAccount = await new ExchangeAccountRepository(context).GetByIdAsync(account.UserId, account.Id);
            Assert.Equal(T1.AddSeconds(10), persistedAccount!.Value.LastAppliedPositionsObservationAt);
            Assert.Null(await new PositionObservationCursorStore(context).GetAsync(account.UserId, CursorKey(account)));
            portfoliosBefore = await CountPortfoliosAsync(context, account);
            outboxBefore = await CountOutboxAsync(context, account);
            positionOpenedBefore = await CountOutboxAsync(
                context,
                account,
                ApplicationEventTypes.PositionOpened);
            portfolioUpdatedBefore = await CountOutboxAsync(
                context,
                account,
                ApplicationEventTypes.PortfolioUpdated);

            var otherPosition = Assert.Single(
                await new PositionRepository(context).GetByExchangeAccountAsync(otherAccount.UserId, otherAccount.Id));
            otherPositionBefore = otherPosition.Value;
            otherCursorBefore = (await new PositionObservationCursorStore(context)
                .GetAsync(otherAccount.UserId, CursorKey(otherAccount)))!.Value;
            otherPortfoliosBefore = await CountPortfoliosAsync(context, otherAccount);
            otherOutboxBefore = await CountOutboxAsync(context, otherAccount);
            otherChangesBefore = await CountChangesAsync(context, otherPosition.Value.Id);
        }

        await RemoveFullPositionsCompleteEvidenceAsync(account);
        Assert.False(await HasRestOrderingKindAsync(account, ExchangeRestOrderingKind.FullPositionsComplete));
        Assert.True(await HasRestOrderingKindAsync(account, ExchangeRestOrderingKind.Balance));

        await using (var context = fixture.CreateContext())
        {
            var intervalsAfterRemoval = await context.ExchangeRestOrderingIntervals.AsNoTracking()
                .Where(interval => interval.ExchangeAccountId == account.Id.Value)
                .ToListAsync();
            Assert.Collection(
                intervalsAfterRemoval,
                interval => Assert.Equal(ExchangeRestOrderingKind.Balance, interval.Kind));
            var accountAfterRemoval = await new ExchangeAccountRepository(context)
                .GetByIdAsync(account.UserId, account.Id);
            Assert.Equal(T1.AddSeconds(10), accountAfterRemoval!.Value.LastAppliedPositionsObservationAt);
            Assert.Equal(portfoliosBefore, await CountPortfoliosAsync(context, account));
            Assert.Equal(outboxBefore, await CountOutboxAsync(context, account));
        }

        var receivedAt = T1.AddSeconds(21);
        var confirmationClock = new MutableTimeProvider(receivedAt);
        var restPosition = OpenPosition(size: 2m);
        var confirmationProvider = new TestPrivateProvider
        {
            Clock = confirmationClock,
            Status = OpenPositionsObservationStatus.Complete,
            PositionsObservedAt = T1.AddSeconds(22),
            BeforePositionsResponse = () =>
            {
                confirmationClock.UtcNow = T1.AddSeconds(22);
                return Task.CompletedTask;
            },
        };
        confirmationProvider.SetPosition(restPosition);

        await using var instance = CreateApplicationServices(account, confirmationProvider, confirmationClock);
        await using var scope = instance.CreateAsyncScope();
        var result = await scope.ServiceProvider
            .GetRequiredService<RealtimePositionReconciliationService>()
            .ApplyAsync(
                account.UserId,
                generation,
                OpenObservation(account, T1.AddSeconds(15), 1, OpenPosition(size: 1m)) with
                {
                    ReceivedAt = receivedAt,
                });

        Assert.Equal(RealtimeObservationApplyOutcome.Applied, result.Outcome);
        Assert.True(result.ConfirmedByRest);
        Assert.Equal("BTCUSDT", Assert.Single(confirmationProvider.PositionRequests));

        await using var verification = fixture.CreateContext();
        var positions = await new PositionRepository(verification)
            .GetByExchangeAccountAsync(account.UserId, account.Id);
        var confirmed = Assert.Single(positions);
        Assert.Equal(PositionTrackingState.Active, confirmed.Value.TrackingState);
        Assert.Equal(restPosition.Size, confirmed.Value.Size);
        Assert.Equal(restPosition.AvgPrice, confirmed.Value.AverageEntryPrice);
        Assert.Equal(restPosition.PositionValue, confirmed.Value.PositionValue);
        Assert.Equal(account.Id, confirmed.Value.ExchangePositionKey.ExchangeAccountId);
        Assert.Equal(restPosition.Symbol, confirmed.Value.ExchangePositionKey.InstrumentId.Value);
        Assert.Equal(restPosition.Side, confirmed.Value.ExchangePositionKey.PositionSide);
        Assert.Equal(restPosition.PositionIdx, confirmed.Value.ExchangePositionKey.PositionIdx);
        Assert.Equal(T1.AddSeconds(22), confirmed.Value.LastObservedAt);
        Assert.Equal(
            1,
            await verification.Positions.CountAsync(position =>
                position.ExchangeAccountId == account.Id.Value &&
                position.TrackingState == PositionTrackingState.Active));

        var changes = await verification.PositionChanges.AsNoTracking()
            .Where(change => change.PositionId == confirmed.Value.Id.Value)
            .ToListAsync();
        var change = Assert.Single(changes);
        Assert.Equal(PositionChangeKind.New, change.Kind);
        Assert.Equal(restPosition.Size, change.AfterSize);
        Assert.Equal(T1.AddSeconds(22), change.OccurredAt);

        var cursor = await new PositionObservationCursorStore(verification)
            .GetAsync(account.UserId, CursorKey(account));
        Assert.NotNull(cursor);
        Assert.Equal(1, cursor!.Value.LastProviderSequence);

        var portfolio = await new PortfolioStateRepository(verification)
            .GetLatestAsync(account.UserId, account.Id);
        Assert.NotNull(portfolio);
        var portfolioPosition = Assert.Single(portfolio!.Positions);
        Assert.Equal(confirmed.Value.Id, portfolioPosition.PositionId);
        Assert.Equal(confirmed.Value.ExchangePositionKey, portfolioPosition.ExchangePositionKey);
        Assert.Equal(restPosition.Size, portfolioPosition.Size);
        Assert.Equal(restPosition.AvgPrice, portfolioPosition.AverageEntryPrice);
        Assert.Equal(T1.AddSeconds(22), portfolioPosition.LastObservedAt);
        Assert.Equal(portfoliosBefore + 1, await CountPortfoliosAsync(verification, account));
        Assert.Equal(
            portfolioUpdatedBefore + 1,
            await CountOutboxAsync(verification, account, ApplicationEventTypes.PortfolioUpdated));
        Assert.Equal(positionOpenedBefore + 1, await CountOutboxAsync(
            verification,
            account,
            ApplicationEventTypes.PositionOpened));
        Assert.True(await CountOutboxAsync(verification, account) >= outboxBefore + 2);

        var scopedEvidence = Assert.Single(await verification.ExchangeRestOrderingIntervals.AsNoTracking()
            .Where(interval =>
                interval.ExchangeAccountId == account.Id.Value &&
                interval.Kind == ExchangeRestOrderingKind.ScopedSymbol &&
                interval.Symbol == "BTCUSDT")
            .ToListAsync());
        Assert.True(scopedEvidence.StartedAt >= receivedAt);
        Assert.True(scopedEvidence.CompletedAt >= scopedEvidence.StartedAt);

        var otherPositionAfter = Assert.Single(
            await new PositionRepository(verification).GetByExchangeAccountAsync(otherAccount.UserId, otherAccount.Id));
        Assert.Equal(otherPositionBefore.Id, otherPositionAfter.Value.Id);
        Assert.Equal(otherPositionBefore.Size, otherPositionAfter.Value.Size);
        Assert.Equal(otherChangesBefore, await CountChangesAsync(verification, otherPositionBefore.Id));
        Assert.Equal(otherCursorBefore, (await new PositionObservationCursorStore(verification)
            .GetAsync(otherAccount.UserId, CursorKey(otherAccount)))!.Value);
        Assert.Equal(otherPortfoliosBefore, await CountPortfoliosAsync(verification, otherAccount));
        Assert.Equal(otherOutboxBefore, await CountOutboxAsync(verification, otherAccount));
    }

    [Fact]
    public async Task Fresh_complete_scoped_rest_without_open_does_not_create_lifecycle_or_cursor()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        var restClock = new MutableTimeProvider(T1.AddSeconds(10));
        var provider = new TestPrivateProvider
        {
            Clock = restClock,
            PositionsObservedAt = T1.AddSeconds(10),
            BeforePositionsResponse = () =>
            {
                restClock.UtcNow = T1.AddSeconds(20);
                return Task.CompletedTask;
            },
        };
        Assert.True((await SynchronizeViaScopeAsync(account, provider, restClock)).FullPositionsCompleteAccepted);

        int portfoliosBefore;
        int outboxBefore;
        await using (var context = fixture.CreateContext())
        {
            Assert.Empty(await new PositionRepository(context).GetByExchangeAccountAsync(account.UserId, account.Id));
            portfoliosBefore = await CountPortfoliosAsync(context, account);
            outboxBefore = await CountOutboxAsync(context, account);
            Assert.Null(await new PositionObservationCursorStore(context).GetAsync(account.UserId, CursorKey(account)));
        }

        restClock.UtcNow = T1.AddSeconds(26);
        provider.PositionsObservedAt = T1.AddSeconds(26);
        provider.BeforePositionsResponse = null;
        await using var services = CreateApplicationServices(account, provider, restClock);
        await using var scope = services.CreateAsyncScope();
        var result = await scope.ServiceProvider
            .GetRequiredService<RealtimePositionReconciliationService>()
            .ApplyAsync(
                account.UserId,
                generation,
                OpenObservation(account, T1.AddSeconds(25), 1));

        Assert.Equal(RealtimeObservationApplyOutcome.RecoveryRequired, result.Outcome);
        Assert.Equal(RealtimeObservationApplyResult.AmbiguousObservationOrder, result.ReasonCode);
        Assert.Equal("BTCUSDT", provider.PositionRequests[^1]);
        Assert.False(await HasRestOrderingKindAsync(account, ExchangeRestOrderingKind.ScopedSymbol, "BTCUSDT"));

        await using var verification = fixture.CreateContext();
        Assert.Empty(await new PositionRepository(verification).GetByExchangeAccountAsync(account.UserId, account.Id));
        Assert.Null(await new PositionObservationCursorStore(verification).GetAsync(account.UserId, CursorKey(account)));
        Assert.Equal(
            0,
            await verification.PositionChanges.CountAsync(change =>
                verification.Positions.Any(position =>
                    position.Id == change.PositionId &&
                    position.ExchangeAccountId == account.Id.Value)));
        Assert.Equal(portfoliosBefore, await CountPortfoliosAsync(verification, account));
        Assert.Equal(outboxBefore, await CountOutboxAsync(verification, account));
        Assert.Equal(
            0,
            await CountOutboxAsync(verification, account, ApplicationEventTypes.PositionOpened));
        var accountAfter = await new ExchangeAccountRepository(verification).GetByIdAsync(account.UserId, account.Id);
        Assert.Equal(T1.AddSeconds(10), accountAfter!.Value.LastAppliedPositionsObservationAt);
    }

    [Fact]
    public async Task Rest_balance_then_wallet_inside_interval_on_other_instance_requires_recovery()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        var restClock = new MutableTimeProvider(T1.AddSeconds(10));
        var restProvider = new TestPrivateProvider
        {
            Clock = restClock,
            PositionsObservedAt = T1.AddSeconds(10),
            BeforeBalanceResponse = () =>
            {
                restClock.UtcNow = T1.AddSeconds(20);
                return Task.CompletedTask;
            },
        };
        restProvider.SetPosition(OpenPosition());
        var sync = await SynchronizeViaScopeAsync(account, restProvider, restClock);
        Assert.Equal(T1.AddSeconds(10), sync.BalanceReadStartedAt);
        Assert.Equal(T1.AddSeconds(20), sync.BalanceReadCompletedAt);
        var equityBefore = (await LatestPortfolioAsync(account)).Capital.TotalEquity;

        await using var instanceA = CreateApplicationServices(account, restProvider);
        await using var scopeA = instanceA.CreateAsyncScope();
        var wallet = await scopeA.ServiceProvider
            .GetRequiredService<RealtimeWalletObservationService>()
            .ApplyAsync(
                account.UserId,
                generation,
                WalletObservation(account, T1.AddSeconds(15), totalEquity: 9_999m));

        Assert.Equal(RealtimeObservationApplyOutcome.RecoveryRequired, wallet.Outcome);
        Assert.Equal(RealtimeObservationApplyResult.AmbiguousWithRestBalance, wallet.ReasonCode);
        Assert.Equal(equityBefore, (await LatestPortfolioAsync(account)).Capital.TotalEquity);
    }

    [Fact]
    public async Task Partial_rest_persists_scoped_symbol_without_full_positions_complete()
    {
        var (account, _) = await CreateObservedAccountAsync();
        var clock = new MutableTimeProvider(T1.AddSeconds(10));
        var provider = new TestPrivateProvider
        {
            Clock = clock,
            PositionsObservedAt = T1.AddSeconds(10),
            Status = OpenPositionsObservationStatus.Partial,
            BeforePositionsResponse = () =>
            {
                clock.UtcNow = T1.AddSeconds(15);
                return Task.CompletedTask;
            },
        };
        provider.SetPosition(OpenPosition("BTCUSDT"));
        var result = await SynchronizeViaScopeAsync(account, provider, clock);

        Assert.False(result.FullPositionsCompleteAccepted);
        Assert.True(result.AcceptedPositionsObservation);
        Assert.False(await HasRestOrderingKindAsync(account, ExchangeRestOrderingKind.FullPositionsComplete));
        Assert.True(await HasRestOrderingKindAsync(account, ExchangeRestOrderingKind.ScopedSymbol, "BTCUSDT"));
    }

    [Fact]
    public async Task Rest_evidence_persists_without_realtime_observation_state()
    {
        var account = await CreateAccountWithoutObservationAsync();
        await using (var context = fixture.CreateContext())
        {
            Assert.Null(await new RealtimeObservationStateStore(context).GetAsync(account.UserId, account.Id));
        }

        var clock = new MutableTimeProvider(T1);
        var provider = new TestPrivateProvider
        {
            Clock = clock,
            PositionsObservedAt = T1,
            BeforePositionsResponse = () =>
            {
                clock.UtcNow = T1.AddSeconds(2);
                return Task.CompletedTask;
            },
        };
        provider.SetPosition(OpenPosition());
        var result = await SynchronizeViaScopeAsync(account, provider, clock);

        Assert.Equal(ExchangeAccountSyncOutcome.Synchronized, result.Outcome);
        Assert.True(await HasRestOrderingKindAsync(account, ExchangeRestOrderingKind.FullPositionsComplete));
        Assert.True(await HasRestOrderingKindAsync(account, ExchangeRestOrderingKind.Balance));
        await using var verification = fixture.CreateContext();
        Assert.Null(await new RealtimeObservationStateStore(verification).GetAsync(account.UserId, account.Id));
    }

    [Fact]
    public async Task Sync_rollback_does_not_leave_rest_ordering_evidence()
    {
        var (account, _) = await CreateObservedAccountAsync();
        var clock = new MutableTimeProvider(T1);
        var provider = new TestPrivateProvider
        {
            Clock = clock,
            PositionsObservedAt = T1,
            BeforePositionsResponse = () =>
            {
                clock.UtcNow = T1.AddSeconds(1);
                return Task.CompletedTask;
            },
        };
        provider.SetPosition(OpenPosition(size: 2m));

        await using var context = fixture.CreateContext();
        var inner = new ExchangeAccountSyncTransaction(context);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ExchangeAccountSyncService(
                    new ExchangeAccountRepository(context),
                    new TestCredentialStore(),
                    new TestPrivateProviderFactory(provider),
                    new PositionRepository(context),
                    new PortfolioStateRepository(context),
                    new FailingBeforeCommitTransaction(inner),
                    new ApplicationEventOutbox(context),
                    new ExchangeRestOrderingEvidenceStore(context),
                    new WalletObservationCursorStore(context),
                    clock)
                .SynchronizeAsync(account.UserId, account.Id));

        Assert.False(await HasRestOrderingKindAsync(account, ExchangeRestOrderingKind.FullPositionsComplete));
        Assert.False(await HasRestOrderingKindAsync(account, ExchangeRestOrderingKind.Balance));
        await using var verification = fixture.CreateContext();
        Assert.Empty(await new PositionRepository(verification).GetByExchangeAccountAsync(account.UserId, account.Id));
        var persistedAccount = await new ExchangeAccountRepository(verification)
            .GetByIdAsync(account.UserId, account.Id);
        Assert.NotNull(persistedAccount);
        Assert.Null(persistedAccount!.Value.LastAppliedBalanceObservationAt);
        Assert.Null(persistedAccount.Value.LastAppliedPositionsObservationAt);
        Assert.Equal(0, await CountPortfoliosAsync(verification, account));
        Assert.Equal(0, await CountOutboxAsync(verification, account));
    }

    [Fact]
    public async Task Missing_evidence_after_rest_on_b_does_not_allow_delayed_ws_rollback_on_a()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        var restClock = new MutableTimeProvider(T1.AddSeconds(10));
        var restProvider = new TestPrivateProvider
        {
            Clock = restClock,
            PositionsObservedAt = T1.AddSeconds(10),
            BeforePositionsResponse = () =>
            {
                restClock.UtcNow = T1.AddSeconds(20);
                return Task.CompletedTask;
            },
        };
        restProvider.SetPosition(OpenPosition(size: 3m));
        var sync = await SynchronizeViaScopeAsync(account, restProvider, restClock);
        Assert.True(sync.FullPositionsCompleteAccepted);
        await ClearRestOrderingEvidenceAsync(account);

        restProvider.PositionsObservedAt = T1.AddMinutes(2);
        restProvider.Clock = new MutableTimeProvider(T1.AddMinutes(2));
        await using var instanceA = CreateApplicationServices(
            account,
            restProvider,
            new FixedTimeProvider(T1.AddMinutes(2)));
        await using var scopeA = instanceA.CreateAsyncScope();
        var delayed = await scopeA.ServiceProvider
            .GetRequiredService<RealtimePositionReconciliationService>()
            .ApplyAsync(account.UserId, generation, OpenObservation(account, T1.AddSeconds(15), 1, OpenPosition(size: 2m)));

        Assert.True(
            delayed.Outcome is RealtimeObservationApplyOutcome.Applied
                or RealtimeObservationApplyOutcome.RecoveryRequired);
        if (delayed.Outcome == RealtimeObservationApplyOutcome.Applied)
        {
            Assert.True(delayed.ConfirmedByRest);
        }

        await using var verification = fixture.CreateContext();
        var position = await SinglePositionAsync(verification, account);
        Assert.Equal(3m, position.Size);
        Assert.Equal(0, await CountOutboxAsync(verification, account, ApplicationEventTypes.PositionChanged));
    }

    [Fact]
    public async Task Missing_scoped_rest_evidence_must_not_allow_delayed_ws_position_rollback()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        var fullRestClock = new MutableTimeProvider(T1);
        var fullRestProvider = new TestPrivateProvider
        {
            Clock = fullRestClock,
            PositionsObservedAt = T1,
            BeforePositionsResponse = () =>
            {
                fullRestClock.UtcNow = T1.AddSeconds(2);
                return Task.CompletedTask;
            },
        };
        fullRestProvider.SetPosition(OpenPosition(size: 1m));
        var fullSync = await SynchronizeViaScopeAsync(account, fullRestProvider, fullRestClock);
        Assert.True(fullSync.FullPositionsCompleteAccepted);

        var scopedRestClock = new MutableTimeProvider(T1.AddSeconds(10));
        var scopedRestProvider = new TestPrivateProvider
        {
            Clock = scopedRestClock,
            PositionsObservedAt = T1.AddSeconds(10),
            BeforePositionsResponse = () =>
            {
                scopedRestClock.UtcNow = T1.AddSeconds(20);
                return Task.CompletedTask;
            },
        };
        scopedRestProvider.SetPosition(OpenPosition(size: 3m));

        await using (var instance = CreateApplicationServices(account, scopedRestProvider, scopedRestClock))
        await using (var scope = instance.CreateAsyncScope())
        {
            var confirmation = await scope.ServiceProvider
                .GetRequiredService<RealtimePositionReconciliationService>()
                .ApplyAsync(
                    account.UserId,
                    generation,
                    OpenObservation(account, T1.AddSeconds(5), 10, OpenPosition(size: 2m)),
                    confirmWithRest: true);

            Assert.Equal(RealtimeObservationApplyOutcome.Applied, confirmation.Outcome);
            Assert.True(confirmation.ConfirmedByRest);
        }

        Position positionBefore;
        ConcurrencyVersion positionVersionBefore;
        PositionObservationCursor cursorBefore;
        ConcurrencyVersion cursorVersionBefore;
        int changesBefore;
        int portfoliosBefore;
        int outboxBefore;
        int positionChangedBefore;
        int portfolioUpdatedBefore;
        await using (var context = fixture.CreateContext())
        {
            var accountAfterScopedRest = await new ExchangeAccountRepository(context)
                .GetByIdAsync(account.UserId, account.Id);
            Assert.Equal(T1, accountAfterScopedRest!.Value.LastAppliedPositionsObservationAt);

            var scopedEvidence = Assert.Single(await context.ExchangeRestOrderingIntervals.AsNoTracking()
                .Where(interval =>
                    interval.ExchangeAccountId == account.Id.Value &&
                    interval.Kind == ExchangeRestOrderingKind.ScopedSymbol &&
                    interval.Symbol == "BTCUSDT")
                .ToListAsync());
            Assert.Equal(T1.AddSeconds(10), scopedEvidence.StartedAt);
            Assert.Equal(T1.AddSeconds(20), scopedEvidence.CompletedAt);

            var versionedPosition = Assert.Single(
                await new PositionRepository(context).GetByExchangeAccountAsync(account.UserId, account.Id));
            positionBefore = versionedPosition.Value;
            positionVersionBefore = versionedPosition.Version;
            var cursor = await new PositionObservationCursorStore(context)
                .GetAsync(account.UserId, CursorKey(account));
            cursorBefore = cursor!.Value;
            cursorVersionBefore = cursor.Version;
            changesBefore = await CountChangesAsync(context, positionBefore.Id);
            portfoliosBefore = await CountPortfoliosAsync(context, account);
            outboxBefore = await CountOutboxAsync(context, account);
            positionChangedBefore = await CountOutboxAsync(
                context,
                account,
                ApplicationEventTypes.PositionChanged);
            portfolioUpdatedBefore = await CountOutboxAsync(
                context,
                account,
                ApplicationEventTypes.PortfolioUpdated);
        }

        Assert.Equal(3m, positionBefore.Size);
        Assert.Equal(T1.AddSeconds(10), positionBefore.LastObservedAt);
        await RemoveScopedRestOrderingEvidenceAsync(account, "BTCUSDT");
        Assert.False(await HasRestOrderingKindAsync(account, ExchangeRestOrderingKind.ScopedSymbol, "BTCUSDT"));
        Assert.True(await HasRestOrderingKindAsync(account, ExchangeRestOrderingKind.FullPositionsComplete));

        scopedRestProvider.Status = OpenPositionsObservationStatus.Partial;
        scopedRestProvider.PositionsObservedAt = T1.AddSeconds(21);
        scopedRestProvider.Clock = new MutableTimeProvider(T1.AddSeconds(22));
        await using var otherInstance = CreateApplicationServices(
            account,
            scopedRestProvider,
            new FixedTimeProvider(T1.AddSeconds(22)));
        await using var otherScope = otherInstance.CreateAsyncScope();
        var delayed = await otherScope.ServiceProvider
            .GetRequiredService<RealtimePositionReconciliationService>()
            .ApplyAsync(
                account.UserId,
                generation,
                OpenObservation(account, T1.AddSeconds(15), 11, OpenPosition(size: 2m)) with
                {
                    ReceivedAt = T1.AddSeconds(21),
                });

        Assert.Equal(RealtimeObservationApplyOutcome.RecoveryRequired, delayed.Outcome);
        Assert.Equal(RealtimeObservationApplyResult.RestConfirmationIncomplete, delayed.ReasonCode);
        await using var verification = fixture.CreateContext();
        var positionAfter = Assert.Single(
            await new PositionRepository(verification).GetByExchangeAccountAsync(account.UserId, account.Id));
        var cursorAfter = await new PositionObservationCursorStore(verification)
            .GetAsync(account.UserId, CursorKey(account));
        Assert.Equal(positionBefore.Id, positionAfter.Value.Id);
        Assert.Equal(positionVersionBefore, positionAfter.Version);
        Assert.Equal(3m, positionAfter.Value.Size);
        Assert.Equal(changesBefore, await CountChangesAsync(verification, positionBefore.Id));
        Assert.Equal(cursorVersionBefore, cursorAfter!.Version);
        Assert.Equal(cursorBefore, cursorAfter.Value);
        Assert.Equal(portfoliosBefore, await CountPortfoliosAsync(verification, account));
        Assert.Equal(outboxBefore, await CountOutboxAsync(verification, account));
        Assert.Equal(
            positionChangedBefore,
            await CountOutboxAsync(verification, account, ApplicationEventTypes.PositionChanged));
        Assert.Equal(
            portfolioUpdatedBefore,
            await CountOutboxAsync(verification, account, ApplicationEventTypes.PortfolioUpdated));
        var accountAfterDelayed = await new ExchangeAccountRepository(verification)
            .GetByIdAsync(account.UserId, account.Id);
        Assert.Equal(T1, accountAfterDelayed!.Value.LastAppliedPositionsObservationAt);

        scopedRestProvider.Status = OpenPositionsObservationStatus.Complete;
        scopedRestProvider.PositionsObservedAt = T1.AddSeconds(22);
        scopedRestProvider.Clock = new MutableTimeProvider(T1.AddSeconds(23));
        await using var confirmationInstance = CreateApplicationServices(
            account,
            scopedRestProvider,
            new FixedTimeProvider(T1.AddSeconds(23)));
        await using var confirmationScope = confirmationInstance.CreateAsyncScope();
        var freshlyConfirmed = await confirmationScope.ServiceProvider
            .GetRequiredService<RealtimePositionReconciliationService>()
            .ApplyAsync(
                account.UserId,
                generation,
                OpenObservation(account, T1.AddSeconds(15), 11, OpenPosition(size: 2m)) with
                {
                    ReceivedAt = T1.AddSeconds(21),
                });

        Assert.Equal(RealtimeObservationApplyOutcome.Applied, freshlyConfirmed.Outcome);
        Assert.True(freshlyConfirmed.ConfirmedByRest);
        await using var confirmedState = fixture.CreateContext();
        var confirmedPosition = Assert.Single(
            await new PositionRepository(confirmedState).GetByExchangeAccountAsync(account.UserId, account.Id));
        var confirmedCursor = await new PositionObservationCursorStore(confirmedState)
            .GetAsync(account.UserId, CursorKey(account));
        Assert.Equal(3m, confirmedPosition.Value.Size);
        Assert.Equal(11, confirmedCursor!.Value.LastProviderSequence);
        Assert.Null(confirmedCursor.Value.LastDirectAppliedPositionId);
        Assert.Null(confirmedCursor.Value.LastDirectAppliedPositionVersion);
        Assert.Equal(changesBefore, await CountChangesAsync(confirmedState, confirmedPosition.Value.Id));
        Assert.Equal(
            positionChangedBefore,
            await CountOutboxAsync(confirmedState, account, ApplicationEventTypes.PositionChanged));
    }

    [Fact]
    public async Task Direct_ws_stamp_allows_next_ws_after_rest_evidence_is_lost()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        var restClock = new MutableTimeProvider(T1);
        var provider = new TestPrivateProvider
        {
            Clock = restClock,
            PositionsObservedAt = T1,
            BeforePositionsResponse = () =>
            {
                restClock.UtcNow = T1.AddSeconds(1);
                return Task.CompletedTask;
            },
        };
        provider.SetPosition(OpenPosition(size: 1m));
        Assert.True((await SynchronizeViaScopeAsync(account, provider, restClock)).FullPositionsCompleteAccepted);

        var requestsBeforeDirectUpdates = provider.PositionRequests.Count;
        await using var instance = CreateApplicationServices(
            account,
            provider,
            new FixedTimeProvider(T1.AddSeconds(2)));
        await using (var scope = instance.CreateAsyncScope())
        {
            var firstDirect = await scope.ServiceProvider
                .GetRequiredService<RealtimePositionReconciliationService>()
                .ApplyAsync(account.UserId, generation, OpenObservation(account, T1.AddSeconds(2), 1, OpenPosition(size: 2m)));
            Assert.Equal(RealtimeObservationApplyOutcome.Applied, firstDirect.Outcome);
            Assert.False(firstDirect.ConfirmedByRest);
        }

        await using (var context = fixture.CreateContext())
        {
            var position = Assert.Single(
                await new PositionRepository(context).GetByExchangeAccountAsync(account.UserId, account.Id));
            var cursor = await new PositionObservationCursorStore(context)
                .GetAsync(account.UserId, CursorKey(account));
            Assert.Equal(position.Value.Id, cursor!.Value.LastDirectAppliedPositionId);
            Assert.Equal(position.Version, cursor.Value.LastDirectAppliedPositionVersion);
        }

        await ClearRestOrderingEvidenceAsync(account);
        Assert.False(await HasRestOrderingKindAsync(account, ExchangeRestOrderingKind.FullPositionsComplete));

        await using var nextInstance = CreateApplicationServices(
            account,
            provider,
            new FixedTimeProvider(T1.AddSeconds(3)));
        await using var nextScope = nextInstance.CreateAsyncScope();
        var nextDirect = await nextScope.ServiceProvider
            .GetRequiredService<RealtimePositionReconciliationService>()
            .ApplyAsync(account.UserId, generation, OpenObservation(account, T1.AddSeconds(3), 2, OpenPosition(size: 3m)));

        Assert.Equal(RealtimeObservationApplyOutcome.Applied, nextDirect.Outcome);
        Assert.False(nextDirect.ConfirmedByRest);
        Assert.Equal(requestsBeforeDirectUpdates, provider.PositionRequests.Count);
        await using var verification = fixture.CreateContext();
        Assert.Equal(3m, (await SinglePositionAsync(verification, account)).Size);
    }

    [Fact]
    public async Task Rest_version_change_invalidates_direct_ws_stamp_after_evidence_is_lost()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        var first = await ApplyAsync(account, generation, OpenObservation(account, T1, 1, OpenPosition(size: 1m)));
        Assert.Equal(RealtimeObservationApplyOutcome.Applied, first.Outcome);

        ConcurrencyVersion directPositionVersion;
        ConcurrencyVersion directCursorVersion;
        PositionId directPositionId;
        await using (var context = fixture.CreateContext())
        {
            var directPosition = Assert.Single(
                await new PositionRepository(context).GetByExchangeAccountAsync(account.UserId, account.Id));
            var directCursor = await new PositionObservationCursorStore(context)
                .GetAsync(account.UserId, CursorKey(account));
            directPositionVersion = directPosition.Version;
            directCursorVersion = directCursor!.Version;
            directPositionId = directPosition.Value.Id;
            Assert.Equal(directPositionId, directCursor.Value.LastDirectAppliedPositionId);
            Assert.Equal(directPositionVersion, directCursor.Value.LastDirectAppliedPositionVersion);
        }

        var restClock = new MutableTimeProvider(T1.AddSeconds(10));
        var provider = new TestPrivateProvider
        {
            Clock = restClock,
            PositionsObservedAt = T1.AddSeconds(10),
            BeforePositionsResponse = () =>
            {
                restClock.UtcNow = T1.AddSeconds(11);
                return Task.CompletedTask;
            },
        };
        provider.SetPosition(OpenPosition(size: 3m));
        Assert.True((await SynchronizeViaScopeAsync(account, provider, restClock)).FullPositionsCompleteAccepted);

        Position positionBeforeDelayed;
        ConcurrencyVersion positionVersionBeforeDelayed;
        PositionObservationCursor cursorBeforeDelayed;
        ConcurrencyVersion cursorVersionBeforeDelayed;
        int changesBeforeDelayed;
        int portfoliosBeforeDelayed;
        int outboxBeforeDelayed;
        int positionChangedBeforeDelayed;
        int portfolioUpdatedBeforeDelayed;
        await using (var context = fixture.CreateContext())
        {
            var restPosition = Assert.Single(
                await new PositionRepository(context).GetByExchangeAccountAsync(account.UserId, account.Id));
            var cursor = await new PositionObservationCursorStore(context)
                .GetAsync(account.UserId, CursorKey(account));
            positionBeforeDelayed = restPosition.Value;
            positionVersionBeforeDelayed = restPosition.Version;
            cursorBeforeDelayed = cursor!.Value;
            cursorVersionBeforeDelayed = cursor.Version;
            Assert.True(positionVersionBeforeDelayed.Value > directPositionVersion.Value);
            Assert.Equal(directPositionId, cursorBeforeDelayed.LastDirectAppliedPositionId);
            Assert.Equal(directPositionVersion, cursorBeforeDelayed.LastDirectAppliedPositionVersion);
            changesBeforeDelayed = await CountChangesAsync(context, positionBeforeDelayed.Id);
            portfoliosBeforeDelayed = await CountPortfoliosAsync(context, account);
            outboxBeforeDelayed = await CountOutboxAsync(context, account);
            positionChangedBeforeDelayed = await CountOutboxAsync(
                context,
                account,
                ApplicationEventTypes.PositionChanged);
            portfolioUpdatedBeforeDelayed = await CountOutboxAsync(
                context,
                account,
                ApplicationEventTypes.PortfolioUpdated);
        }

        await ClearRestOrderingEvidenceAsync(account);
        provider.Status = OpenPositionsObservationStatus.Partial;
        provider.PositionsObservedAt = T1.AddSeconds(21);
        provider.Clock = new MutableTimeProvider(T1.AddSeconds(22));
        await using var instance = CreateApplicationServices(
            account,
            provider,
            new FixedTimeProvider(T1.AddSeconds(22)));
        await using var scope = instance.CreateAsyncScope();
        var delayed = await scope.ServiceProvider
            .GetRequiredService<RealtimePositionReconciliationService>()
            .ApplyAsync(
                account.UserId,
                generation,
                OpenObservation(account, T1.AddSeconds(20), 2, OpenPosition(size: 2m)) with
                {
                    ReceivedAt = T1.AddSeconds(21),
                });

        Assert.Equal(RealtimeObservationApplyOutcome.RecoveryRequired, delayed.Outcome);
        Assert.Equal(RealtimeObservationApplyResult.RestConfirmationIncomplete, delayed.ReasonCode);
        await using var verification = fixture.CreateContext();
        var positionAfter = Assert.Single(
            await new PositionRepository(verification).GetByExchangeAccountAsync(account.UserId, account.Id));
        var cursorAfter = await new PositionObservationCursorStore(verification)
            .GetAsync(account.UserId, CursorKey(account));
        Assert.Equal(positionVersionBeforeDelayed, positionAfter.Version);
        Assert.Equal(3m, positionAfter.Value.Size);
        Assert.Equal(changesBeforeDelayed, await CountChangesAsync(verification, positionAfter.Value.Id));
        Assert.Equal(cursorVersionBeforeDelayed, cursorAfter!.Version);
        Assert.Equal(cursorBeforeDelayed, cursorAfter.Value);
        Assert.Equal(portfoliosBeforeDelayed, await CountPortfoliosAsync(verification, account));
        Assert.Equal(outboxBeforeDelayed, await CountOutboxAsync(verification, account));
        Assert.Equal(
            positionChangedBeforeDelayed,
            await CountOutboxAsync(verification, account, ApplicationEventTypes.PositionChanged));
        Assert.Equal(
            portfolioUpdatedBeforeDelayed,
            await CountOutboxAsync(verification, account, ApplicationEventTypes.PortfolioUpdated));
        Assert.Equal(directCursorVersion, cursorVersionBeforeDelayed);
    }

    [Fact]
    public async Task Missing_balance_evidence_after_rest_on_b_requires_recovery_for_ambiguous_wallet_on_a()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        var restClock = new MutableTimeProvider(T1.AddSeconds(10));
        var restProvider = new TestPrivateProvider
        {
            Clock = restClock,
            PositionsObservedAt = T1.AddSeconds(10),
            BeforeBalanceResponse = () =>
            {
                restClock.UtcNow = T1.AddSeconds(20);
                return Task.CompletedTask;
            },
        };
        restProvider.SetPosition(OpenPosition());
        var sync = await SynchronizeViaScopeAsync(account, restProvider, restClock);
        Assert.Equal(T1.AddSeconds(10), sync.BalanceReadStartedAt);
        Assert.Equal(T1.AddSeconds(20), sync.BalanceReadCompletedAt);
        var equityBefore = (await LatestPortfolioAsync(account)).Capital.TotalEquity;
        await ClearRestOrderingEvidenceAsync(account);
        int outboxBefore;
        await using (var beforeContext = fixture.CreateContext())
        {
            outboxBefore = await CountOutboxAsync(beforeContext, account, ApplicationEventTypes.PortfolioUpdated);
        }

        await using var instanceA = CreateApplicationServices(account, restProvider);
        await using var scopeA = instanceA.CreateAsyncScope();
        var wallet = await scopeA.ServiceProvider
            .GetRequiredService<RealtimeWalletObservationService>()
            .ApplyAsync(
                account.UserId,
                generation,
                WalletObservation(account, T1.AddSeconds(15), totalEquity: 9_999m));

        Assert.Equal(RealtimeObservationApplyOutcome.RecoveryRequired, wallet.Outcome);
        Assert.Equal(RealtimeObservationApplyResult.AmbiguousWithRestBalance, wallet.ReasonCode);
        Assert.Equal(equityBefore, (await LatestPortfolioAsync(account)).Capital.TotalEquity);
        await using var verification = fixture.CreateContext();
        Assert.Equal(
            outboxBefore,
            await CountOutboxAsync(verification, account, ApplicationEventTypes.PortfolioUpdated));
    }

    [Fact]
    public async Task Parallel_scopes_preserve_locks_cas_and_generation_fencing_with_rest_evidence()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        var seedClock = new MutableTimeProvider(T1);
        var provider = new TestPrivateProvider
        {
            Clock = seedClock,
            PositionsObservedAt = T1,
            BeforePositionsResponse = () =>
            {
                seedClock.UtcNow = T1.AddSeconds(1);
                return Task.CompletedTask;
            },
        };
        provider.SetPosition(OpenPosition(size: 1m));
        await SynchronizeViaScopeAsync(account, provider, seedClock);

        // WS внутри первого REST-интервала: запоздалый size=2 не должен откатить состояние.
        using var ready = new Barrier(2);
        await using var services = CreateApplicationServices(
            account,
            provider,
            new FixedTimeProvider(T1.AddMinutes(2)));
        var syncTask = Task.Run(async () =>
        {
            ready.SignalAndWait();
            await using var scope = services.CreateAsyncScope();
            provider.SetPosition(OpenPosition(size: 3m));
            provider.PositionsObservedAt = T1.AddMinutes(1);
            provider.ObservedAt = T1.AddMinutes(1);
            provider.Clock = new MutableTimeProvider(T1.AddMinutes(1));
            return await scope.ServiceProvider
                .GetRequiredService<ExchangeAccountSyncService>()
                .SynchronizeAsync(account.UserId, account.Id);
        });
        var wsTask = Task.Run(async () =>
        {
            ready.SignalAndWait();
            await using var scope = services.CreateAsyncScope();
            return await scope.ServiceProvider
                .GetRequiredService<RealtimePositionReconciliationService>()
                .ApplyAsync(
                    account.UserId,
                    generation,
                    OpenObservation(account, T1.AddMilliseconds(500), 2, OpenPosition(size: 2m)));
        });

        await syncTask.WaitAsync(CompletionTimeout);
        var ws = await wsTask.WaitAsync(CompletionTimeout);

        await using var verification = fixture.CreateContext();
        var position = await SinglePositionAsync(verification, account);
        Assert.NotEqual(2m, position.Size);
        Assert.True(position.Size is 1m or 3m);
        Assert.True(
            ws.Outcome is RealtimeObservationApplyOutcome.RecoveryRequired
                or RealtimeObservationApplyOutcome.Applied);
        if (ws.Outcome == RealtimeObservationApplyOutcome.Applied)
        {
            Assert.True(ws.ConfirmedByRest);
        }
    }

    private async Task<ExchangeAccount> CreateAccountWithoutObservationAsync()
    {
        var account = ExchangeAccount.Create(
            ExchangeAccountId.New(),
            UserId.New(),
            ExchangeId.Bybit,
            ExchangeAccountProviderIdentity.From($"provider-{Guid.NewGuid():N}"),
            "REST evidence",
            ExchangeAccountConnectionStatus.Connected,
            ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions);
        await using var context = fixture.CreateContext();
        await new ExchangeAccountRepository(context).SaveAsync(account.UserId, account, expectedVersion: null);
        return account;
    }

    private async Task<ExchangeAccountSyncResult> SynchronizeViaScopeAsync(
        ExchangeAccount account,
        TestPrivateProvider provider,
        TimeProvider clock)
    {
        await using var services = CreateApplicationServices(account, provider, clock);
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<ExchangeAccountSyncService>()
            .SynchronizeAsync(account.UserId, account.Id);
    }

    private async Task<bool> HasRestOrderingKindAsync(
        ExchangeAccount account,
        ExchangeRestOrderingKind kind,
        string? symbol = null)
    {
        await using var context = fixture.CreateContext();
        var query = context.ExchangeRestOrderingIntervals.AsNoTracking()
            .Where(interval =>
                interval.ExchangeAccountId == account.Id.Value &&
                interval.Kind == kind);
        if (symbol is not null)
        {
            query = query.Where(interval => interval.Symbol == symbol);
        }

        return await query.AnyAsync();
    }

    [Fact]
    public async Task Older_position_evidence_on_b_does_not_allow_rollback_on_a_after_newer_evidence_loss()
    {
        var (account, generation) = await CreateObservedAccountAsync();

        var rest1Clock = new MutableTimeProvider(T1.AddSeconds(1));
        var rest1Provider = new TestPrivateProvider
        {
            Clock = rest1Clock,
            PositionsObservedAt = T1.AddSeconds(1),
            BeforePositionsResponse = () =>
            {
                rest1Clock.UtcNow = T1.AddSeconds(2);
                return Task.CompletedTask;
            },
        };
        rest1Provider.SetPosition(OpenPosition(size: 1m));
        Assert.True((await SynchronizeViaScopeAsync(account, rest1Provider, rest1Clock)).FullPositionsCompleteAccepted);
        Guid evidence1Id;
        await using (var context = fixture.CreateContext())
        {
            var evidence1 = Assert.Single(
                await context.ExchangeRestOrderingIntervals.AsNoTracking()
                    .Where(interval =>
                        interval.ExchangeAccountId == account.Id.Value &&
                        interval.Kind == ExchangeRestOrderingKind.FullPositionsComplete)
                    .ToListAsync());
            evidence1Id = evidence1.Id;
            Assert.Equal(T1.AddSeconds(2), evidence1.CompletedAt);
        }

        var rest2Clock = new MutableTimeProvider(T1.AddSeconds(10));
        var rest2Provider = new TestPrivateProvider
        {
            Clock = rest2Clock,
            PositionsObservedAt = T1.AddSeconds(10),
            BeforePositionsResponse = () =>
            {
                rest2Clock.UtcNow = T1.AddSeconds(20);
                return Task.CompletedTask;
            },
        };
        rest2Provider.SetPosition(OpenPosition(size: 3m));
        Assert.True((await SynchronizeViaScopeAsync(account, rest2Provider, rest2Clock)).FullPositionsCompleteAccepted);
        await using (var context = fixture.CreateContext())
        {
            Assert.Equal(2, await context.ExchangeRestOrderingIntervals.CountAsync(interval =>
                interval.ExchangeAccountId == account.Id.Value &&
                interval.Kind == ExchangeRestOrderingKind.FullPositionsComplete));
        }

        await RemoveRestOrderingEvidenceExceptAsync(account, evidence1Id);
        await using (var context = fixture.CreateContext())
        {
            var remaining = Assert.Single(
                await context.ExchangeRestOrderingIntervals.AsNoTracking()
                    .Where(interval => interval.ExchangeAccountId == account.Id.Value)
                    .ToListAsync());
            Assert.Equal(evidence1Id, remaining.Id);
            Assert.Equal(ExchangeRestOrderingKind.FullPositionsComplete, remaining.Kind);
            Assert.Equal(T1.AddSeconds(2), remaining.CompletedAt);
        }

        int changedBefore;
        await using (var beforeContext = fixture.CreateContext())
        {
            changedBefore = await CountOutboxAsync(
                beforeContext,
                account,
                ApplicationEventTypes.PositionChanged);
        }

        rest2Provider.PositionsObservedAt = T1.AddMinutes(2);
        rest2Provider.Clock = new MutableTimeProvider(T1.AddMinutes(2));
        rest2Provider.SetPosition(OpenPosition(size: 3m));
        await using var instanceA = CreateApplicationServices(
            account,
            rest2Provider,
            new FixedTimeProvider(T1.AddMinutes(2)));
        await using var scopeA = instanceA.CreateAsyncScope();
        var delayed = await scopeA.ServiceProvider
            .GetRequiredService<RealtimePositionReconciliationService>()
            .ApplyAsync(
                account.UserId,
                generation,
                OpenObservation(account, T1.AddSeconds(15), 1, OpenPosition(size: 2m)));

        Assert.True(
            delayed.Outcome is RealtimeObservationApplyOutcome.Applied
                or RealtimeObservationApplyOutcome.RecoveryRequired);
        if (delayed.Outcome == RealtimeObservationApplyOutcome.Applied)
        {
            Assert.True(delayed.ConfirmedByRest);
        }

        await using var verification = fixture.CreateContext();
        var position = await SinglePositionAsync(verification, account);
        Assert.Equal(3m, position.Size);
        Assert.NotEqual(2m, position.Size);
        var accountAfter = await new ExchangeAccountRepository(verification)
            .GetByIdAsync(account.UserId, account.Id);
        Assert.Equal(T1.AddSeconds(10), accountAfter!.Value.LastAppliedPositionsObservationAt);
        Assert.Equal(
            changedBefore,
            await CountOutboxAsync(verification, account, ApplicationEventTypes.PositionChanged));
        if (delayed.Outcome == RealtimeObservationApplyOutcome.RecoveryRequired)
        {
            var cursor = await new PositionObservationCursorStore(verification)
                .GetAsync(account.UserId, CursorKey(account));
            Assert.Null(cursor);
        }
    }

    [Fact]
    public async Task Older_balance_evidence_on_b_does_not_allow_silent_stale_on_a_after_newer_evidence_loss()
    {
        var (account, generation) = await CreateObservedAccountAsync();

        var rest1Clock = new MutableTimeProvider(T1.AddSeconds(1));
        var rest1Provider = new TestPrivateProvider
        {
            Clock = rest1Clock,
            PositionsObservedAt = T1.AddSeconds(1),
            Balance = new AccountBalance(AccountType.Unified, 1_000m, 900m, 800m, 10m, []),
            BeforeBalanceResponse = () =>
            {
                rest1Clock.UtcNow = T1.AddSeconds(2);
                return Task.CompletedTask;
            },
        };
        rest1Provider.SetPosition(OpenPosition());
        var sync1 = await SynchronizeViaScopeAsync(account, rest1Provider, rest1Clock);
        Assert.Equal(T1.AddSeconds(2), sync1.BalanceReadCompletedAt);
        Guid balanceEvidence1Id;
        await using (var context = fixture.CreateContext())
        {
            var evidence1 = Assert.Single(
                await context.ExchangeRestOrderingIntervals.AsNoTracking()
                    .Where(interval =>
                        interval.ExchangeAccountId == account.Id.Value &&
                        interval.Kind == ExchangeRestOrderingKind.Balance)
                    .ToListAsync());
            balanceEvidence1Id = evidence1.Id;
        }

        var rest2Clock = new MutableTimeProvider(T1.AddSeconds(10));
        var rest2Provider = new TestPrivateProvider
        {
            Clock = rest2Clock,
            PositionsObservedAt = T1.AddSeconds(10),
            Balance = new AccountBalance(AccountType.Unified, 3_000m, 2_900m, 2_500m, 100m, []),
            BeforeBalanceResponse = () =>
            {
                rest2Clock.UtcNow = T1.AddSeconds(20);
                return Task.CompletedTask;
            },
        };
        rest2Provider.SetPosition(OpenPosition());
        var sync2 = await SynchronizeViaScopeAsync(account, rest2Provider, rest2Clock);
        Assert.Equal(T1.AddSeconds(20), sync2.BalanceReadCompletedAt);
        Assert.Equal(3_000m, (await LatestPortfolioAsync(account)).Capital.TotalEquity);

        await RemoveRestOrderingEvidenceExceptAsync(account, balanceEvidence1Id);
        await using (var context = fixture.CreateContext())
        {
            var remaining = Assert.Single(
                await context.ExchangeRestOrderingIntervals.AsNoTracking()
                    .Where(interval =>
                        interval.ExchangeAccountId == account.Id.Value &&
                        interval.Kind == ExchangeRestOrderingKind.Balance)
                    .ToListAsync());
            Assert.Equal(balanceEvidence1Id, remaining.Id);
            Assert.Equal(T1.AddSeconds(2), remaining.CompletedAt);
        }

        int outboxBefore;
        DateTimeOffset? balanceWatermarkBefore;
        await using (var beforeContext = fixture.CreateContext())
        {
            outboxBefore = await CountOutboxAsync(beforeContext, account, ApplicationEventTypes.PortfolioUpdated);
            var loaded = await new ExchangeAccountRepository(beforeContext)
                .GetByIdAsync(account.UserId, account.Id);
            balanceWatermarkBefore = loaded!.Value.LastAppliedBalanceObservationAt;
            Assert.Equal(T1.AddSeconds(20), balanceWatermarkBefore);
        }

        await using var instanceA = CreateApplicationServices(account, rest2Provider);
        await using var scopeA = instanceA.CreateAsyncScope();
        var wallet = await scopeA.ServiceProvider
            .GetRequiredService<RealtimeWalletObservationService>()
            .ApplyAsync(
                account.UserId,
                generation,
                WalletObservation(account, T1.AddSeconds(15), totalEquity: 2_000m));

        Assert.Equal(RealtimeObservationApplyOutcome.RecoveryRequired, wallet.Outcome);
        Assert.Equal(RealtimeObservationApplyResult.AmbiguousWithRestBalance, wallet.ReasonCode);
        Assert.Equal(3_000m, (await LatestPortfolioAsync(account)).Capital.TotalEquity);
        await using var verification = fixture.CreateContext();
        var accountAfter = await new ExchangeAccountRepository(verification)
            .GetByIdAsync(account.UserId, account.Id);
        Assert.Equal(balanceWatermarkBefore, accountAfter!.Value.LastAppliedBalanceObservationAt);
        Assert.Equal(
            outboxBefore,
            await CountOutboxAsync(verification, account, ApplicationEventTypes.PortfolioUpdated));
    }

    /// <summary>
    /// Моделирует потерю/отсутствие interval history после принятого REST без production delete path.
    /// </summary>
    private async Task RemoveFullPositionsCompleteEvidenceAsync(ExchangeAccount account)
    {
        await using var context = fixture.CreateContext();
        var intervals = await context.ExchangeRestOrderingIntervals
            .Where(interval =>
                interval.ExchangeAccountId == account.Id.Value &&
                interval.Kind == ExchangeRestOrderingKind.FullPositionsComplete)
            .ToListAsync();
        context.ExchangeRestOrderingIntervals.RemoveRange(intervals);
        await context.SaveChangesAsync();
    }

    private async Task ClearRestOrderingEvidenceAsync(ExchangeAccount account)
    {
        await using var context = fixture.CreateContext();
        var intervals = await context.ExchangeRestOrderingIntervals
            .Where(interval => interval.ExchangeAccountId == account.Id.Value)
            .ToListAsync();
        context.ExchangeRestOrderingIntervals.RemoveRange(intervals);
        await context.SaveChangesAsync();
    }

    private async Task RemoveScopedRestOrderingEvidenceAsync(ExchangeAccount account, string symbol)
    {
        await using var context = fixture.CreateContext();
        var intervals = await context.ExchangeRestOrderingIntervals
            .Where(interval =>
                interval.ExchangeAccountId == account.Id.Value &&
                interval.Kind == ExchangeRestOrderingKind.ScopedSymbol &&
                interval.Symbol == symbol)
            .ToListAsync();
        context.ExchangeRestOrderingIntervals.RemoveRange(intervals);
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Удаляет все intervals аккаунта, кроме указанного — модель частичной потери history.
    /// </summary>
    private async Task RemoveRestOrderingEvidenceExceptAsync(ExchangeAccount account, Guid keepId)
    {
        await using var context = fixture.CreateContext();
        var intervals = await context.ExchangeRestOrderingIntervals
            .Where(interval =>
                interval.ExchangeAccountId == account.Id.Value &&
                interval.Id != keepId)
            .ToListAsync();
        context.ExchangeRestOrderingIntervals.RemoveRange(intervals);
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Ожидает детерминированный порядок commit двух REST-сводок. После запуска задач любой
    /// выход, включая тайм-аут входа в persistence, освобождает второй gate и наблюдает обе задачи.
    /// </summary>
    private static async Task<(ExchangeAccountSyncResult ResultA, ExchangeAccountSyncResult ResultB)>
        AwaitGatedConcurrentRestSyncsAsync(
            Task<ExchangeAccountSyncResult> taskA,
            Task<ExchangeAccountSyncResult> taskB,
            TaskCompletionSource<bool> secondEnteredPersistence,
            TaskCompletionSource<bool> releaseSecondPersistence,
            bool aPersistsFirst,
            CancellationTokenSource cancellation,
            TimeSpan? waitTimeout = null,
            TimeSpan? cleanupTimeout = null)
    {
        var timeout = waitTimeout ?? CompletionTimeout;
        var cleanup = cleanupTimeout ?? CompletionTimeout;
        var firstTask = aPersistsFirst ? taskA : taskB;
        var secondTask = aPersistsFirst ? taskB : taskA;
        Exception? primaryFailure = null;
        ExchangeAccountSyncResult? firstResult = null;

        try
        {
            await Task.WhenAny(secondEnteredPersistence.Task, secondTask).WaitAsync(timeout);
            if (!secondEnteredPersistence.Task.IsCompletedSuccessfully)
            {
                primaryFailure = ExceptionFromCompletedTask(secondTask)
                    ?? new TimeoutException(
                        "Вторая REST-операция не вошла в persistence gate в отведённое время.");
            }
            else
            {
                try
                {
                    firstResult = await firstTask.WaitAsync(timeout);
                }
                catch (Exception exception)
                {
                    primaryFailure = exception;
                }
            }
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
        }
        finally
        {
            releaseSecondPersistence.TrySetResult(true);
        }

        if (primaryFailure is null)
        {
            try
            {
                var secondResult = await secondTask.WaitAsync(timeout);
                return aPersistsFirst
                    ? (firstResult!, secondResult)
                    : (secondResult, firstResult!);
            }
            catch (Exception exception)
            {
                primaryFailure = exception;
            }
        }

        var failure = primaryFailure
            ?? new InvalidOperationException(
                "Конкурентные REST-задачи завершились без результата и без зафиксированной ошибки.");
        cancellation.Cancel();
        var observed = await ObserveConcurrentRestTasksAsync(taskA, taskB, cleanup);
        ThrowConcurrentRestFailures(failure, observed);
        throw new InvalidOperationException("Ожидалось повторное возбуждение ошибки конкурентных REST-задач.");
    }

    private static async Task<GatedConcurrentRestFailure> RunGatedConcurrentRestFailureAsync(
        Task<ExchangeAccountSyncResult> first,
        Task<ExchangeAccountSyncResult> second,
        bool signalEntered,
        TimeSpan? waitTimeout = null,
        TimeSpan? cleanupTimeout = null)
    {
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        if (signalEntered)
        {
            entered.TrySetResult(true);
        }

        var boundedWait = waitTimeout ?? TimeSpan.FromSeconds(1);
        var boundedCleanup = cleanupTimeout ?? TimeSpan.FromSeconds(1);
        var exception = await Assert.ThrowsAnyAsync<Exception>(() =>
            AwaitGatedConcurrentRestSyncsAsync(
                first,
                second,
                entered,
                release,
                aPersistsFirst: true,
                cancellation,
                boundedWait,
                boundedCleanup));

        return new GatedConcurrentRestFailure(
            exception,
            first,
            second,
            entered,
            release,
            cancellation.IsCancellationRequested);
    }

    private static async Task<IReadOnlyList<Exception>> ObserveConcurrentRestTasksAsync(
        Task<ExchangeAccountSyncResult> taskA,
        Task<ExchangeAccountSyncResult> taskB,
        TimeSpan timeout)
    {
        var observedA = ObserveConcurrentRestTaskAsync(taskA, "A", timeout);
        var observedB = ObserveConcurrentRestTaskAsync(taskB, "B", timeout);
        return new[] { await observedA, await observedB }
            .OfType<Exception>()
            .ToArray();
    }

    private static async Task<Exception?> ObserveConcurrentRestTaskAsync(
        Task task,
        string name,
        TimeSpan timeout)
    {
        try
        {
            await task.WaitAsync(timeout);
            return null;
        }
        catch (TimeoutException)
        {
            if (task.IsCompletedSuccessfully)
            {
                return null;
            }

            if (task.IsFaulted || task.IsCanceled)
            {
                return ExceptionFromCompletedTask(task);
            }

            _ = task.ContinueWith(
                static completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return new TimeoutException(
                $"REST-задача {name} осталась активной после отмены и ограниченного ожидания cleanup.");
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static Exception? ExceptionFromCompletedTask(Task task)
    {
        if (task.IsFaulted)
        {
            return UnwrapException(task.Exception!);
        }

        return task.IsCanceled ? new OperationCanceledException() : null;
    }

    private static void ThrowConcurrentRestFailures(
        Exception primary,
        IReadOnlyList<Exception> observed)
    {
        var extra = observed
            .Where(exception => !RefersToSameFailure(primary, exception))
            .ToArray();
        if (extra.Length == 0)
        {
            ExceptionDispatchInfo.Capture(primary).Throw();
        }

        throw new AggregateException([primary, .. extra]);
    }

    private static bool RefersToSameFailure(Exception left, Exception right)
    {
        var unwrappedLeft = UnwrapException(left);
        var unwrappedRight = UnwrapException(right);
        return ReferenceEquals(unwrappedLeft, unwrappedRight)
            || (unwrappedLeft.GetType() == unwrappedRight.GetType()
                && unwrappedLeft.Message == unwrappedRight.Message);
    }

    private static Exception UnwrapException(Exception exception) =>
        exception is AggregateException { InnerExceptions.Count: 1 } aggregate
            ? UnwrapException(aggregate.InnerException!)
            : exception;

    private static Exception[] FlattenExceptions(Exception exception) =>
        exception is AggregateException aggregate
            ? [.. aggregate.Flatten().InnerExceptions]
            : [exception];

    private sealed record GatedConcurrentRestFailure(
        Exception Exception,
        Task<ExchangeAccountSyncResult> First,
        Task<ExchangeAccountSyncResult> Second,
        TaskCompletionSource<bool> Entered,
        TaskCompletionSource<bool> Release,
        bool CancellationRequested);

    /// <summary>
    /// Удерживает второе чтение аккаунта внутри persistence-транзакции, пока другой экземпляр
    /// не завершит commit — детерминированный порядок PostgreSQL persistence без Task.Delay.
    /// </summary>
    private sealed class GateSecondAccountReadRepository(
        IExchangeAccountRepository inner,
        TaskCompletionSource<bool> secondRead,
        TaskCompletionSource<bool> release)
        : IExchangeAccountRepository
    {
        private int readCount;

        public Task<IReadOnlyList<Versioned<ExchangeAccount>>> ListActiveAsync(
            UserId userId,
            CancellationToken cancellationToken = default) =>
            inner.ListActiveAsync(userId, cancellationToken);

        public Task<IReadOnlyList<Versioned<ExchangeAccount>>> ListAsync(
            UserId userId,
            CancellationToken cancellationToken = default) =>
            inner.ListAsync(userId, cancellationToken);

        public async Task<Versioned<ExchangeAccount>?> GetByIdAsync(
            UserId userId,
            ExchangeAccountId id,
            CancellationToken cancellationToken = default)
        {
            var result = await inner.GetByIdAsync(userId, id, cancellationToken);
            if (Interlocked.Increment(ref readCount) == 2)
            {
                secondRead.TrySetResult(true);
                await release.Task.WaitAsync(CompletionTimeout, cancellationToken);
            }

            return result;
        }

        public Task<Versioned<ExchangeAccount>?> GetByProviderIdentityAsync(
            UserId userId,
            ExchangeId exchangeId,
            ExchangeAccountProviderIdentity providerIdentity,
            CancellationToken cancellationToken = default) =>
            inner.GetByProviderIdentityAsync(userId, exchangeId, providerIdentity, cancellationToken);

        public Task<ConcurrencyVersion> SaveAsync(
            UserId userId,
            ExchangeAccount account,
            ConcurrencyVersion? expectedVersion,
            CancellationToken cancellationToken = default) =>
            inner.SaveAsync(userId, account, expectedVersion, cancellationToken);

        public Task DeleteAsync(
            UserId userId,
            ExchangeAccountId id,
            ConcurrencyVersion expectedVersion,
            CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(userId, id, expectedVersion, cancellationToken);
    }
}
