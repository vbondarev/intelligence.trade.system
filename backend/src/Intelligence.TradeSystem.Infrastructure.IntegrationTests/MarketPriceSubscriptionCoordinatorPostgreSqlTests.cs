using System.Collections.Concurrent;
using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Application.Realtime.Observations;
using Intelligence.TradeSystem.Application.Realtime.State;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Snapshots;
using Intelligence.TradeSystem.Infrastructure.Persistence;
using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;
using Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;
using Intelligence.TradeSystem.Infrastructure.Realtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests;

[Collection("PostgreSql-A")]
public sealed class MarketPriceSubscriptionCoordinatorPostgreSqlTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Shares_symbol_subscription_bounds_price_persistence_and_recovers_failed_stream()
    {
        var symbol = $"T{Guid.NewGuid():N}".ToUpperInvariant();
        await CreatePositionAsync(symbol, "USDT", ExchangeAccountConnectionStatus.Connected);
        await CreatePositionAsync(symbol, "USDC", ExchangeAccountConnectionStatus.Unavailable);
        await CreatePositionAsync(
            $"I{Guid.NewGuid():N}".ToUpperInvariant(),
            "USDT",
            ExchangeAccountConnectionStatus.Connected,
            MarketCategory.Inverse);

        await using (var context = fixture.CreateContext())
        {
            var supportedSymbols = await context.Positions
                .Where(position =>
                    position.MarketCategory == MarketCategory.Linear
                    && position.TrackingState != PositionTrackingState.Closed
                    && position.Size > 0m
                    && (position.SettlementAsset == "USDT" || position.SettlementAsset == "USDC")
                    && context.ExchangeAccounts.Any(account =>
                        account.Id == position.ExchangeAccountId
                        && account.ExchangeId == ExchangeId.Bybit
                        && (account.ConnectionStatus == ExchangeAccountConnectionStatus.Connected
                            || account.ConnectionStatus == ExchangeAccountConnectionStatus.Unavailable)))
                .Select(position => position.InstrumentId)
                .Distinct()
                .ToArrayAsync();
            Assert.Contains(symbol, supportedSymbols);
        }

        var store = new RecordingMarketMarkPriceStore();
        var provider = new RecordingMarketMarkPriceSubscriptionProvider();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<TradeSystemDbContext>(options =>
            options.UseNpgsql(
                fixture.ConnectionString,
                npgsqlOptions => npgsqlOptions.MigrationsAssembly(
                    typeof(TradeSystemDbContext).Assembly.GetName().Name)));
        services.AddSingleton<IMarketMarkPriceStore>(store);
        await using var serviceProvider = services.BuildServiceProvider();
        var coordinator = new MarketPriceSubscriptionCoordinator(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            provider,
            TimeProvider.System,
            serviceProvider.GetRequiredService<ILogger<MarketPriceSubscriptionCoordinator>>());
        using var cancellation = new CancellationTokenSource();

        await coordinator.StartAsync(cancellation.Token);
        try
        {
            await WaitUntilAsync(() => provider.SubscribeCountFor(symbol) == 1);
            var observedAt = DateTimeOffset.UtcNow;
            for (var index = 0; index < 300; index++)
            {
                provider.Emit(
                    symbol,
                    CreateObservation(symbol, 100m + index, observedAt.AddMilliseconds(index)));
            }

            provider.Emit(
                symbol,
                CreateObservation(symbol, 99m, observedAt.AddMilliseconds(10)));
            provider.Emit(
                symbol,
                CreateObservation(
                    symbol,
                    markPrice: null,
                    observedAt.AddSeconds(1),
                    MarketPriceUpdateKind.Delta));

            await WaitUntilAsync(() => store.LatestPrice(symbol) is not null);
            await Task.Delay(TimeSpan.FromMilliseconds(700));

            Assert.Equal(1, provider.SubscribeCountFor(symbol));
            Assert.Equal(399m, store.LatestPrice(symbol)!.MarkPrice);
            Assert.Equal(observedAt.AddMilliseconds(299), store.LatestPrice(symbol)!.ObservedAt);
            Assert.InRange(store.UpsertBatchCount, 1, 2);

            provider.Fail(symbol);
            await WaitUntilAsync(
                () => provider.SubscribeCountFor(symbol) >= 2,
                TimeSpan.FromSeconds(12));
            Assert.Equal(1, provider.ActiveSubscriptionCountFor(symbol));

            provider.Emit(
                symbol,
                CreateObservation(symbol, 401m, DateTimeOffset.UtcNow.AddMilliseconds(2)));
            await WaitUntilAsync(() => store.LatestPrice(symbol)?.MarkPrice == 401m);
        }
        finally
        {
            await coordinator.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Persists_monotonic_market_prices_through_the_postgresql_store()
    {
        var symbol = $"Q{Guid.NewGuid():N}USDT".ToUpperInvariant();
        await CreatePositionAsync(symbol, "USDT", ExchangeAccountConnectionStatus.Connected);

        var provider = new ControlledMarketMarkPriceSubscriptionProvider();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<TradeSystemDbContext>(options =>
            options.UseNpgsql(
                fixture.ConnectionString,
                npgsqlOptions => npgsqlOptions.MigrationsAssembly(
                    typeof(TradeSystemDbContext).Assembly.GetName().Name)));
        services.AddScoped<IMarketMarkPriceStore, MarketMarkPriceStore>();
        await using var serviceProvider = services.BuildServiceProvider();
        var coordinator = CreateCoordinator(serviceProvider, provider);
        DateTimeOffset expectedObservedAt = default;

        await coordinator.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(() => provider.SubscribeCountFor(symbol) == 1);
            var attempt = provider.AttemptFor(symbol, attemptNumber: 1);
            attempt.Complete();

            var t10 = DateTimeOffset.FromUnixTimeMilliseconds(
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            attempt.Emit(CreateObservation(symbol, 100m, t10));
            var first = await WaitForStoredPriceAsync(
                serviceProvider,
                symbol,
                price => price?.MarkPrice == 100m);
            Assert.Equal(t10, first.ObservedAt);

            attempt.Emit(CreateObservation(symbol, 90m, t10.AddSeconds(-1)));
            await Task.Delay(TimeSpan.FromMilliseconds(350));
            var afterOlderObservation = await ReadMarketPriceAsync(serviceProvider, symbol);
            Assert.Equal(100m, afterOlderObservation!.MarkPrice);
            Assert.Equal(t10, afterOlderObservation.ObservedAt);

            await Task.Delay(TimeSpan.FromMilliseconds(2_100));
            expectedObservedAt = DateTimeOffset.FromUnixTimeMilliseconds(
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            attempt.Emit(CreateObservation(symbol, 110m, expectedObservedAt));
            var updated = await WaitForStoredPriceAsync(
                serviceProvider,
                symbol,
                price => price?.MarkPrice == 110m);

            Assert.Equal(expectedObservedAt, updated.ObservedAt);
            Assert.Equal(ExchangeId.Bybit, updated.Exchange);
            Assert.Equal(MarketCategory.Linear, updated.Category);
            Assert.Equal(InstrumentId.From(symbol), updated.Symbol);
            Assert.Equal(MarketMarkPriceSource.PublicTicker, updated.Source);
        }
        finally
        {
            await coordinator.StopAsync(CancellationToken.None);
        }

        var persisted = await ReadMarketPriceAsync(serviceProvider, symbol);
        Assert.NotNull(persisted);
        Assert.Equal(110m, persisted.MarkPrice);
        Assert.Equal(ExchangeId.Bybit, persisted.Exchange);
        Assert.Equal(MarketCategory.Linear, persisted.Category);
        Assert.Equal(InstrumentId.From(symbol), persisted.Symbol);
        Assert.Equal(MarketMarkPriceSource.PublicTicker, persisted.Source);
        Assert.Equal(expectedObservedAt, persisted.ObservedAt);

        await using var verificationContext = fixture.CreateContext();
        var positionSettlement = await verificationContext.Positions
            .Where(position => position.InstrumentId == symbol)
            .Select(position => position.SettlementAsset)
            .SingleAsync();
        Assert.Equal("USDT", positionSettlement);
    }

    [Fact]
    public async Task Rejects_observations_from_other_symbols_and_old_generations()
    {
        var btcSymbol = $"B{Guid.NewGuid():N}".ToUpperInvariant();
        var ethSymbol = $"E{Guid.NewGuid():N}".ToUpperInvariant();
        var unknownSymbol = $"U{Guid.NewGuid():N}".ToUpperInvariant();
        await CreatePositionAsync(btcSymbol, "USDT", ExchangeAccountConnectionStatus.Connected);
        await CreatePositionAsync(ethSymbol, "USDT", ExchangeAccountConnectionStatus.Connected);

        var store = new RecordingMarketMarkPriceStore();
        var provider = new RecordingMarketMarkPriceSubscriptionProvider();
        var services = CreateServices(store);
        await using var serviceProvider = services.BuildServiceProvider();
        var coordinator = CreateCoordinator(serviceProvider, provider);

        await coordinator.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(
                () => provider.SubscribeCountFor(btcSymbol) == 1
                    && provider.SubscribeCountFor(ethSymbol) == 1);

            var observedAt = DateTimeOffset.UtcNow;
            provider.Emit(btcSymbol, CreateObservation(ethSymbol, 90m, observedAt));
            provider.Emit(btcSymbol, CreateObservation(unknownSymbol, 80m, observedAt.AddMilliseconds(1)));
            provider.Emit(btcSymbol, CreateObservation(string.Empty, 70m, observedAt.AddMilliseconds(2)));
            await Task.Delay(TimeSpan.FromMilliseconds(350));

            Assert.Null(store.LatestPrice(ethSymbol));
            Assert.Null(store.LatestPrice(unknownSymbol));
            Assert.Null(store.LatestPrice(string.Empty));

            var acceptedAt = observedAt.AddSeconds(1);
            provider.Emit(ethSymbol, CreateObservation(ethSymbol, 100m, acceptedAt));
            await WaitUntilAsync(() => store.LatestPrice(ethSymbol)?.MarkPrice == 100m);
            Assert.Equal(acceptedAt, store.LatestPrice(ethSymbol)!.ObservedAt);

            provider.Fail(btcSymbol);
            await WaitUntilAsync(
                () => provider.SubscribeCountFor(btcSymbol) == 2,
                TimeSpan.FromSeconds(12));

            provider.EmitFromAttempt(
                btcSymbol,
                attemptNumber: 1,
                CreateObservation(btcSymbol, 500m, acceptedAt.AddSeconds(1)));
            await Task.Delay(TimeSpan.FromMilliseconds(350));
            Assert.Null(store.LatestPrice(btcSymbol));

            var newGenerationAt = acceptedAt.AddSeconds(2);
            provider.Emit(btcSymbol, CreateObservation(btcSymbol, 600m, newGenerationAt));
            await WaitUntilAsync(() => store.LatestPrice(btcSymbol)?.MarkPrice == 600m);
            Assert.Equal(newGenerationAt, store.LatestPrice(btcSymbol)!.ObservedAt);
        }
        finally
        {
            await coordinator.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Pending_subscription_for_one_symbol_does_not_block_persistence_for_another()
    {
        var btcSymbol = $"B{Guid.NewGuid():N}".ToUpperInvariant();
        var ethSymbol = $"E{Guid.NewGuid():N}".ToUpperInvariant();
        await CreatePositionAsync(btcSymbol, "USDT", ExchangeAccountConnectionStatus.Connected);
        await CreatePositionAsync(ethSymbol, "USDT", ExchangeAccountConnectionStatus.Connected);

        var store = new RecordingMarketMarkPriceStore();
        var provider = new ControlledMarketMarkPriceSubscriptionProvider();
        await using var serviceProvider = CreateServices(store).BuildServiceProvider();
        var coordinator = CreateCoordinator(serviceProvider, provider);

        await coordinator.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(
                () => provider.SubscribeCountFor(btcSymbol) == 1
                    && provider.SubscribeCountFor(ethSymbol) == 1);
            var btcAttempt = provider.AttemptFor(btcSymbol, attemptNumber: 1);
            var ethAttempt = provider.AttemptFor(ethSymbol, attemptNumber: 1);
            btcAttempt.Complete();

            var observedAt = DateTimeOffset.UtcNow;
            btcAttempt.Emit(CreateObservation(btcSymbol, 100m, observedAt));
            await WaitUntilAsync(() => store.LatestPrice(btcSymbol)?.MarkPrice == 100m);

            Assert.False(ethAttempt.Completion.Task.IsCompleted);
            await Task.Delay(TimeSpan.FromMilliseconds(5_300));
            Assert.Equal(1, provider.SubscribeCountFor(ethSymbol));
        }
        finally
        {
            await coordinator.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Timed_out_subscription_remains_tracked_and_disposes_its_late_lease()
    {
        var symbol = $"T{Guid.NewGuid():N}".ToUpperInvariant();
        await CreatePositionAsync(symbol, "USDT", ExchangeAccountConnectionStatus.Connected);

        var store = new RecordingMarketMarkPriceStore();
        var provider = new ControlledMarketMarkPriceSubscriptionProvider();
        await using var serviceProvider = CreateServices(store).BuildServiceProvider();
        var coordinator = CreateCoordinator(serviceProvider, provider);

        await coordinator.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(() => provider.SubscribeCountFor(symbol) == 1);
            var attempt = provider.AttemptFor(symbol, attemptNumber: 1);
            await WaitUntilAsync(
                () => attempt.CancellationToken.IsCancellationRequested,
                TimeSpan.FromSeconds(12));

            Assert.Equal(1, provider.SubscribeCountFor(symbol));
            var lease = attempt.Complete();
            await WaitUntilAsync(() => lease.IsDisposed);
            Assert.Equal(0, provider.ActiveLeaseCountFor(symbol));

            attempt.Emit(CreateObservation(symbol, 100m, DateTimeOffset.UtcNow));
            await Task.Delay(TimeSpan.FromMilliseconds(350));
            Assert.Null(store.LatestPrice(symbol));
        }
        finally
        {
            await coordinator.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Does_not_retry_while_the_previous_lease_is_stopping()
    {
        var symbol = $"S{Guid.NewGuid():N}".ToUpperInvariant();
        await CreatePositionAsync(symbol, "USDT", ExchangeAccountConnectionStatus.Connected);

        var store = new RecordingMarketMarkPriceStore();
        var provider = new ControlledMarketMarkPriceSubscriptionProvider();
        await using var serviceProvider = CreateServices(store).BuildServiceProvider();
        var coordinator = CreateCoordinator(serviceProvider, provider);
        var releaseDisposal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await coordinator.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(() => provider.SubscribeCountFor(symbol) == 1);
            var firstAttempt = provider.AttemptFor(symbol, attemptNumber: 1);
            var oldLease = firstAttempt.Complete(releaseDisposal.Task);
            await WaitUntilAsync(() => provider.ActiveLeaseCountFor(symbol) == 1);

            firstAttempt.FailStream();
            await WaitUntilAsync(() => oldLease.DisposeRequested.Task.IsCompleted);
            await Task.Delay(TimeSpan.FromMilliseconds(5_300));

            Assert.Equal(1, provider.SubscribeCountFor(symbol));
            Assert.Equal(1, provider.ActiveLeaseCountFor(symbol));

            releaseDisposal.TrySetResult();
            await WaitUntilAsync(
                () => oldLease.IsDisposed && provider.SubscribeCountFor(symbol) == 2,
                TimeSpan.FromSeconds(8));

            Assert.Equal(0, provider.ActiveLeaseCountFor(symbol));
            provider.AttemptFor(symbol, attemptNumber: 2)
                .Emit(CreateObservation(symbol, 150m, DateTimeOffset.UtcNow));
            await Task.Delay(TimeSpan.FromMilliseconds(350));
            Assert.Null(store.LatestPrice(symbol));
        }
        finally
        {
            await coordinator.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Removing_a_symbol_cancels_its_start_and_fences_late_callbacks()
    {
        var symbol = $"D{Guid.NewGuid():N}".ToUpperInvariant();
        await CreatePositionAsync(symbol, "USDT", ExchangeAccountConnectionStatus.Connected);

        var store = new RecordingMarketMarkPriceStore();
        var provider = new ControlledMarketMarkPriceSubscriptionProvider();
        await using var serviceProvider = CreateServices(store).BuildServiceProvider();
        var coordinator = CreateCoordinator(serviceProvider, provider);
        var releaseDisposal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await coordinator.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(() => provider.SubscribeCountFor(symbol) == 1);
            var firstAttempt = provider.AttemptFor(symbol, attemptNumber: 1);
            await DeletePositionsAsync(symbol);
            await WaitUntilAsync(
                () => firstAttempt.CancellationToken.IsCancellationRequested,
                TimeSpan.FromSeconds(8));

            var oldLease = firstAttempt.Complete(releaseDisposal.Task);
            await WaitUntilAsync(() => oldLease.DisposeRequested.Task.IsCompleted);
            await CreatePositionAsync(symbol, "USDT", ExchangeAccountConnectionStatus.Connected);
            await Task.Delay(TimeSpan.FromMilliseconds(5_300));

            Assert.Equal(1, provider.SubscribeCountFor(symbol));
            Assert.Equal(1, provider.ActiveLeaseCountFor(symbol));

            firstAttempt.Emit(CreateObservation(symbol, 90m, DateTimeOffset.UtcNow));
            await Task.Delay(TimeSpan.FromMilliseconds(350));
            Assert.Null(store.LatestPrice(symbol));

            releaseDisposal.TrySetResult();
            await WaitUntilAsync(
                () => oldLease.IsDisposed && provider.SubscribeCountFor(symbol) == 2,
                TimeSpan.FromSeconds(8));
            Assert.Equal(0, provider.ActiveLeaseCountFor(symbol));

            var retry = provider.AttemptFor(symbol, attemptNumber: 2);
            retry.Complete();
            retry.Emit(CreateObservation(symbol, 100m, DateTimeOffset.UtcNow));
            await WaitUntilAsync(() => store.LatestPrice(symbol)?.MarkPrice == 100m);
        }
        finally
        {
            await coordinator.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Failed_subscription_is_retried_and_persistence_resumes()
    {
        var symbol = $"R{Guid.NewGuid():N}".ToUpperInvariant();
        await CreatePositionAsync(symbol, "USDT", ExchangeAccountConnectionStatus.Connected);

        var store = new RecordingMarketMarkPriceStore();
        var provider = new ControlledMarketMarkPriceSubscriptionProvider();
        await using var serviceProvider = CreateServices(store).BuildServiceProvider();
        var coordinator = CreateCoordinator(serviceProvider, provider);

        await coordinator.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(() => provider.SubscribeCountFor(symbol) == 1);
            provider.AttemptFor(symbol, attemptNumber: 1)
                .Fail(new InvalidOperationException("test subscription failure"));

            await WaitUntilAsync(
                () => provider.SubscribeCountFor(symbol) == 2,
                TimeSpan.FromSeconds(12));
            var retry = provider.AttemptFor(symbol, attemptNumber: 2);
            retry.Complete();
            var observedAt = DateTimeOffset.UtcNow;
            retry.Emit(CreateObservation(symbol, 120m, observedAt));

            await WaitUntilAsync(() => store.LatestPrice(symbol)?.MarkPrice == 120m);
            Assert.Equal(observedAt, store.LatestPrice(symbol)!.ObservedAt);
            Assert.Equal(1, provider.ActiveLeaseCountFor(symbol));
        }
        finally
        {
            await coordinator.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Shutdown_cancels_pending_attempts_and_disposes_late_leases()
    {
        var activeSymbol = $"A{Guid.NewGuid():N}".ToUpperInvariant();
        var pendingSymbol = $"P{Guid.NewGuid():N}".ToUpperInvariant();
        await CreatePositionAsync(activeSymbol, "USDT", ExchangeAccountConnectionStatus.Connected);
        await CreatePositionAsync(pendingSymbol, "USDT", ExchangeAccountConnectionStatus.Connected);

        var store = new RecordingMarketMarkPriceStore();
        var provider = new ControlledMarketMarkPriceSubscriptionProvider();
        await using var serviceProvider = CreateServices(store).BuildServiceProvider();
        var coordinator = CreateCoordinator(serviceProvider, provider);

        await coordinator.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(
                () => provider.SubscribeCountFor(activeSymbol) == 1
                    && provider.SubscribeCountFor(pendingSymbol) == 1);
            var activeAttempt = provider.AttemptFor(activeSymbol, attemptNumber: 1);
            var pendingAttempt = provider.AttemptFor(pendingSymbol, attemptNumber: 1);
            var activeLease = activeAttempt.Complete();
            await WaitUntilAsync(() => provider.ActiveLeaseCountFor(activeSymbol) == 1);
            await coordinator.StopAsync(CancellationToken.None);

            Assert.True(pendingAttempt.CancellationToken.IsCancellationRequested);
            Assert.True(activeLease.IsDisposed);

            var lateLease = pendingAttempt.Complete();
            await WaitUntilAsync(() => lateLease.IsDisposed);
            Assert.Equal(0, provider.ActiveLeaseCountFor(pendingSymbol));

            pendingAttempt.Emit(CreateObservation(
                pendingSymbol,
                100m,
                DateTimeOffset.UtcNow));
            await Task.Delay(TimeSpan.FromMilliseconds(350));
            Assert.Null(store.LatestPrice(pendingSymbol));
            Assert.Equal(1, provider.SubscribeCountFor(pendingSymbol));
        }
        finally
        {
            await coordinator.StopAsync(CancellationToken.None);
        }
    }

    private async Task CreatePositionAsync(
        string symbol,
        string settlementAsset,
        ExchangeAccountConnectionStatus connectionStatus,
        MarketCategory category = MarketCategory.Linear)
    {
        var accountId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using var context = fixture.CreateContext();
        context.ExchangeAccounts.Add(new ExchangeAccountEntity
        {
            Id = accountId,
            UserId = Guid.NewGuid(),
            ExchangeId = ExchangeId.Bybit,
            ProviderAccountId = Guid.NewGuid().ToString("N"),
            DisplayName = "Рыночная оценка",
            ConnectionStatus = connectionStatus,
            Capabilities = ExchangeAccountCapabilities.ReadPositions,
            Version = 1,
        });
        context.Positions.Add(new PositionEntity
        {
            Id = Guid.NewGuid(),
            ExchangeAccountId = accountId,
            InstrumentId = symbol,
            PositionSide = PositionSide.Long,
            PositionIdx = 0,
            MarketCategory = category,
            SettlementAsset = settlementAsset,
            Size = 1m,
            FirstDetectedAt = now,
            LastObservedAt = now,
            TrackingState = PositionTrackingState.Active,
            Version = 1,
        });
        await context.SaveChangesAsync();
    }

    private async Task DeletePositionsAsync(string symbol)
    {
        await using var context = fixture.CreateContext();
        var positions = await context.Positions
            .Where(position => position.InstrumentId == symbol)
            .ToArrayAsync();
        context.Positions.RemoveRange(positions);
        await context.SaveChangesAsync();
    }

    private ServiceCollection CreateServices(IMarketMarkPriceStore store)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<TradeSystemDbContext>(options =>
            options.UseNpgsql(
                fixture.ConnectionString,
                npgsqlOptions => npgsqlOptions.MigrationsAssembly(
                    typeof(TradeSystemDbContext).Assembly.GetName().Name)));
        services.AddSingleton<IMarketMarkPriceStore>(store);
        return services;
    }

    private static MarketPriceSubscriptionCoordinator CreateCoordinator(
        ServiceProvider serviceProvider,
        IMarketMarkPriceSubscriptionProvider provider) =>
        new(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            provider,
            TimeProvider.System,
            serviceProvider.GetRequiredService<ILogger<MarketPriceSubscriptionCoordinator>>());

    private static MarketMarkPriceObservation CreateObservation(
        string symbol,
        decimal? markPrice,
        DateTimeOffset observedAt,
        MarketPriceUpdateKind updateKind = MarketPriceUpdateKind.Snapshot) =>
        new()
        {
            Exchange = ExchangeId.Bybit,
            Category = MarketCategory.Linear,
            Symbol = symbol,
            MarkPrice = markPrice,
            UpdateKind = updateKind,
            ObservedAt = observedAt,
            ReceivedAt = observedAt,
        };

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("Ожидаемое состояние рыночной подписки не наступило.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }
    }

    private static async Task<MarketMarkPrice> WaitForStoredPriceAsync(
        IServiceProvider serviceProvider,
        string symbol,
        Func<MarketMarkPrice?, bool> predicate,
        TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        MarketMarkPrice? price = null;
        while (DateTime.UtcNow < deadline)
        {
            price = await ReadMarketPriceAsync(serviceProvider, symbol);
            if (predicate(price))
            {
                return price!;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }

        throw new TimeoutException(
            $"Рыночная цена для {symbol} не достигла ожидаемого состояния. Последнее значение: {price?.MarkPrice}.");
    }

    private static async Task<MarketMarkPrice?> ReadMarketPriceAsync(
        IServiceProvider serviceProvider,
        string symbol)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IMarketMarkPriceStore>();
        var prices = await store.GetAsync(
            ExchangeId.Bybit,
            MarketCategory.Linear,
            [InstrumentId.From(symbol)]);
        return prices.SingleOrDefault();
    }

    private sealed class RecordingMarketMarkPriceStore : IMarketMarkPriceStore
    {
        private readonly ConcurrentDictionary<string, MarketMarkPrice> _prices =
            new(StringComparer.Ordinal);
        private int _upsertBatchCount;

        public int UpsertBatchCount => Volatile.Read(ref _upsertBatchCount);

        public MarketMarkPrice? LatestPrice(string symbol) =>
            _prices.TryGetValue(symbol, out var price) ? price : null;

        public Task<IReadOnlyCollection<MarketMarkPrice>> GetAsync(
            ExchangeId exchange,
            MarketCategory category,
            IReadOnlyCollection<InstrumentId> symbols,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyCollection<MarketMarkPrice> result = symbols
                .Where(symbol => _prices.TryGetValue(symbol.Value, out _))
                .Select(symbol => _prices[symbol.Value])
                .Where(price => price.Exchange == exchange && price.Category == category)
                .ToArray();
            return Task.FromResult(result);
        }

        public Task<int> UpsertAsync(
            IReadOnlyCollection<MarketMarkPrice> prices,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _upsertBatchCount);
            var applied = 0;
            foreach (var price in prices)
            {
                if (_prices.TryGetValue(price.Symbol.Value, out var current)
                    && current.ObservedAt >= price.ObservedAt)
                {
                    continue;
                }

                _prices[price.Symbol.Value] = price;
                applied++;
            }

            return Task.FromResult(applied);
        }
    }

    private sealed class RecordingMarketMarkPriceSubscriptionProvider
        : IMarketMarkPriceSubscriptionProvider
    {
        private readonly ConcurrentDictionary<string, Subscription> _active =
            new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, int> _subscribeCounts =
            new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, ConcurrentQueue<Subscription>> _attempts =
            new(StringComparer.Ordinal);

        public int SubscribeCountFor(string symbol) =>
            _subscribeCounts.TryGetValue(symbol, out var count) ? count : 0;

        public int ActiveSubscriptionCountFor(string symbol) => _active.ContainsKey(symbol) ? 1 : 0;

        public Task<IAsyncDisposable> SubscribeAsync(
            InstrumentId symbol,
            Action<MarketMarkPriceObservation> onObservation,
            Action onFailure,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var subscription = new Subscription(
                symbol.Value,
                onObservation,
                onFailure,
                _active);
            _active[symbol.Value] = subscription;
            _attempts.GetOrAdd(symbol.Value, static _ => new()).Enqueue(subscription);
            _subscribeCounts.AddOrUpdate(symbol.Value, 1, static (_, count) => count + 1);
            return Task.FromResult<IAsyncDisposable>(subscription);
        }

        public void Emit(string symbol, MarketMarkPriceObservation observation)
        {
            if (_active.TryGetValue(symbol, out var subscription))
            {
                subscription.Emit(observation);
            }
        }

        public void EmitFromAttempt(
            string symbol,
            int attemptNumber,
            MarketMarkPriceObservation observation)
        {
            if (_attempts.TryGetValue(symbol, out var attempts)
                && attempts.ElementAtOrDefault(attemptNumber - 1) is { } subscription)
            {
                subscription.EmitEvenIfDisposed(observation);
            }
        }

        public void Fail(string symbol)
        {
            if (_active.TryGetValue(symbol, out var subscription))
            {
                subscription.Fail();
            }
        }

        private sealed class Subscription(
            string symbol,
            Action<MarketMarkPriceObservation> onObservation,
            Action onFailure,
            ConcurrentDictionary<string, Subscription> active) : IAsyncDisposable
        {
            private int _disposed;

            public void Emit(MarketMarkPriceObservation observation)
            {
                if (Volatile.Read(ref _disposed) == 0)
                {
                    onObservation(observation);
                }
            }

            public void EmitEvenIfDisposed(MarketMarkPriceObservation observation) =>
                onObservation(observation);

            public void Fail() => onFailure();

            public ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    ((ICollection<KeyValuePair<string, Subscription>>)active)
                        .Remove(new KeyValuePair<string, Subscription>(symbol, this));
                }

                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class ControlledMarketMarkPriceSubscriptionProvider
        : IMarketMarkPriceSubscriptionProvider
    {
        private readonly ConcurrentDictionary<string, ConcurrentQueue<Attempt>> _attempts =
            new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, int> _activeLeaseCounts =
            new(StringComparer.Ordinal);

        public int SubscribeCountFor(string symbol) =>
            _attempts.TryGetValue(symbol, out var attempts) ? attempts.Count : 0;

        public int ActiveLeaseCountFor(string symbol) =>
            _activeLeaseCounts.TryGetValue(symbol, out var count) ? count : 0;

        public Attempt AttemptFor(string symbol, int attemptNumber) =>
            _attempts.TryGetValue(symbol, out var attempts)
                ? attempts.ElementAtOrDefault(attemptNumber - 1)
                    ?? throw new InvalidOperationException("Requested subscription attempt does not exist.")
                : throw new InvalidOperationException("Requested subscription attempt does not exist.");

        public Task<IAsyncDisposable> SubscribeAsync(
            InstrumentId symbol,
            Action<MarketMarkPriceObservation> onObservation,
            Action onFailure,
            CancellationToken cancellationToken = default)
        {
            var attempt = new Attempt(
                symbol.Value,
                onObservation,
                onFailure,
                this,
                cancellationToken);
            _attempts.GetOrAdd(symbol.Value, static _ => new()).Enqueue(attempt);
            return attempt.Completion.Task;
        }

        private void RegisterLease(string symbol) =>
            _activeLeaseCounts.AddOrUpdate(symbol, 1, static (_, count) => count + 1);

        private void ReleaseLease(string symbol) =>
            _activeLeaseCounts.AddOrUpdate(symbol, 0, static (_, count) => count - 1);

        public sealed class Attempt(
            string symbol,
            Action<MarketMarkPriceObservation> onObservation,
            Action onFailure,
            ControlledMarketMarkPriceSubscriptionProvider provider,
            CancellationToken cancellationToken)
        {
            public TaskCompletionSource<IAsyncDisposable> Completion { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public CancellationToken CancellationToken { get; } = cancellationToken;

            public ControlledLease Complete() => Complete(null);

            public ControlledLease Complete(Task? disposalGate)
            {
                var lease = new ControlledLease(symbol, provider, disposalGate);
                provider.RegisterLease(symbol);
                if (!Completion.TrySetResult(lease))
                {
                    provider.ReleaseLease(symbol);
                    throw new InvalidOperationException("Subscription attempt has already completed.");
                }

                return lease;
            }

            public void Fail(Exception exception) => Completion.TrySetException(exception);

            public void Emit(MarketMarkPriceObservation observation) => onObservation(observation);

            public void FailStream() => onFailure();
        }

        public sealed class ControlledLease(
            string symbol,
            ControlledMarketMarkPriceSubscriptionProvider provider,
            Task? disposalGate) : IAsyncDisposable
        {
            private int _disposeStarted;
            private int _disposed;

            public TaskCompletionSource DisposeRequested { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

            public async ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
                {
                    return;
                }

                DisposeRequested.TrySetResult();
                if (disposalGate is not null)
                {
                    await disposalGate.ConfigureAwait(false);
                }

                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    provider.ReleaseLease(symbol);
                }
            }
        }
    }
}
