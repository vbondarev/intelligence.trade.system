using Intelligence.TradeSystem.Application.Assessments;
using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Events;
using Intelligence.TradeSystem.Application.Evaluations;
using Intelligence.TradeSystem.Application.Market;
using Intelligence.TradeSystem.Application.Recommendations;
using Intelligence.TradeSystem.Application.Time;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Decisions;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Portfolio;
using Intelligence.TradeSystem.Domain.Recommendations;
using Intelligence.TradeSystem.Domain.Snapshots;
using Intelligence.TradeSystem.Infrastructure.Persistence;
using Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;
using Intelligence.TradeSystem.MarketIntelligence.Snapshots;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests;

[Collection("PostgreSql")]
public sealed class PositionEvaluationConcurrencyPostgreSqlTests(
    PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset T0 =
        new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Concurrent_full_evaluations_for_one_position_complete_without_deadlock()
    {
        var userId = UserId.New();
        var account = CreateAccount(userId);
        var position = CreatePosition(account.Id);
        var portfolio = PortfolioState.Create(
            account.Id,
            [position],
            new PortfolioCapitalState(1_000m, 800m, T0.AddMinutes(1), 1_000m),
            T0.AddMinutes(1),
            TimeSpan.FromMinutes(10),
            positionsFullyReconciled: true);

        await using (var setup = await CreateMigratedContext())
        {
            await new ExchangeAccountRepository(setup)
                .SaveAsync(userId, account, expectedVersion: null);
            await new PositionRepository(setup)
                .SaveAsync(userId, position, expectedVersion: null);
            await new PortfolioStateRepository(setup)
                .SaveAsync(userId, portfolio);
        }

        using var transactionBoundary = new Barrier(2);
        var first = RunEvaluationAsync(
            userId,
            position.Id,
            transactionBoundary);
        var second = RunEvaluationAsync(
            userId,
            position.Id,
            transactionBoundary);

        var results = await Task
            .WhenAll(first, second)
            .WaitAsync(TimeSpan.FromSeconds(30));

        Assert.All(
            results,
            result => Assert.Equal(PositionEvaluationOutcome.Succeeded, result.Outcome));

        await using var verification = await CreateMigratedContext();
        Assert.Equal(
            2,
            await verification.PositionAssessments.CountAsync(
                assessment =>
                    assessment.PositionId == position.Id.Value &&
                    assessment.ExchangeAccountId == account.Id.Value));

        var currentRecommendations = await verification.Recommendations
            .Where(recommendation =>
                recommendation.PositionId == position.Id.Value &&
                (recommendation.Status == RecommendationStatus.Active ||
                 recommendation.Status == RecommendationStatus.Acknowledged))
            .ToArrayAsync();
        Assert.Single(currentRecommendations);
        Assert.Equal(position.Id.Value, currentRecommendations[0].PositionId);

        var stabilityStates = await verification.RecommendationStabilityStates
            .Where(state => state.PositionId == position.Id.Value)
            .ToArrayAsync();
        Assert.InRange(stabilityStates.Length, 0, 1);
        if (stabilityStates is [{ } state])
        {
            Assert.Equal(
                currentRecommendations[0].Id,
                state.BaselineRecommendationId);
            Assert.True(state.ConsecutiveObservations > 0);
        }

        var evaluationEvents = (await verification.OutboxMessages
                .Where(message =>
                    message.EventType == ApplicationEventTypes.PositionEvaluationUpdated)
                .ToArrayAsync())
            .Where(message =>
                message.Payload.Contains(
                    position.Id.Value.ToString(),
                    StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, evaluationEvents.Length);
    }

    [Fact]
    public async Task Concurrent_evaluations_with_inverted_logical_time_return_concurrency_conflict_without_partial_state()
    {
        var userId = UserId.New();
        var account = CreateAccount(userId);
        var position = CreatePosition(account.Id);
        var portfolio = PortfolioState.Create(
            account.Id,
            [position],
            new PortfolioCapitalState(1_000m, 800m, T0.AddMinutes(1), 1_000m),
            T0.AddMinutes(1),
            TimeSpan.FromMinutes(10),
            positionsFullyReconciled: true);

        await using (var setup = await CreateMigratedContext())
        {
            await new ExchangeAccountRepository(setup)
                .SaveAsync(userId, account, expectedVersion: null);
            await new PositionRepository(setup)
                .SaveAsync(userId, position, expectedVersion: null);
            await new PortfolioStateRepository(setup)
                .SaveAsync(userId, portfolio);
        }

        var coordinator = new InvertedLogicalTimeCoordinator();
        var earlier = RunEvaluationAsync(
            userId,
            position.Id,
            T0.AddMinutes(3),
            context => new InvertedLogicalTimeEvaluationTransaction(
                new PositionEvaluationTransaction(context),
                coordinator,
                isEarlier: true));
        await coordinator.EarlierReached.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var later = RunEvaluationAsync(
            userId,
            position.Id,
            T0.AddMinutes(4),
            context => new InvertedLogicalTimeEvaluationTransaction(
                new PositionEvaluationTransaction(context),
                coordinator,
                isEarlier: false));

        var laterResult = await later.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(PositionEvaluationOutcome.Succeeded, laterResult.Outcome);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            async () => await earlier.WaitAsync(TimeSpan.FromSeconds(30)));

        await using var verification = await CreateMigratedContext();
        var assessments = await verification.PositionAssessments
            .Where(assessment =>
                assessment.PositionId == position.Id.Value &&
                assessment.ExchangeAccountId == account.Id.Value)
            .ToArrayAsync();
        Assert.Single(assessments);
        Assert.Equal(T0.AddMinutes(4), assessments[0].CreatedAt);

        var currentRecommendations = await verification.Recommendations
            .Where(recommendation =>
                recommendation.PositionId == position.Id.Value &&
                (recommendation.Status == RecommendationStatus.Active ||
                 recommendation.Status == RecommendationStatus.Acknowledged))
            .ToArrayAsync();
        Assert.Single(currentRecommendations);
        Assert.Equal(T0.AddMinutes(4), currentRecommendations[0].CreatedAt);

        var stabilityStates = await verification.RecommendationStabilityStates
            .Where(state => state.PositionId == position.Id.Value)
            .ToArrayAsync();
        Assert.Empty(stabilityStates);

        var evaluationEvents = (await verification.OutboxMessages
                .Where(message =>
                    message.EventType == ApplicationEventTypes.PositionEvaluationUpdated)
                .ToArrayAsync())
            .Where(message =>
                message.Payload.Contains(
                    position.Id.Value.ToString(),
                    StringComparison.Ordinal))
            .ToArray();
        Assert.Single(evaluationEvents);
    }

    [Fact]
    public async Task Evaluation_rejects_a_position_snapshot_changed_before_lock_without_partial_state()
    {
        var userId = UserId.New();
        var account = CreateAccount(userId);
        var position = CreatePosition(account.Id);
        var portfolio = PortfolioState.Create(
            account.Id,
            [position],
            new PortfolioCapitalState(1_000m, 800m, T0.AddMinutes(1), 1_000m),
            T0.AddMinutes(1),
            TimeSpan.FromMinutes(10),
            positionsFullyReconciled: true);

        await using (var setup = await CreateMigratedContext())
        {
            await new ExchangeAccountRepository(setup)
                .SaveAsync(userId, account, expectedVersion: null);
            await new PositionRepository(setup)
                .SaveAsync(userId, position, expectedVersion: null);
            await new PortfolioStateRepository(setup)
                .SaveAsync(userId, portfolio);
        }

        var coordinator = new BlockingPositionEvaluationCoordinator();
        var evaluation = RunEvaluationAsync(
            userId,
            position.Id,
            T0.AddMinutes(3),
            context => new BlockingPositionEvaluationTransaction(
                new PositionEvaluationTransaction(context),
                coordinator));

        try
        {
            await coordinator.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10));

            await using (var mutationContext = await CreateMigratedContext())
            {
                var currentPosition = await new PositionRepository(mutationContext)
                    .GetByIdAsync(userId, position.Id);
                Assert.NotNull(currentPosition);
                var changedPosition = currentPosition!;
                changedPosition.Value.MarkUnknown(T0.AddMinutes(3));
                await new PositionRepository(mutationContext)
                    .SaveAsync(userId, changedPosition.Value, changedPosition.Version);
            }

            coordinator.Continue.TrySetResult(null);
            await Assert.ThrowsAsync<ConcurrencyConflictException>(
                async () => await evaluation.WaitAsync(TimeSpan.FromSeconds(30)));
        }
        finally
        {
            coordinator.Continue.TrySetResult(null);
        }

        await using var verification = await CreateMigratedContext();
        Assert.Empty(await verification.PositionAssessments
            .Where(assessment =>
                assessment.PositionId == position.Id.Value &&
                assessment.ExchangeAccountId == account.Id.Value)
            .ToArrayAsync());
        var evaluationEvents = (await verification.OutboxMessages
            .Where(message =>
                message.EventType == ApplicationEventTypes.PositionEvaluationUpdated)
            .ToArrayAsync())
            .Where(message =>
                message.Payload.Contains(
                    position.Id.Value.ToString(),
                    StringComparison.Ordinal))
            .ToArray();
        Assert.Empty(evaluationEvents);
    }

    private async Task<PositionEvaluationResult> RunEvaluationAsync(
        UserId userId,
        PositionId positionId,
        Barrier transactionBoundary)
    {
        return await RunEvaluationAsync(
            userId,
            positionId,
            T0.AddMinutes(3),
            context => new CoordinatedPositionEvaluationTransaction(
                new PositionEvaluationTransaction(context),
                transactionBoundary));
    }

    private async Task<PositionEvaluationResult> RunEvaluationAsync(
        UserId userId,
        PositionId positionId,
        DateTimeOffset asOf,
        Func<TradeSystemDbContext, IPositionEvaluationTransaction> transactionFactory)
    {
        await using var context = fixture.CreateContext();
        await Task.Yield();

        var market = CreateMarketSnapshot(asOf.AddMinutes(-1));
        var policyProvider = new FixedPolicyProvider();
        var positionAssessmentRepository = new PositionAssessmentRepository(context);
        var recommendationRepository = new RecommendationRepository(context);
        var stabilityRepository = new RecommendationStabilityStateRepository(context);
        var recommendationService = new RecommendationService(
            policyProvider,
            new Intelligence.TradeSystem.Domain.Recommendations.RecommendationPolicy(),
            new RecommendationStabilityPolicy(),
            recommendationRepository,
            stabilityRepository,
            new RecommendationPublicationTransaction(
                context,
                recommendationRepository,
                stabilityRepository),
            positionAssessmentRepository);
        var evaluationTransaction = transactionFactory(context);
        var service = new PositionEvaluationService(
            new PositionRepository(context),
            new ExchangeAccountRepository(context),
            new PortfolioStateRepository(context),
            positionAssessmentRepository,
            recommendationRepository,
            new FixedMarketSnapshotService(market),
            policyProvider,
            new PositionAssessmentService(),
            recommendationService,
            evaluationTransaction,
            new ApplicationEventOutbox(context),
            new PositionEvaluationPolicySettings(
                PositionAssessmentRules.Default,
                new PortfolioRiskPolicySettings(0m, 200m, 100m)),
            new FixedTimeProvider(asOf));

        return await service.EvaluateAsync(userId, positionId);
    }

    private async Task<TradeSystemDbContext> CreateMigratedContext()
    {
        var context = fixture.CreateContext();
        await context.Database.MigrateAsync();
        return context;
    }

    private static ExchangeAccount CreateAccount(UserId userId) =>
        ExchangeAccount.Create(
            ExchangeAccountId.New(),
            userId,
            ExchangeId.Bybit,
            ExchangeAccountProviderIdentity.From("provider-account"),
            ExchangeAccountConnectionStatus.Connected,
            ExchangeAccountCapabilities.ReadBalance |
            ExchangeAccountCapabilities.ReadPositions,
            lastSyncedAt: T0);

    private static Position CreatePosition(ExchangeAccountId accountId) =>
        Position.Create(
            ExchangePositionKey.Create(
                accountId,
                InstrumentId.From("BTCUSDT"),
                PositionSide.Long,
                0),
            MarketCategory.Linear,
            1m,
            T0,
            T0,
            averageEntryPrice: 100m,
            positionValue: 100m,
            leverage: 2m,
            markPrice: 105m,
            breakEvenPrice: 100m,
            liquidationPrice: 50m,
            unrealizedPnl: 5m,
            stopLoss: 95m);

    private static MarketSnapshot CreateMarketSnapshot(DateTimeOffset capturedAt)
    {
        var timeframe = new TimeframeAnalysisSnapshot
        {
            Timeframe = "4h",
            LastCandleOpenTimeUtc = capturedAt.AddMinutes(-1),
            LastCandle = new CandleSnapshot
            {
                OpenTimeUtc = capturedAt.AddMinutes(-1),
                Open = 105m,
                High = 107m,
                Low = 103m,
                Close = 105m,
                Volume = 100m,
                Turnover = 10_500m,
            },
            Rsi14 = 50m,
            Rsi14IsReliable = true,
            Atr14 = 2m,
            AtrIsReliable = true,
            VolumeRatio = 1m,
            VolumeRatioIsReliable = true,
            Trend = MarketTrend.Bullish,
            TrendStrengthScore = 0.8m,
            Support1 = 100m,
            Support1Strength = 0.7m,
            Resistance1 = 110m,
            Resistance1Strength = 0.7m,
        };

        return new MarketSnapshot
        {
            Exchange = "Bybit",
            Symbol = "BTCUSDT",
            Category = "Linear",
            CapturedAtUtc = capturedAt,
            Price = new PriceSnapshot { LastPrice = 105m, MarkPrice = 105m },
            Derivatives = new(),
            OrderBook = new() { CapturedAtUtc = capturedAt },
            TradeFlow = new() { WindowEndUtc = capturedAt },
            M15 = timeframe with { Timeframe = "15m" },
            H1 = timeframe with { Timeframe = "1h" },
            H4 = timeframe,
            D1 = timeframe with { Timeframe = "1d" },
            Sentiment = new(),
        };
    }

    private sealed class CoordinatedPositionEvaluationTransaction(
        IPositionEvaluationTransaction inner,
        Barrier barrier) : IPositionEvaluationTransaction
    {
        public async Task ExecuteAsync(
            UserId userId,
            PositionId positionId,
            Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            barrier.SignalAndWait(cancellationToken);
            await inner.ExecuteAsync(
                userId,
                positionId,
                operation,
                cancellationToken);
        }
    }

    private sealed class BlockingPositionEvaluationCoordinator
    {
        public TaskCompletionSource<object?> Reached { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<object?> Continue { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class BlockingPositionEvaluationTransaction(
        IPositionEvaluationTransaction inner,
        BlockingPositionEvaluationCoordinator coordinator) : IPositionEvaluationTransaction
    {
        public async Task ExecuteAsync(
            UserId userId,
            PositionId positionId,
            Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken = default)
        {
            coordinator.Reached.TrySetResult(null);
            await coordinator.Continue.Task.WaitAsync(cancellationToken);
            await inner.ExecuteAsync(
                userId,
                positionId,
                operation,
                cancellationToken);
        }
    }

    private sealed class InvertedLogicalTimeCoordinator
    {
        public TaskCompletionSource<object?> EarlierReached { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<object?> LaterCompleted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class InvertedLogicalTimeEvaluationTransaction(
        IPositionEvaluationTransaction inner,
        InvertedLogicalTimeCoordinator coordinator,
        bool isEarlier) : IPositionEvaluationTransaction
    {
        public async Task ExecuteAsync(
            UserId userId,
            PositionId positionId,
            Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken = default)
        {
            if (isEarlier)
            {
                coordinator.EarlierReached.TrySetResult(null);
                await coordinator.LaterCompleted.Task.WaitAsync(cancellationToken);
                await inner.ExecuteAsync(
                    userId,
                    positionId,
                    operation,
                    cancellationToken);
                return;
            }

            try
            {
                await inner.ExecuteAsync(
                    userId,
                    positionId,
                    operation,
                    cancellationToken);
                coordinator.LaterCompleted.TrySetResult(null);
            }
            catch (Exception exception)
            {
                coordinator.LaterCompleted.TrySetException(exception);
                throw;
            }
        }
    }

    private sealed class FixedMarketSnapshotService(MarketSnapshot snapshot)
        : IMarketSnapshotService
    {
        public Task<MarketSnapshot> BuildSnapshotAsync(
            ExchangeId exchangeId,
            string symbol,
            MarketCategory category,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);
    }

    private sealed class FixedPolicyProvider : IRecommendationPolicyDefinitionProvider
    {
        public ValueTask<PolicyDefinition> GetAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(PolicyDefinition.Default);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
