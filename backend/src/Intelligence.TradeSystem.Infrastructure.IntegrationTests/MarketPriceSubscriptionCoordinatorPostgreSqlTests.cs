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
}
