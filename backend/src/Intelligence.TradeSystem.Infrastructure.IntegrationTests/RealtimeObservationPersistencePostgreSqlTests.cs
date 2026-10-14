using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Realtime.State;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Infrastructure.Persistence;
using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;
using Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests;

[Collection("PostgreSql-B")]
public sealed class RealtimeObservationPersistencePostgreSqlTests(PostgreSqlFixture fixture)
{
    private static readonly TimeSpan BlockedCommandProbe = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan LockWaitTimeout = TimeSpan.FromSeconds(10);
    private static readonly DateTimeOffset BaseTime = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Observation_state_is_created_saved_and_superseded_by_a_new_generation()
    {
        var account = await CreateSavedAccountAsync();

        await using (var context = fixture.CreateContext())
        {
            var store = new RealtimeObservationStateStore(context);
            Assert.Null(await store.GetAsync(account.UserId, account.Id));

            var created = await store.AdvanceGenerationAsync(
                account.UserId,
                account.Id,
                RealtimeObservationTransportStatus.Initializing);

            Assert.Equal(1, created.Value.Generation);
            Assert.Equal(ConcurrencyVersion.Initial, created.Version);
            Assert.True(created.Value.RecoveryRequired);
            Assert.Null(created.Value.LastHeartbeatAt);

            var saved = await store.SaveAsync(
                account.UserId,
                created.Value with
                {
                    TransportStatus = RealtimeObservationTransportStatus.Connected,
                    LastConnectedAt = BaseTime,
                    LastHeartbeatAt = BaseTime.AddSeconds(20),
                    LastFullyReconciledAt = BaseTime.AddSeconds(5),
                    RecoveryRequired = false,
                },
                created.Version);
            Assert.Equal(created.Version.Next(), saved);
        }

        await using (var context = fixture.CreateContext())
        {
            var store = new RealtimeObservationStateStore(context);
            var reloaded = await store.GetAsync(account.UserId, account.Id);
            Assert.NotNull(reloaded);
            Assert.Equal(new ConcurrencyVersion(2), reloaded!.Version);
            Assert.Equal(RealtimeObservationTransportStatus.Connected, reloaded.Value.TransportStatus);
            Assert.Equal(BaseTime, reloaded.Value.LastConnectedAt);
            Assert.Equal(BaseTime.AddSeconds(20), reloaded.Value.LastHeartbeatAt);
            Assert.False(reloaded.Value.RecoveryRequired);

            var degraded = await store.SaveAsync(
                account.UserId,
                reloaded.Value with
                {
                    TransportStatus = RealtimeObservationTransportStatus.Degraded,
                    FailureCode = "auth.rejected",
                },
                reloaded.Version);

            var next = await store.AdvanceGenerationAsync(
                account.UserId,
                account.Id,
                RealtimeObservationTransportStatus.Reconnecting);

            Assert.Equal(2, next.Value.Generation);
            Assert.Equal(degraded.Next(), next.Version);
            Assert.Equal(RealtimeObservationTransportStatus.Reconnecting, next.Value.TransportStatus);
            Assert.True(next.Value.RecoveryRequired);
            Assert.Null(next.Value.LastHeartbeatAt);
            Assert.Null(next.Value.FailureCode);
            Assert.Equal(BaseTime, next.Value.LastConnectedAt);
            Assert.Equal(BaseTime.AddSeconds(5), next.Value.LastFullyReconciledAt);
            Assert.False(next.Value.IsTransportOperational(BaseTime.AddSeconds(21), TimeSpan.FromMinutes(1)));

            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => store.SaveAsync(
                account.UserId,
                next.Value with { Generation = 1 },
                next.Version));
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => store.SaveAsync(
                account.UserId,
                next.Value,
                degraded));
        }
    }

    [Fact]
    public async Task Observation_state_is_isolated_by_user()
    {
        var account = await CreateSavedAccountAsync();
        var otherUser = UserId.New();

        await using var context = fixture.CreateContext();
        var store = new RealtimeObservationStateStore(context);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => store.AdvanceGenerationAsync(
            otherUser,
            account.Id,
            RealtimeObservationTransportStatus.Initializing));
        Assert.False(await context.ExchangeRealtimeObservations
            .AnyAsync(observation => observation.ExchangeAccountId == account.Id.Value));

        var state = await store.AdvanceGenerationAsync(
            account.UserId,
            account.Id,
            RealtimeObservationTransportStatus.Initializing);

        Assert.Null(await store.GetAsync(otherUser, account.Id));
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => store.SaveAsync(
            otherUser,
            state.Value with { RecoveryRequired = false },
            state.Version));
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => store.AdvanceGenerationAsync(
            otherUser,
            account.Id,
            RealtimeObservationTransportStatus.Stopped));

        var unchanged = await store.GetAsync(account.UserId, account.Id);
        Assert.Equal(state, unchanged);
    }

    [Fact]
    public async Task Cursors_of_two_symbols_in_one_account_are_stored_and_advanced_independently()
    {
        var account = await CreateSavedAccountAsync();
        var generation = await StartObservationAsync(account);
        var btc = CreateKey(account, "BTCUSDT");
        var eth = CreateKey(account, "ETHUSDT");

        await using (var context = fixture.CreateContext())
        {
            var store = new PositionObservationCursorStore(context);
            Assert.Null(await store.GetAsync(account.UserId, btc));

            Assert.Equal(
                ConcurrencyVersion.Initial,
                await store.SaveAsync(account.UserId, CreateCursor(eth, 900, BaseTime), null, generation));
            Assert.Equal(
                ConcurrencyVersion.Initial,
                await store.SaveAsync(account.UserId, CreateCursor(btc, 10, BaseTime), null, generation));

            var advanced = await store.SaveAsync(
                account.UserId,
                CreateCursor(btc, 11, BaseTime.AddSeconds(1), "fp-btc-11"),
                ConcurrencyVersion.Initial,
                generation);
            Assert.Equal(new ConcurrencyVersion(2), advanced);
        }

        await using (var context = fixture.CreateContext())
        {
            var cursors = await new PositionObservationCursorStore(context)
                .GetByExchangeAccountAsync(account.UserId, account.Id);

            Assert.Collection(
                cursors,
                cursor =>
                {
                    Assert.Equal(btc, cursor.Value.Key);
                    Assert.Equal(11, cursor.Value.LastProviderSequence);
                    Assert.Equal(BaseTime.AddSeconds(1), cursor.Value.LastProviderUpdatedAt);
                    Assert.Equal("fp-btc-11", cursor.Value.LastAcceptedFingerprint);
                    Assert.Equal(new ConcurrencyVersion(2), cursor.Version);
                },
                cursor =>
                {
                    Assert.Equal(eth, cursor.Value.Key);
                    Assert.Equal(900, cursor.Value.LastProviderSequence);
                    Assert.Equal(ConcurrencyVersion.Initial, cursor.Version);
                });
        }
    }

    [Fact]
    public async Task Cursor_accepts_a_repeated_revision_and_rejects_stale_or_reset_revisions()
    {
        var account = await CreateSavedAccountAsync();
        var generation = await StartObservationAsync(account);
        var key = CreateKey(account, "SOLUSDT");

        await using var context = fixture.CreateContext();
        var store = new PositionObservationCursorStore(context);
        var version = await store.SaveAsync(account.UserId, CreateCursor(key, 50, BaseTime), null, generation);

        version = await store.SaveAsync(
            account.UserId,
            CreateCursor(key, 50, BaseTime, "fp-repeated"),
            version,
            generation);
        Assert.Equal(new ConcurrencyVersion(2), version);

        PositionObservationCursor[] rejected =
        [
            CreateCursor(key, 49, BaseTime),
            CreateCursor(key, 51, BaseTime.AddTicks(-10)),
            CreateCursor(key, null, BaseTime),
            CreateCursor(key, 51, null),
        ];
        foreach (var cursor in rejected)
        {
            await Assert.ThrowsAsync<ConcurrencyConflictException>(
                () => store.SaveAsync(account.UserId, cursor, version, generation));
        }

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => store.SaveAsync(
            account.UserId,
            CreateCursor(key, 60, BaseTime.AddSeconds(1)),
            null,
            generation));
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => store.SaveAsync(
            account.UserId,
            CreateCursor(key, 60, BaseTime.AddSeconds(1)),
            ConcurrencyVersion.Initial,
            generation));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(
            account.UserId,
            CreateCursor(key, null, null),
            version,
            generation));

        var stored = await store.GetAsync(account.UserId, key);
        Assert.NotNull(stored);
        Assert.Equal(version, stored!.Version);
        Assert.Equal(50, stored.Value.LastProviderSequence);
        Assert.Equal(BaseTime, stored.Value.LastProviderUpdatedAt);
        Assert.Equal("fp-repeated", stored.Value.LastAcceptedFingerprint);
    }

    [Fact]
    public async Task Concurrent_cursor_writes_with_the_same_expected_version_allow_exactly_one_winner()
    {
        var account = await CreateSavedAccountAsync();
        var generation = await StartObservationAsync(account);
        var key = CreateKey(account, "XRPUSDT");
        await using (var context = fixture.CreateContext())
        {
            await new PositionObservationCursorStore(context)
                .SaveAsync(account.UserId, CreateCursor(key, 1, BaseTime), null, generation);
        }

        await using var first = fixture.CreateContext();
        await using var second = fixture.CreateContext();
        var results = await Task.WhenAll(
            TrySaveAsync(first, account.UserId, CreateCursor(key, 2, BaseTime.AddSeconds(1), "fp-first"), generation),
            TrySaveAsync(second, account.UserId, CreateCursor(key, 3, BaseTime.AddSeconds(2), "fp-second"), generation));

        Assert.Single(results, succeeded => succeeded);
        await using var reader = fixture.CreateContext();
        var stored = await new PositionObservationCursorStore(reader).GetAsync(account.UserId, key);
        Assert.Equal(new ConcurrencyVersion(2), stored!.Version);
    }

    [Fact]
    public async Task Concurrent_first_inserts_of_one_cursor_allow_exactly_one_winner()
    {
        var account = await CreateSavedAccountAsync();
        var generation = await StartObservationAsync(account);
        var key = CreateKey(account, "DOGEUSDT");

        await using var first = fixture.CreateContext();
        await using var second = fixture.CreateContext();
        var results = await Task.WhenAll(
            TryInsertAsync(first, account.UserId, CreateCursor(key, 5, BaseTime, "fp-first"), generation),
            TryInsertAsync(second, account.UserId, CreateCursor(key, 6, BaseTime, "fp-second"), generation));

        Assert.Single(results, succeeded => succeeded);
    }

    [Fact]
    public async Task Cursor_write_of_a_superseded_generation_is_rejected()
    {
        var account = await CreateSavedAccountAsync();
        var key = CreateKey(account, "ADAUSDT");

        await using var context = fixture.CreateContext();
        var stateStore = new RealtimeObservationStateStore(context);
        var cursorStore = new PositionObservationCursorStore(context);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => cursorStore.SaveAsync(
            account.UserId,
            CreateCursor(key, 1, BaseTime),
            null,
            expectedGeneration: 1));

        var firstGeneration = (await stateStore.AdvanceGenerationAsync(
            account.UserId,
            account.Id,
            RealtimeObservationTransportStatus.Initializing)).Value.Generation;
        var version = await cursorStore.SaveAsync(
            account.UserId,
            CreateCursor(key, 1, BaseTime),
            null,
            firstGeneration);

        var secondGeneration = (await stateStore.AdvanceGenerationAsync(
            account.UserId,
            account.Id,
            RealtimeObservationTransportStatus.Reconnecting)).Value.Generation;

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => cursorStore.SaveAsync(
            account.UserId,
            CreateCursor(key, 2, BaseTime.AddSeconds(1)),
            version,
            firstGeneration));
        Assert.Equal(
            version.Next(),
            await cursorStore.SaveAsync(
                account.UserId,
                CreateCursor(key, 2, BaseTime.AddSeconds(1)),
                version,
                secondGeneration));
    }

    [Fact]
    public async Task Generation_change_waits_for_an_open_cursor_transaction_of_the_current_generation()
    {
        var account = await CreateSavedAccountAsync();
        var generation = await StartObservationAsync(account);
        var key = CreateKey(account, "LTCUSDT");

        await using var observerContext = fixture.CreateContext();
        await using var lifecycleContext = fixture.CreateContext();
        await using (var transaction = await observerContext.Database.BeginTransactionAsync())
        {
            await new PositionObservationCursorStore(observerContext)
                .SaveAsync(account.UserId, CreateCursor(key, 1, BaseTime), null, generation);

            var advance = new RealtimeObservationStateStore(lifecycleContext).AdvanceGenerationAsync(
                account.UserId,
                account.Id,
                RealtimeObservationTransportStatus.Reconnecting);
            Assert.NotSame(advance, await Task.WhenAny(advance, Task.Delay(BlockedCommandProbe)));

            await transaction.CommitAsync();
            Assert.Equal(generation + 1, (await advance).Value.Generation);
        }

        await using var reader = fixture.CreateContext();
        Assert.NotNull(await new PositionObservationCursorStore(reader).GetAsync(account.UserId, key));
    }

    [Fact]
    public async Task Cursor_write_blocked_by_a_generation_change_is_rejected_after_it_commits()
    {
        var account = await CreateSavedAccountAsync();
        var generation = await StartObservationAsync(account);
        var key = CreateKey(account, "DOTUSDT");

        await using var observerContext = fixture.CreateContext();
        await using var lifecycleContext = fixture.CreateContext();
        await using (var transaction = await lifecycleContext.Database.BeginTransactionAsync())
        {
            await new RealtimeObservationStateStore(lifecycleContext).AdvanceGenerationAsync(
                account.UserId,
                account.Id,
                RealtimeObservationTransportStatus.Stopped);

            var write = new PositionObservationCursorStore(observerContext)
                .SaveAsync(account.UserId, CreateCursor(key, 1, BaseTime), null, generation);
            Assert.NotSame(write, await Task.WhenAny(write, Task.Delay(BlockedCommandProbe)));

            await transaction.CommitAsync();
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => write);
        }

        await using var reader = fixture.CreateContext();
        Assert.Null(await new PositionObservationCursorStore(reader).GetAsync(account.UserId, key));
    }

    [Fact]
    public async Task Cursor_and_state_writes_are_rolled_back_with_the_owning_transaction()
    {
        var account = await CreateSavedAccountAsync();
        var generation = await StartObservationAsync(account);
        var key = CreateKey(account, "AVAXUSDT");

        await using (var context = fixture.CreateContext())
        {
            var stateStore = new RealtimeObservationStateStore(context);
            var state = (await stateStore.GetAsync(account.UserId, account.Id))!;
            await using var transaction = await context.Database.BeginTransactionAsync();

            await new PositionObservationCursorStore(context)
                .SaveAsync(account.UserId, CreateCursor(key, 7, BaseTime), null, generation);
            await stateStore.SaveAsync(
                account.UserId,
                state.Value with { RecoveryRequired = false },
                state.Version);

            await transaction.RollbackAsync();
        }

        await using var reader = fixture.CreateContext();
        Assert.Null(await new PositionObservationCursorStore(reader).GetAsync(account.UserId, key));
        var reloaded = await new RealtimeObservationStateStore(reader).GetAsync(account.UserId, account.Id);
        Assert.True(reloaded!.Value.RecoveryRequired);
        Assert.Equal(ConcurrencyVersion.Initial, reloaded.Version);
    }

    [Fact]
    public async Task Cursors_are_isolated_by_user()
    {
        var account = await CreateSavedAccountAsync();
        var generation = await StartObservationAsync(account);
        var key = CreateKey(account, "LINKUSDT");
        var otherUser = UserId.New();

        await using var context = fixture.CreateContext();
        var store = new PositionObservationCursorStore(context);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => store.SaveAsync(
            otherUser,
            CreateCursor(key, 1, BaseTime),
            null,
            generation));
        var version = await store.SaveAsync(account.UserId, CreateCursor(key, 1, BaseTime), null, generation);

        Assert.Null(await store.GetAsync(otherUser, key));
        Assert.Empty(await store.GetByExchangeAccountAsync(otherUser, account.Id));
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => store.SaveAsync(
            otherUser,
            CreateCursor(key, 2, BaseTime.AddSeconds(1)),
            version,
            generation));
        Assert.Equal(1, (await store.GetAsync(account.UserId, key))!.Value.LastProviderSequence);
    }

    [Fact]
    public async Task Observation_state_and_cursors_are_deleted_with_the_exchange_account()
    {
        var account = await CreateSavedAccountAsync();
        var generation = await StartObservationAsync(account);

        await using var context = fixture.CreateContext();
        await new PositionObservationCursorStore(context).SaveAsync(
            account.UserId,
            CreateCursor(CreateKey(account, "BNBUSDT"), 1, BaseTime),
            null,
            generation);

        await context.ExchangeAccounts
            .Where(entity => entity.Id == account.Id.Value)
            .ExecuteDeleteAsync();

        Assert.False(await context.ExchangeRealtimeObservations
            .AnyAsync(observation => observation.ExchangeAccountId == account.Id.Value));
        Assert.False(await context.PositionObservationCursors
            .AnyAsync(cursor => cursor.ExchangeAccountId == account.Id.Value));
    }

    [Fact]
    public async Task Cursor_insert_racing_with_account_deletion_is_rejected_without_deadlock()
    {
        var account = await CreateSavedAccountAsync();
        await SaveCredentialAsync(account);
        var generation = await StartObservationAsync(account);
        var key = CreateKey(account, "BTCUSDT");

        await using var sessions = await AccountDeletionRace.StartAsync(fixture, account);
        var write = new PositionObservationCursorStore(sessions.ObserverContext)
            .SaveAsync(account.UserId, CreateCursor(key, 1, BaseTime), null, generation);
        await sessions.WaitUntilObserverIsBlockedByDeletionAsync(write);

        await sessions.ReleaseDeletionAsync();

        await sessions.Deletion;
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => write);
        await AssertAccountDataDeletedAsync(account);
    }

    [Fact]
    public async Task Cursor_update_racing_with_account_deletion_is_rejected_without_deadlock()
    {
        var account = await CreateSavedAccountAsync();
        await SaveCredentialAsync(account);
        var generation = await StartObservationAsync(account);
        var key = CreateKey(account, "ETHUSDT");
        ConcurrencyVersion version;
        await using (var context = fixture.CreateContext())
        {
            version = await new PositionObservationCursorStore(context)
                .SaveAsync(account.UserId, CreateCursor(key, 1, BaseTime), null, generation);
        }

        await using var sessions = await AccountDeletionRace.StartAsync(fixture, account);
        var write = new PositionObservationCursorStore(sessions.ObserverContext)
            .SaveAsync(account.UserId, CreateCursor(key, 2, BaseTime.AddSeconds(1)), version, generation);
        await sessions.WaitUntilObserverIsBlockedByDeletionAsync(write);

        await sessions.ReleaseDeletionAsync();

        await sessions.Deletion;
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => write);
        await AssertAccountDataDeletedAsync(account);
    }

    [Fact]
    public async Task First_generation_racing_with_account_deletion_is_rejected_as_a_conflict()
    {
        var account = await CreateSavedAccountAsync();
        await SaveCredentialAsync(account);

        await using var sessions = await AccountDeletionRace.StartAsync(fixture, account);
        var advance = new RealtimeObservationStateStore(sessions.ObserverContext).AdvanceGenerationAsync(
            account.UserId,
            account.Id,
            RealtimeObservationTransportStatus.Initializing);
        await sessions.WaitUntilObserverIsBlockedByDeletionAsync(advance);

        await sessions.ReleaseDeletionAsync();

        await sessions.Deletion;
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => advance);
        await AssertAccountDataDeletedAsync(account);
    }

    [Fact]
    public async Task Account_deletion_in_progress_does_not_delay_cursor_writes_of_another_account()
    {
        var deleted = await CreateSavedAccountAsync();
        await SaveCredentialAsync(deleted);
        await StartObservationAsync(deleted);
        var independent = await CreateSavedAccountAsync();
        var generation = await StartObservationAsync(independent);
        var key = CreateKey(independent, "SOLUSDT");

        await using var sessions = await AccountDeletionRace.StartAsync(fixture, deleted);
        var store = new PositionObservationCursorStore(sessions.ObserverContext);
        var version = await store
            .SaveAsync(independent.UserId, CreateCursor(key, 1, BaseTime), null, generation)
            .WaitAsync(LockWaitTimeout);
        await store
            .SaveAsync(independent.UserId, CreateCursor(key, 2, BaseTime.AddSeconds(1)), version, generation)
            .WaitAsync(LockWaitTimeout);
        await new RealtimeObservationStateStore(sessions.ObserverContext)
            .AdvanceGenerationAsync(independent.UserId, independent.Id, RealtimeObservationTransportStatus.Reconnecting)
            .WaitAsync(LockWaitTimeout);
        Assert.False(sessions.Deletion.IsCompleted);

        await sessions.ReleaseDeletionAsync();
        await sessions.Deletion;
        await AssertAccountDataDeletedAsync(deleted);
    }

    [Fact]
    public async Task Account_deletion_waits_for_an_open_cursor_transaction_and_cascades_after_it()
    {
        var account = await CreateSavedAccountAsync();
        var generation = await StartObservationAsync(account);
        var key = CreateKey(account, "TRXUSDT");

        await using var observerContext = fixture.CreateContext();
        await using var lifecycleContext = fixture.CreateContext();
        var observerPid = await OpenSessionAsync(observerContext);
        var lifecyclePid = await OpenSessionAsync(lifecycleContext);
        await using var monitor = await OpenConnectionAsync();
        var observerTransaction = await observerContext.Database.BeginTransactionAsync();
        Task? deletion = null;
        try
        {
            await new PositionObservationCursorStore(observerContext)
                .SaveAsync(account.UserId, CreateCursor(key, 1, BaseTime), null, generation);

            deletion = new ExchangeAccountRepository(lifecycleContext)
                .DeleteAsync(account.UserId, account.Id, ConcurrencyVersion.Initial);
            await WaitUntilBlockedAsync(monitor, deletion, lifecyclePid, observerPid);

            await observerTransaction.CommitAsync();
            await deletion.WaitAsync(LockWaitTimeout);
        }
        finally
        {
            await observerTransaction.DisposeAsync();
            await DrainAsync(deletion);
        }

        await AssertAccountDataDeletedAsync(account);
    }

    [Fact]
    public async Task Mark_price_upsert_applies_only_strictly_newer_observations()
    {
        var symbol = UniqueSymbol();
        var other = UniqueSymbol();

        await using var context = fixture.CreateContext();
        var store = new MarketMarkPriceStore(context);

        Assert.Equal(2, await store.UpsertAsync([CreatePrice(symbol, 100m, BaseTime), CreatePrice(other, 5m, BaseTime)]));
        Assert.Equal(0, await store.UpsertAsync([CreatePrice(symbol, 90m, BaseTime.AddSeconds(-1))]));
        Assert.Equal(0, await store.UpsertAsync([CreatePrice(symbol, 95m, BaseTime)]));
        Assert.Equal(1, await store.UpsertAsync([CreatePrice(symbol, 101.25m, BaseTime.AddSeconds(1))]));

        var prices = await store.GetAsync(ExchangeId.Bybit, MarketCategory.Linear, [symbol, other, UniqueSymbol()]);

        Assert.Equal(
            new[] { symbol.Value, other.Value }.Order(StringComparer.Ordinal),
            prices.Select(price => price.Symbol.Value).Order(StringComparer.Ordinal));
        var stored = prices.Single(price => price.Symbol == symbol);
        Assert.Equal(101.25m, stored.MarkPrice);
        Assert.Equal(BaseTime.AddSeconds(1), stored.ObservedAt);
        Assert.Equal(MarketMarkPriceSource.PublicTicker, stored.Source);
        Assert.Empty(await store.GetAsync(ExchangeId.Bybit, MarketCategory.Inverse, [symbol]));
        Assert.Empty(await store.GetAsync(ExchangeId.Bybit, MarketCategory.Linear, []));
    }

    [Fact]
    public async Task Mark_price_batch_keeps_the_newest_observation_of_each_instrument()
    {
        var symbol = UniqueSymbol();

        await using var context = fixture.CreateContext();
        var store = new MarketMarkPriceStore(context);

        var applied = await store.UpsertAsync(
        [
            CreatePrice(symbol, 10m, BaseTime),
            CreatePrice(symbol, 12m, BaseTime.AddSeconds(2)),
            CreatePrice(symbol, 11m, BaseTime.AddSeconds(1)),
        ]);

        Assert.Equal(1, applied);
        var stored = Assert.Single(await store.GetAsync(ExchangeId.Bybit, MarketCategory.Linear, [symbol]));
        Assert.Equal(12m, stored.MarkPrice);
        Assert.Equal(BaseTime.AddSeconds(2), stored.ObservedAt);
        Assert.Equal(0, await store.UpsertAsync([]));
    }

    [Fact]
    public async Task Mark_price_batch_keeps_categories_of_one_symbol_independent()
    {
        var symbol = UniqueSymbol();
        var other = UniqueSymbol();

        await using var context = fixture.CreateContext();
        var store = new MarketMarkPriceStore(context);

        Assert.Equal(3, await store.UpsertAsync(
        [
            CreatePrice(symbol, 10m, BaseTime),
            CreatePrice(symbol, 20m, BaseTime) with { Category = MarketCategory.Inverse },
            CreatePrice(other, 30m, BaseTime),
        ]));

        var linear = await store.GetAsync(ExchangeId.Bybit, MarketCategory.Linear, [symbol, other]);
        var inverse = Assert.Single(await store.GetAsync(ExchangeId.Bybit, MarketCategory.Inverse, [symbol, other]));
        Assert.Equal(10m, linear.Single(price => price.Symbol == symbol).MarkPrice);
        Assert.Equal(30m, linear.Single(price => price.Symbol == other).MarkPrice);
        Assert.Equal(symbol, inverse.Symbol);
        Assert.Equal(20m, inverse.MarkPrice);
    }

    [Fact]
    public async Task Overlapping_mark_price_batches_update_existing_rows_in_one_key_order()
    {
        var (first, middle, last) = OrderedSymbols();
        await using (var context = fixture.CreateContext())
        {
            await new MarketMarkPriceStore(context).UpsertAsync(
                [CreatePrice(first, 1m, BaseTime), CreatePrice(middle, 1m, BaseTime), CreatePrice(last, 1m, BaseTime)]);
        }

        await using var firstContext = fixture.CreateContext();
        await using var secondContext = fixture.CreateContext();
        var firstPid = await OpenSessionAsync(firstContext);
        var secondPid = await OpenSessionAsync(secondContext);
        await using var monitor = await OpenConnectionAsync();
        await using var blocker = await OpenConnectionAsync();
        var blockerTransaction = await blocker.BeginTransactionAsync();
        Task<int>? firstBatch = null;
        Task<int>? secondBatch = null;
        try
        {
            await ExecuteAsync(
                blocker,
                "SELECT 1 FROM market_mark_prices WHERE instrument_id = @symbol FOR UPDATE",
                blockerTransaction,
                ("symbol", middle.Value));

            firstBatch = new MarketMarkPriceStore(firstContext).UpsertAsync(
            [
                CreatePrice(first, 2m, BaseTime.AddSeconds(1)),
                CreatePrice(middle, 2m, BaseTime.AddSeconds(1)),
                CreatePrice(last, 2m, BaseTime.AddSeconds(1)),
            ]);
            await WaitUntilBlockedAsync(monitor, firstBatch, firstPid, blocker.ProcessID);

            // Пакет в обратном порядке ждёт первый ключ, уже заблокированный первым пакетом, и не
            // захватывает последний ключ, который понадобится первому пакету.
            secondBatch = new MarketMarkPriceStore(secondContext).UpsertAsync(
            [
                CreatePrice(last, 3m, BaseTime.AddSeconds(2)),
                CreatePrice(middle, 3m, BaseTime.AddSeconds(2)),
                CreatePrice(first, 3m, BaseTime.AddSeconds(2)),
            ]);
            await WaitUntilBlockedAsync(monitor, secondBatch, secondPid, firstPid);
            Assert.False(await IsMarkPriceLockedAsync(monitor, last));

            await blockerTransaction.CommitAsync();

            Assert.Equal(3, await firstBatch.WaitAsync(LockWaitTimeout));
            Assert.Equal(3, await secondBatch.WaitAsync(LockWaitTimeout));
        }
        finally
        {
            await blockerTransaction.DisposeAsync();
            await DrainAsync(firstBatch, secondBatch);
        }

        await AssertMarkPricesAsync([first, middle, last], 3m, BaseTime.AddSeconds(2));
    }

    [Fact]
    public async Task Concurrent_first_inserts_of_the_same_mark_price_keys_store_one_row_per_key()
    {
        var (first, middle, last) = OrderedSymbols();

        await using var firstContext = fixture.CreateContext();
        await using var secondContext = fixture.CreateContext();
        var firstPid = await OpenSessionAsync(firstContext);
        var secondPid = await OpenSessionAsync(secondContext);
        await using var monitor = await OpenConnectionAsync();
        await using var blocker = await OpenConnectionAsync();
        var blockerTransaction = await blocker.BeginTransactionAsync();
        Task<int>? firstBatch = null;
        Task<int>? secondBatch = null;
        try
        {
            await ExecuteAsync(
                blocker,
                """
                INSERT INTO market_mark_prices (
                    exchange_id, market_category, instrument_id, mark_price, observed_at, received_at, source)
                VALUES ('Bybit', 'Linear', @symbol, 1, @observed_at, @observed_at, 'PublicTicker')
                """,
                blockerTransaction,
                ("symbol", middle.Value),
                ("observed_at", BaseTime));

            firstBatch = new MarketMarkPriceStore(firstContext).UpsertAsync(
            [
                CreatePrice(first, 2m, BaseTime.AddSeconds(1)),
                CreatePrice(middle, 2m, BaseTime.AddSeconds(1)),
                CreatePrice(last, 2m, BaseTime.AddSeconds(1)),
            ]);
            await WaitUntilBlockedAsync(monitor, firstBatch, firstPid, blocker.ProcessID);

            secondBatch = new MarketMarkPriceStore(secondContext).UpsertAsync(
            [
                CreatePrice(last, 3m, BaseTime.AddSeconds(2)),
                CreatePrice(middle, 3m, BaseTime.AddSeconds(2)),
                CreatePrice(first, 3m, BaseTime.AddSeconds(2)),
            ]);
            await WaitUntilBlockedAsync(monitor, secondBatch, secondPid, firstPid);

            await blockerTransaction.CommitAsync();

            Assert.Equal(3, await firstBatch.WaitAsync(LockWaitTimeout));
            Assert.Equal(3, await secondBatch.WaitAsync(LockWaitTimeout));
        }
        finally
        {
            await blockerTransaction.DisposeAsync();
            await DrainAsync(firstBatch, secondBatch);
        }

        await AssertMarkPricesAsync([first, middle, last], 3m, BaseTime.AddSeconds(2));
    }

    [Fact]
    public async Task Older_mark_price_waiting_for_a_newer_concurrent_upsert_does_not_overwrite_it()
    {
        var symbol = UniqueSymbol();
        await using (var context = fixture.CreateContext())
        {
            await new MarketMarkPriceStore(context).UpsertAsync([CreatePrice(symbol, 1m, BaseTime)]);
        }

        await using var newerContext = fixture.CreateContext();
        await using var olderContext = fixture.CreateContext();
        var newerPid = await OpenSessionAsync(newerContext);
        var olderPid = await OpenSessionAsync(olderContext);
        await using var monitor = await OpenConnectionAsync();
        var newerTransaction = await newerContext.Database.BeginTransactionAsync();
        Task<int>? older = null;
        try
        {
            Assert.Equal(
                1,
                await new MarketMarkPriceStore(newerContext).UpsertAsync([CreatePrice(symbol, 3m, BaseTime.AddSeconds(2))]));

            older = new MarketMarkPriceStore(olderContext).UpsertAsync([CreatePrice(symbol, 2m, BaseTime.AddSeconds(1))]);
            await WaitUntilBlockedAsync(monitor, older, olderPid, newerPid);

            await newerTransaction.CommitAsync();

            Assert.Equal(0, await older.WaitAsync(LockWaitTimeout));
        }
        finally
        {
            await newerTransaction.DisposeAsync();
            await DrainAsync(older);
        }

        await AssertMarkPricesAsync([symbol], 3m, BaseTime.AddSeconds(2));
    }

    private async Task<ExchangeAccount> CreateSavedAccountAsync()
    {
        var account = ExchangeAccount.Create(
            ExchangeAccountId.New(),
            UserId.New(),
            ExchangeId.Bybit,
            ExchangeAccountProviderIdentity.From($"provider-{Guid.NewGuid():N}"),
            "Основной",
            ExchangeAccountConnectionStatus.Connected,
            ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions);
        await using var context = fixture.CreateContext();
        await new ExchangeAccountRepository(context).SaveAsync(account.UserId, account, expectedVersion: null);
        return account;
    }

    private async Task SaveCredentialAsync(ExchangeAccount account)
    {
        await using var context = fixture.CreateContext();
        context.ExchangeAccountCredentials.Add(new ExchangeAccountCredentialEntity
        {
            ExchangeAccountId = account.Id.Value,
            Ciphertext = [1],
            Nonce = new byte[12],
            AuthenticationTag = new byte[16],
            EncryptionKeyId = "v1",
            FormatVersion = 1,
            Version = 1,
            UpdatedAt = BaseTime,
        });
        await context.SaveChangesAsync();
    }

    private async Task AssertAccountDataDeletedAsync(ExchangeAccount account)
    {
        await using var context = fixture.CreateContext();
        Assert.False(await context.ExchangeAccounts.AnyAsync(entity => entity.Id == account.Id.Value));
        Assert.False(await context.ExchangeRealtimeObservations
            .AnyAsync(observation => observation.ExchangeAccountId == account.Id.Value));
        Assert.False(await context.PositionObservationCursors
            .AnyAsync(cursor => cursor.ExchangeAccountId == account.Id.Value));
    }

    private async Task<long> StartObservationAsync(ExchangeAccount account)
    {
        await using var context = fixture.CreateContext();
        var state = await new RealtimeObservationStateStore(context).AdvanceGenerationAsync(
            account.UserId,
            account.Id,
            RealtimeObservationTransportStatus.Initializing);
        return state.Value.Generation;
    }

    private static async Task<bool> TrySaveAsync(
        TradeSystemDbContext context,
        UserId userId,
        PositionObservationCursor cursor,
        long generation)
    {
        try
        {
            await new PositionObservationCursorStore(context)
                .SaveAsync(userId, cursor, ConcurrencyVersion.Initial, generation);
            return true;
        }
        catch (ConcurrencyConflictException)
        {
            return false;
        }
    }

    private static async Task<bool> TryInsertAsync(
        TradeSystemDbContext context,
        UserId userId,
        PositionObservationCursor cursor,
        long generation)
    {
        try
        {
            await new PositionObservationCursorStore(context).SaveAsync(userId, cursor, null, generation);
            return true;
        }
        catch (ConcurrencyConflictException)
        {
            return false;
        }
    }

    private static PositionObservationCursorKey CreateKey(ExchangeAccount account, string symbol) =>
        PositionObservationCursorKey.Create(account.Id, MarketCategory.Linear, InstrumentId.From(symbol), 0);

    private static PositionObservationCursor CreateCursor(
        PositionObservationCursorKey key,
        long? sequence,
        DateTimeOffset? updatedAt,
        string fingerprint = "fp-1") =>
        new()
        {
            Key = key,
            LastProviderSequence = sequence,
            LastProviderUpdatedAt = updatedAt,
            LastMessageCreatedAt = updatedAt,
            LastAcceptedFingerprint = fingerprint,
        };

    private static InstrumentId UniqueSymbol() => InstrumentId.From($"T{Guid.NewGuid():N}".ToUpperInvariant());

    private static MarketMarkPrice CreatePrice(InstrumentId symbol, decimal markPrice, DateTimeOffset observedAt) =>
        new()
        {
            Exchange = ExchangeId.Bybit,
            Category = MarketCategory.Linear,
            Symbol = symbol,
            MarkPrice = markPrice,
            ObservedAt = observedAt,
            ReceivedAt = observedAt.AddMilliseconds(150),
            Source = MarketMarkPriceSource.PublicTicker,
        };

    private static (InstrumentId First, InstrumentId Middle, InstrumentId Last) OrderedSymbols()
    {
        var symbols = new[] { UniqueSymbol(), UniqueSymbol(), UniqueSymbol() }
            .OrderBy(symbol => symbol.Value, StringComparer.Ordinal)
            .ToArray();
        return (symbols[0], symbols[1], symbols[2]);
    }

    private async Task AssertMarkPricesAsync(
        IReadOnlyCollection<InstrumentId> symbols,
        decimal markPrice,
        DateTimeOffset observedAt)
    {
        await using var context = fixture.CreateContext();
        var prices = await new MarketMarkPriceStore(context)
            .GetAsync(ExchangeId.Bybit, MarketCategory.Linear, symbols);

        Assert.Equal(symbols.Count, prices.Count);
        Assert.All(prices, price =>
        {
            Assert.Equal(markPrice, price.MarkPrice);
            Assert.Equal(observedAt, price.ObservedAt);
        });
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<int> OpenSessionAsync(TradeSystemDbContext context)
    {
        await context.Database.OpenConnectionAsync();
        return await context.Database.SqlQuery<int>($"SELECT pg_backend_pid() AS \"Value\"").SingleAsync();
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        string sql,
        NpgsqlTransaction? transaction,
        params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Ждёт, пока сессия <paramref name="waitingPid"/> начнёт ожидать блокировку сессии
    /// <paramref name="blockingPid"/>. Порядок подтверждается PostgreSQL, а не задержкой.
    /// </summary>
    private static async Task WaitUntilBlockedAsync(
        NpgsqlConnection monitor,
        Task operation,
        int waitingPid,
        int blockingPid)
    {
        var deadline = DateTimeOffset.UtcNow + LockWaitTimeout;
        while (true)
        {
            if (operation.IsCompleted)
            {
                await operation;
                Assert.Fail($"Сессия {waitingPid} завершила операцию, не ожидая сессию {blockingPid}.");
            }

            await using var command = new NpgsqlCommand(
                "SELECT @blocking = ANY(pg_blocking_pids(@waiting))",
                monitor);
            command.Parameters.AddWithValue("blocking", blockingPid);
            command.Parameters.AddWithValue("waiting", waitingPid);
            if ((bool)(await command.ExecuteScalarAsync())!)
            {
                return;
            }

            if (DateTimeOffset.UtcNow > deadline)
            {
                Assert.Fail($"Сессия {waitingPid} не начала ожидать сессию {blockingPid}.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }
    }

    private static async Task<bool> IsLockedAsync(
        NpgsqlConnection monitor,
        string lockingQuery,
        params (string Name, object Value)[] parameters)
    {
        try
        {
            await ExecuteAsync(monitor, lockingQuery + " NOWAIT", null, parameters);
            return false;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            return true;
        }
    }

    private static Task<bool> IsMarkPriceLockedAsync(NpgsqlConnection monitor, InstrumentId symbol) =>
        IsLockedAsync(
            monitor,
            "SELECT 1 FROM market_mark_prices WHERE instrument_id = @symbol FOR NO KEY UPDATE",
            ("symbol", symbol.Value));

    /// <summary>
    /// Дожидается завершения операций, оставшихся после неуспешной проверки, чтобы контекст не
    /// освобождался во время выполняющейся команды. Результат операций проверяет сам тест.
    /// </summary>
    private static async Task DrainAsync(params Task?[] operations)
    {
        foreach (var operation in operations)
        {
            if (operation is null)
            {
                continue;
            }

            try
            {
                await operation.WaitAsync(LockWaitTimeout);
            }
            catch (Exception)
            {
            }
        }
    }

    /// <summary>
    /// Останавливает удаление аккаунта после блокировки его строки, но до каскадного удаления
    /// состояния наблюдения: отдельная сессия держит строку credentials, каскад на которые
    /// срабатывает раньше. Это воспроизводит окно, в котором запись наблюдателя конкурирует
    /// с уже начатым удалением.
    /// </summary>
    private sealed class AccountDeletionRace : IAsyncDisposable
    {
        private readonly NpgsqlConnection monitor;
        private readonly NpgsqlConnection blocker;
        private readonly NpgsqlTransaction blockerTransaction;
        private readonly TradeSystemDbContext lifecycleContext;
        private readonly int lifecyclePid;
        private readonly int observerPid;
        private Task? observerOperation;
        private bool released;

        private AccountDeletionRace(
            NpgsqlConnection monitor,
            NpgsqlConnection blocker,
            NpgsqlTransaction blockerTransaction,
            TradeSystemDbContext lifecycleContext,
            int lifecyclePid,
            TradeSystemDbContext observerContext,
            int observerPid,
            Task deletion)
        {
            this.monitor = monitor;
            this.blocker = blocker;
            this.blockerTransaction = blockerTransaction;
            this.lifecycleContext = lifecycleContext;
            this.lifecyclePid = lifecyclePid;
            ObserverContext = observerContext;
            this.observerPid = observerPid;
            Deletion = deletion;
        }

        public TradeSystemDbContext ObserverContext { get; }

        public Task Deletion { get; }

        public static async Task<AccountDeletionRace> StartAsync(PostgreSqlFixture fixture, ExchangeAccount account)
        {
            var monitor = new NpgsqlConnection(fixture.ConnectionString);
            await monitor.OpenAsync();
            var blocker = new NpgsqlConnection(fixture.ConnectionString);
            await blocker.OpenAsync();
            var blockerPid = blocker.ProcessID;
            var blockerTransaction = await blocker.BeginTransactionAsync();
            await ExecuteAsync(
                blocker,
                "SELECT 1 FROM exchange_account_credentials WHERE exchange_account_id = @id FOR SHARE",
                blockerTransaction,
                ("id", account.Id.Value));

            var lifecycleContext = fixture.CreateContext();
            var lifecyclePid = await OpenSessionAsync(lifecycleContext);
            var observerContext = fixture.CreateContext();
            var observerPid = await OpenSessionAsync(observerContext);
            var deletion = new ExchangeAccountRepository(lifecycleContext)
                .DeleteAsync(account.UserId, account.Id, ConcurrencyVersion.Initial);

            var race = new AccountDeletionRace(
                monitor,
                blocker,
                blockerTransaction,
                lifecycleContext,
                lifecyclePid,
                observerContext,
                observerPid,
                deletion);
            await WaitUntilBlockedAsync(monitor, deletion, lifecyclePid, blockerPid);

            Assert.True(
                await IsLockedAsync(
                    monitor,
                    "SELECT 1 FROM exchange_accounts WHERE exchange_account_id = @id FOR KEY SHARE",
                    ("id", account.Id.Value)),
                "Удаление должно удерживать строку аккаунта.");
            Assert.False(
                await IsLockedAsync(
                    monitor,
                    "SELECT 1 FROM exchange_realtime_observations WHERE exchange_account_id = @id FOR KEY SHARE",
                    ("id", account.Id.Value)),
                "Каскад удаления не должен дойти до состояния наблюдения.");
            return race;
        }

        public Task WaitUntilObserverIsBlockedByDeletionAsync(Task operation)
        {
            observerOperation = operation;
            return WaitUntilBlockedAsync(monitor, operation, observerPid, lifecyclePid);
        }

        public async Task ReleaseDeletionAsync()
        {
            await blockerTransaction.CommitAsync();
            released = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (!released)
            {
                await blockerTransaction.RollbackAsync();
            }

            await DrainAsync(Deletion, observerOperation);
            await blockerTransaction.DisposeAsync();
            await blocker.DisposeAsync();
            await monitor.DisposeAsync();
            await lifecycleContext.DisposeAsync();
            await ObserverContext.DisposeAsync();
        }
    }
}
