using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Realtime.State;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Infrastructure.Persistence;
using Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests;

[Collection("PostgreSql-B")]
public sealed class RealtimeObservationPersistencePostgreSqlTests(PostgreSqlFixture fixture)
{
    private static readonly TimeSpan BlockedCommandProbe = TimeSpan.FromMilliseconds(500);
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
}
