using Intelligence.TradeSystem.Application.Events;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Snapshots;
using Intelligence.TradeSystem.Infrastructure.ApplicationEvents;
using Intelligence.TradeSystem.Infrastructure.Persistence;
using Intelligence.TradeSystem.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests;

public sealed class ApplicationEventOutboxDispatcherTests
{
    private static readonly int[] ExpectedSequence = [1, 2];

    [Fact]
    public void Dispatcher_is_enabled_by_default_and_instance_id_is_bounded()
    {
        Assert.True(new ApplicationEventOutboxDispatcherOptions().Enabled);
        Assert.InRange(
            ApplicationEventOutboxDispatcherWorker.CreateInstanceId().Length,
            1,
            128);
    }

    [Fact]
    public void Explicitly_enabled_configuration_registers_the_worker()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:TradeSystem"] = "Host=localhost;Database=test",
                ["ApplicationEventOutboxDispatcher:Enabled"] = "true",
            })
            .Build();
        var services = new ServiceCollection();

        services.AddApplicationEventOutboxDispatcher(configuration);

        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IHostedService) &&
                          descriptor.ImplementationType ==
                          typeof(ApplicationEventOutboxDispatcherWorker));
    }

    [Fact]
    public async Task Success_dispatches_typed_event_and_marks_it_processed()
    {
        var store = new InMemoryOutboxStore();
        var received = new List<PositionOpenedEventV1>();
        var handler = new RecordingOpenedHandler(received);
        using var fixture = CreateDispatcher(store, handler);
        var applicationEvent = CreateOpenedEvent();
        var claim = CreateClaim(applicationEvent);

        await fixture.Worker.DispatchBatchAsync([claim], CancellationToken.None);

        Assert.Single(received);
        Assert.Equal(applicationEvent.EventId, received[0].EventId);
        Assert.Equal(applicationEvent.PositionChangeSequence, received[0].PositionChangeSequence);
        Assert.Single(store.Processed);
        Assert.Empty(store.Retries);
    }

    [Fact]
    public async Task Handler_failure_leaves_event_pending_and_schedules_retry()
    {
        var store = new InMemoryOutboxStore();
        using var fixture = CreateDispatcher(
            store,
            new RecordingOpenedHandler([], throwOnHandle: true));

        await fixture.Worker.DispatchBatchAsync(
            [CreateClaim(CreateOpenedEvent())],
            CancellationToken.None);

        Assert.Empty(store.Processed);
        Assert.Single(store.Retries);
    }

    [Fact]
    public async Task No_handler_does_not_mark_event_processed()
    {
        var store = new InMemoryOutboxStore();
        using var fixture = CreateDispatcher(store);

        await fixture.Worker.DispatchBatchAsync(
            [CreateClaim(CreateOpenedEvent())],
            CancellationToken.None);

        Assert.Empty(store.Processed);
        Assert.Single(store.Retries);
    }

    [Theory]
    [InlineData("future.unknown", 1, "{\"eventType\":\"future.unknown\",\"schemaVersion\":1}")]
    [InlineData("position.opened", 99, "{\"eventType\":\"position.opened\",\"schemaVersion\":1}")]
    [InlineData("position.opened", 1, "{")]
    public async Task Invalid_event_contracts_remain_retriable(
        string eventType,
        int schemaVersion,
        string payload)
    {
        var store = new InMemoryOutboxStore();
        using var fixture = CreateDispatcher(store);
        var claim = CreateClaim(
            CreateOpenedEvent(),
            eventType,
            schemaVersion,
            payload);

        await fixture.Worker.DispatchBatchAsync([claim], CancellationToken.None);

        Assert.Empty(store.Processed);
        Assert.Single(store.Retries);
    }

    [Fact]
    public async Task Stale_claim_cannot_mark_a_reclaimed_event_processed()
    {
        var store = new InMemoryOutboxStore { MarkProcessedResult = false };
        var received = new List<PositionOpenedEventV1>();
        using var fixture = CreateDispatcher(
            store,
            new RecordingOpenedHandler(received));

        await fixture.Worker.DispatchBatchAsync(
            [CreateClaim(CreateOpenedEvent())],
            CancellationToken.None);

        Assert.Single(received);
        Assert.Empty(store.Processed);
        Assert.Empty(store.Retries);
    }

    [Fact]
    public async Task Same_position_events_are_handled_in_sequence_order()
    {
        var store = new InMemoryOutboxStore();
        var received = new List<int>();
        using var fixture = CreateDispatcher(
            store,
            changedHandler: new RecordingChangedHandler(received));
        var first = CreateChangedEvent(Guid.NewGuid(), 1);
        var second = CreateChangedEvent(first.PositionId, 2);

        await fixture.Worker.DispatchBatchAsync(
            [CreateClaim(second), CreateClaim(first)],
            CancellationToken.None);

        Assert.Equal(ExpectedSequence, received);
        Assert.Equal(2, store.Processed.Count);
    }

    [Fact]
    public async Task Resource_events_are_dispatched_through_their_typed_handlers()
    {
        var store = new InMemoryOutboxStore();
        var openedEvents = new List<PositionOpenedEventV1>();
        var changedEvents = new List<PositionChangedEventV1>();
        var closedEvents = new List<PositionClosedEventV1>();
        var degradedEvents = new List<ExchangeAccountSyncDegradedEventV1>();
        var accountEvents = new List<ExchangeAccountUpdatedEventV1>();
        var portfolioEvents = new List<PortfolioUpdatedEventV1>();
        var evaluationEvents = new List<PositionEvaluationUpdatedEventV1>();
        using var fixture = CreateDispatcher(
            store,
            handler: new RecordingEventHandler<PositionOpenedEventV1>(openedEvents),
            changedHandler: new RecordingEventHandler<PositionChangedEventV1>(changedEvents),
            closedHandler: new RecordingEventHandler<PositionClosedEventV1>(closedEvents),
            degradedHandler: new RecordingEventHandler<ExchangeAccountSyncDegradedEventV1>(degradedEvents),
            accountHandler: new RecordingEventHandler<ExchangeAccountUpdatedEventV1>(accountEvents),
            portfolioHandler: new RecordingEventHandler<PortfolioUpdatedEventV1>(portfolioEvents),
            evaluationHandler: new RecordingEventHandler<PositionEvaluationUpdatedEventV1>(evaluationEvents));
        var userId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var occurredAt = OccurredAt.AddMinutes(1);
        IApplicationEvent[] events =
        [
            CreateOpenedEvent() with { UserId = userId, ExchangeAccountId = accountId, PositionId = positionId },
            CreateChangedEvent(positionId, 2) with { UserId = userId, ExchangeAccountId = accountId },
            CreateClosedEvent(positionId) with { UserId = userId, ExchangeAccountId = accountId },
            new ExchangeAccountSyncDegradedEventV1(
                Guid.NewGuid(),
                occurredAt,
                userId,
                accountId,
                ExchangeId.Bybit,
                "positions_partial",
                OccurredAt),
            new ExchangeAccountUpdatedEventV1(Guid.NewGuid(), occurredAt, userId, accountId),
            new PortfolioUpdatedEventV1(Guid.NewGuid(), occurredAt, userId, accountId),
            new PositionEvaluationUpdatedEventV1(Guid.NewGuid(), occurredAt, userId, positionId),
        ];

        await fixture.Worker.DispatchBatchAsync(
            events
                .Select(applicationEvent =>
                {
                    var serialized = ApplicationEventSerializer.Serialize(applicationEvent);
                    return CreateClaim(ApplicationEventSerializer.Deserialize(
                        serialized.EventType,
                        serialized.SchemaVersion,
                        serialized.Payload));
                })
                .ToArray(),
            CancellationToken.None);

        Assert.Single(openedEvents);
        Assert.Single(changedEvents);
        Assert.Single(closedEvents);
        Assert.Single(degradedEvents);
        Assert.Single(accountEvents);
        Assert.Single(portfolioEvents);
        Assert.Single(evaluationEvents);
        Assert.Equal(7, store.Processed.Count);
    }

    private static DispatcherFixture CreateDispatcher(
        InMemoryOutboxStore store,
        IApplicationEventHandler<PositionOpenedEventV1>? handler = null,
        IApplicationEventHandler<PositionChangedEventV1>? changedHandler = null,
        IApplicationEventHandler<PositionClosedEventV1>? closedHandler = null,
        IApplicationEventHandler<ExchangeAccountSyncDegradedEventV1>? degradedHandler = null,
        IApplicationEventHandler<ExchangeAccountUpdatedEventV1>? accountHandler = null,
        IApplicationEventHandler<PortfolioUpdatedEventV1>? portfolioHandler = null,
        IApplicationEventHandler<PositionEvaluationUpdatedEventV1>? evaluationHandler = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOutboxMessageStore>(store);
        if (handler is not null)
        {
            services.AddSingleton<IApplicationEventHandler<PositionOpenedEventV1>>(handler);
        }
        if (changedHandler is not null)
        {
            services.AddSingleton<IApplicationEventHandler<PositionChangedEventV1>>(changedHandler);
        }
        if (closedHandler is not null)
        {
            services.AddSingleton<IApplicationEventHandler<PositionClosedEventV1>>(closedHandler);
        }
        if (degradedHandler is not null)
        {
            services.AddSingleton<IApplicationEventHandler<ExchangeAccountSyncDegradedEventV1>>(degradedHandler);
        }
        if (accountHandler is not null)
        {
            services.AddSingleton<IApplicationEventHandler<ExchangeAccountUpdatedEventV1>>(accountHandler);
        }
        if (portfolioHandler is not null)
        {
            services.AddSingleton<IApplicationEventHandler<PortfolioUpdatedEventV1>>(portfolioHandler);
        }
        if (evaluationHandler is not null)
        {
            services.AddSingleton<IApplicationEventHandler<PositionEvaluationUpdatedEventV1>>(evaluationHandler);
        }

        var provider = services.BuildServiceProvider();
        var worker = new ApplicationEventOutboxDispatcherWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ApplicationEventOutboxDispatcherOptions
            {
                Enabled = true,
                MaxConcurrency = 2,
                RetryBaseDelay = TimeSpan.FromSeconds(1),
            }),
            new FixedTimeProvider(OccurredAt.AddMinutes(10)),
            NullLogger<ApplicationEventOutboxDispatcherWorker>.Instance);
        return new DispatcherFixture(provider, worker);
    }

    private static OutboxMessageClaim CreateClaim(
        PositionOpenedEventV1 applicationEvent,
        string? eventType = null,
        int? schemaVersion = null,
        string? payload = null) =>
        CreateClaimCore(
            applicationEvent.EventId,
            eventType ?? applicationEvent.EventType,
            schemaVersion ?? applicationEvent.SchemaVersion,
            applicationEvent.OccurredAt,
            payload ?? ApplicationEventSerializer.Serialize(applicationEvent).Payload);

    private static OutboxMessageClaim CreateClaim(
        PositionChangedEventV1 applicationEvent) =>
        CreateClaim((IApplicationEvent)applicationEvent);

    private static OutboxMessageClaim CreateClaim(IApplicationEvent applicationEvent) =>
        CreateClaimCore(
            applicationEvent.EventId,
            applicationEvent.EventType,
            applicationEvent.SchemaVersion,
            applicationEvent.OccurredAt,
            ApplicationEventSerializer.Serialize(applicationEvent).Payload);

    private static OutboxMessageClaim CreateClaimCore(
        Guid eventId,
        string eventType,
        int schemaVersion,
        DateTimeOffset occurredAt,
        string payload) =>
        new(
            eventId,
            eventType,
            schemaVersion,
            occurredAt,
            payload,
            OccurredAt,
            0,
            "claim-owner",
            Guid.NewGuid(),
            OccurredAt,
            OccurredAt.AddMinutes(5));

    private static PositionOpenedEventV1 CreateOpenedEvent() =>
        new(
            Guid.NewGuid(),
            OccurredAt,
            Guid.NewGuid(),
            Guid.NewGuid(),
            ExchangeId.Bybit,
            Guid.NewGuid(),
            1,
            "BTCUSDT",
            MarketCategory.Linear,
            PositionSide.Long,
            0,
            OccurredAt,
            OccurredAt,
            null,
            PositionChangeKind.New,
            PositionChangeCause.InitialObservation,
            PositionTrackingState.Active,
            null,
            new PositionStateEventPayloadV1(
                1m,
                100m,
                100m,
                2m,
                100m,
                null,
                null,
                0m,
                null,
                null,
                null));

    private static PositionChangedEventV1 CreateChangedEvent(
        Guid positionId,
        int sequence) =>
        new(
            Guid.NewGuid(),
            OccurredAt.AddMinutes(sequence),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ExchangeId.Bybit,
            positionId,
            sequence,
            "BTCUSDT",
            MarketCategory.Linear,
            PositionSide.Long,
            0,
            OccurredAt,
            OccurredAt.AddMinutes(sequence),
            null,
            PositionChangeKind.Increased,
            PositionChangeCause.ExchangeObservation,
            PositionTrackingState.Active,
            new PositionStateEventPayloadV1(
                1m,
                100m,
                100m,
                2m,
                100m,
                null,
                null,
                0m,
                null,
                null,
                null),
            new PositionStateEventPayloadV1(
                2m,
                100m,
                200m,
                2m,
                100m,
                null,
                null,
                0m,
                null,
                null,
                null));

    private static PositionClosedEventV1 CreateClosedEvent(Guid positionId) =>
        new(
            Guid.NewGuid(),
            OccurredAt.AddMinutes(3),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ExchangeId.Bybit,
            positionId,
            3,
            "BTCUSDT",
            MarketCategory.Linear,
            PositionSide.Long,
            0,
            OccurredAt,
            OccurredAt.AddMinutes(3),
            OccurredAt.AddMinutes(3),
            PositionChangeKind.Closed,
            PositionChangeCause.MissingFromCompleteObservation,
            PositionTrackingState.Closed,
            new PositionStateEventPayloadV1(
                1m,
                100m,
                100m,
                2m,
                100m,
                null,
                null,
                0m,
                null,
                null,
                null),
            new PositionStateEventPayloadV1(
                0m,
                0m,
                0m,
                2m,
                100m,
                null,
                null,
                0m,
                null,
                null,
                null));

    private static readonly DateTimeOffset OccurredAt =
        new(2026, 9, 9, 10, 0, 0, TimeSpan.Zero);

    private sealed record DispatcherFixture(
        ServiceProvider Provider,
        ApplicationEventOutboxDispatcherWorker Worker) : IDisposable
    {
        public void Dispose() => Provider.Dispose();
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class RecordingOpenedHandler(
        List<PositionOpenedEventV1> received,
        bool throwOnHandle = false)
        : IApplicationEventHandler<PositionOpenedEventV1>
    {
        public Task HandleAsync(
            PositionOpenedEventV1 applicationEvent,
            CancellationToken cancellationToken = default)
        {
            if (throwOnHandle)
                throw new InvalidOperationException("Injected handler failure.");

            received.Add(applicationEvent);
            return Task.CompletedTask;
        }

    }

    private sealed class RecordingChangedHandler(List<int> received)
        : IApplicationEventHandler<PositionChangedEventV1>
    {
        public Task HandleAsync(
            PositionChangedEventV1 applicationEvent,
            CancellationToken cancellationToken = default)
        {
            received.Add(applicationEvent.PositionChangeSequence);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingEventHandler<TEvent>(
        List<TEvent> received) : IApplicationEventHandler<TEvent>
        where TEvent : IApplicationEvent
    {
        public Task HandleAsync(
            TEvent applicationEvent,
            CancellationToken cancellationToken = default)
        {
            received.Add(applicationEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryOutboxStore : IOutboxMessageStore
    {
        public List<OutboxMessageClaim> Processed { get; } = [];
        public List<OutboxMessageClaim> Retries { get; } = [];
        public bool MarkProcessedResult { get; set; } = true;

        public Task<IReadOnlyList<OutboxMessageClaim>> ClaimAsync(
            int batchSize,
            string claimedBy,
            DateTimeOffset now,
            TimeSpan claimDuration,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OutboxMessageClaim>>([]);

        public Task<bool> MarkProcessedAsync(
            OutboxMessageClaim claim,
            DateTimeOffset processedAt,
            CancellationToken cancellationToken = default)
        {
            if (MarkProcessedResult)
                Processed.Add(claim);
            return Task.FromResult(MarkProcessedResult);
        }

        public Task<bool> ScheduleRetryAsync(
            OutboxMessageClaim claim,
            DateTimeOffset nextAttemptAt,
            CancellationToken cancellationToken = default)
        {
            Retries.Add(claim);
            return Task.FromResult(true);
        }
    }
}
