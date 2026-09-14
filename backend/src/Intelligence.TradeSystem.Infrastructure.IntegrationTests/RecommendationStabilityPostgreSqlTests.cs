using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Recommendations;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Decisions;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Portfolio;
using Intelligence.TradeSystem.Domain.Recommendations;
using Intelligence.TradeSystem.Domain.Snapshots;
using Intelligence.TradeSystem.Infrastructure.Persistence;
using Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests;

[Collection("PostgreSql")]
public sealed class RecommendationStabilityPostgreSqlTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Current_lookup_and_pending_state_round_trip_are_user_scoped_and_versioned()
    {
        var aggregate = CreateAggregate(UserId.New());
        await PersistAsync(aggregate);
        var pendingState = new RecommendationStabilityState(
            RecommendationSemanticState.From(aggregate.Recommendation),
            T0.AddMinutes(5),
            T0.AddMinutes(6),
            2);
        var snapshot = new RecommendationStabilityStateSnapshot(
            aggregate.Recommendation.Id,
            pendingState);

        await using (var context = await CreateMigratedContext())
        {
            var stateRepository = new RecommendationStabilityStateRepository(context);
            var savedVersion = await stateRepository.SaveAsync(
                aggregate.Account.UserId,
                aggregate.Position.Id,
                snapshot,
                expectedVersion: null);
            Assert.Equal(ConcurrencyVersion.Initial, savedVersion);
        }

        await using (var context = await CreateMigratedContext())
        {
            var recommendations = new RecommendationRepository(context);
            var states = new RecommendationStabilityStateRepository(context);
            var current = await recommendations.GetCurrentForPositionAsync(
                aggregate.Account.UserId,
                aggregate.Position.Id);
            var loaded = await states.GetAsync(
                aggregate.Account.UserId,
                aggregate.Position.Id);

            Assert.NotNull(current);
            Assert.Equal(aggregate.Recommendation.Id, current.Value.Id);
            Assert.Equal(ConcurrencyVersion.Initial, current.Version);
            Assert.NotNull(loaded);
            Assert.Equal(ConcurrencyVersion.Initial, loaded.Version);
            Assert.Equal(snapshot.BaselineRecommendationId, loaded.Value.BaselineRecommendationId);
            Assert.Equal(snapshot.State, loaded.Value.State);
        }

        await using (var context = await CreateMigratedContext())
        {
            var foreign = UserId.New();
            var recommendations = new RecommendationRepository(context);
            var states = new RecommendationStabilityStateRepository(context);

            Assert.Null(await recommendations.GetCurrentForPositionAsync(foreign, aggregate.Position.Id));
            Assert.Null(await states.GetAsync(foreign, aggregate.Position.Id));
            await Assert.ThrowsAsync<ConcurrencyConflictException>(
                () => states.SaveAsync(
                    foreign,
                    aggregate.Position.Id,
                    snapshot,
                    expectedVersion: null));
        }
    }

    [Fact]
    public async Task Pending_state_compare_and_swap_rejects_a_stale_writer()
    {
        var aggregate = CreateAggregate(UserId.New());
        await PersistAsync(aggregate);
        var state = new RecommendationStabilityStateSnapshot(
            aggregate.Recommendation.Id,
            new RecommendationStabilityState(
                RecommendationSemanticState.From(aggregate.Recommendation),
                T0.AddMinutes(5),
                T0.AddMinutes(5),
                1));

        await using (var context = await CreateMigratedContext())
        {
            await new RecommendationStabilityStateRepository(context)
                .SaveAsync(aggregate.Account.UserId, aggregate.Position.Id, state, null);
        }

        await using var writer = await CreateMigratedContext();
        var repository = new RecommendationStabilityStateRepository(writer);
        var loaded = await repository.GetAsync(aggregate.Account.UserId, aggregate.Position.Id);
        Assert.NotNull(loaded);

        var next = new RecommendationStabilityStateSnapshot(
            state.BaselineRecommendationId,
            new RecommendationStabilityState(
                state.State.SemanticState,
                state.State.FirstObservedAt,
                T0.AddMinutes(7),
                2));
        var nextVersion = await repository.SaveAsync(
            aggregate.Account.UserId,
            aggregate.Position.Id,
            next,
            loaded.Version);
        Assert.Equal(loaded.Version.Next(), nextVersion);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => repository.SaveAsync(
                aggregate.Account.UserId,
                aggregate.Position.Id,
                state,
                loaded.Version));
    }

    [Fact]
    public async Task Current_lookup_includes_acknowledged_and_excludes_terminal_history()
    {
        var aggregate = CreateAggregate(UserId.New());
        await PersistAsync(aggregate);

        await using (var context = await CreateMigratedContext())
        {
            var repository = new RecommendationRepository(context);
            var current = await repository.GetCurrentForPositionAsync(
                aggregate.Account.UserId,
                aggregate.Position.Id);
            Assert.NotNull(current);
            current.Value.Acknowledge(T0.AddMinutes(5));
            await repository.SaveAsync(
                aggregate.Account.UserId,
                current.Value,
                current.Version);
        }

        await using (var context = await CreateMigratedContext())
        {
            var repository = new RecommendationRepository(context);
            var acknowledged = await repository.GetCurrentForPositionAsync(
                aggregate.Account.UserId,
                aggregate.Position.Id);
            Assert.NotNull(acknowledged);
            Assert.Equal(RecommendationStatus.Acknowledged, acknowledged.Value.Status);

            acknowledged.Value.Dismiss(T0.AddMinutes(6));
            await repository.SaveAsync(
                aggregate.Account.UserId,
                acknowledged.Value,
                acknowledged.Version);
        }

        await using var verification = await CreateMigratedContext();
        Assert.Null(await new RecommendationRepository(verification)
            .GetCurrentForPositionAsync(aggregate.Account.UserId, aggregate.Position.Id));
    }

    [Fact]
    public async Task Partial_current_index_rejects_two_active_recommendations()
    {
        var aggregate = CreateAggregate(UserId.New());
        await PersistAsync(aggregate);
        var second = Recommendation.RestoreLegacy(
            RecommendationId.New(),
            aggregate.Assessment,
            PositionAction.Watch,
            AddDecision.DoNotAdd,
            new RuleVersion("policy-v1"),
            aggregate.Assessment.ReasonCodes,
            T0.AddMinutes(5),
            T0.AddMinutes(20),
            RecommendationStatus.Active,
            null,
            null,
            null,
            null,
            null);

        await using var context = await CreateMigratedContext();
        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => new RecommendationRepository(context)
                .SaveAsync(aggregate.Account.UserId, second, expectedVersion: null));
    }

    [Fact]
    public async Task Replacement_transaction_updates_old_then_inserts_successor_atomically()
    {
        var aggregate = CreateAggregate(UserId.New());
        await PersistAsync(aggregate);
        var successor = Recommendation.RestoreLegacy(
            RecommendationId.New(),
            aggregate.Assessment,
            PositionAction.Watch,
            AddDecision.DoNotAdd,
            new RuleVersion("policy-v1"),
            aggregate.Assessment.ReasonCodes,
            T0.AddMinutes(5),
            T0.AddMinutes(20),
            RecommendationStatus.Active,
            null,
            null,
            null,
            null,
            null);
        aggregate.Recommendation.SupersedeBy(successor);

        await using (var context = await CreateMigratedContext())
        {
            var transaction = new RecommendationPublicationTransaction(
                context,
                new RecommendationRepository(context),
                new RecommendationStabilityStateRepository(context));
            await transaction.ReplaceAsync(
                aggregate.Account.UserId,
                aggregate.Recommendation,
                ConcurrencyVersion.Initial,
                successor,
                expectedPendingVersion: null);
        }

        await using var verification = await CreateMigratedContext();
        var repository = new RecommendationRepository(verification);
        var current = await repository.GetCurrentForPositionAsync(
            aggregate.Account.UserId,
            aggregate.Position.Id);
        var old = await repository.GetByIdAsync(
            aggregate.Account.UserId,
            aggregate.Recommendation.Id);

        Assert.NotNull(current);
        Assert.Equal(successor.Id, current.Value.Id);
        Assert.NotNull(old);
        Assert.Equal(RecommendationStatus.Superseded, old.Value.Status);
        Assert.Equal(successor.Id, old.Value.SupersededByRecommendationId);
    }

    [Fact]
    public async Task Replacement_failure_rolls_back_old_lifecycle_update()
    {
        var aggregate = CreateAggregate(UserId.New());
        await PersistAsync(aggregate);
        var unpersistedAssessment = PositionAssessment.Create(
            aggregate.Assessment.InputVersions,
            new RuleVersion("assessment-v2"),
            RiskIncreasePolicyResult.Blocked([ReasonCode.PortfolioDataStale]),
            [],
            T0.AddMinutes(5),
            T0.AddHours(1));
        var successor = Recommendation.RestoreLegacy(
            RecommendationId.New(),
            unpersistedAssessment,
            PositionAction.Watch,
            AddDecision.DoNotAdd,
            new RuleVersion("policy-v1"),
            unpersistedAssessment.ReasonCodes,
            T0.AddMinutes(5),
            T0.AddMinutes(20),
            RecommendationStatus.Active,
            null,
            null,
            null,
            null,
            null);
        aggregate.Recommendation.SupersedeBy(successor);

        await using var context = await CreateMigratedContext();
        var transaction = new RecommendationPublicationTransaction(
            context,
            new RecommendationRepository(context),
            new RecommendationStabilityStateRepository(context));
        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => transaction.ReplaceAsync(
                aggregate.Account.UserId,
                aggregate.Recommendation,
                ConcurrencyVersion.Initial,
                successor,
                expectedPendingVersion: null));

        await using var verification = await CreateMigratedContext();
        var current = await new RecommendationRepository(verification)
            .GetCurrentForPositionAsync(aggregate.Account.UserId, aggregate.Position.Id);
        Assert.NotNull(current);
        Assert.Equal(aggregate.Recommendation.Id, current.Value.Id);
        Assert.Equal(RecommendationStatus.Active, current.Value.Status);
    }

    [Fact]
    public async Task Concurrent_replacements_leave_exactly_one_current_successor()
    {
        var aggregate = CreateAggregate(UserId.New());
        await PersistAsync(aggregate);
        var successorA = CreateSuccessor(aggregate);
        var successorB = CreateSuccessor(aggregate);

        await using var firstContext = await CreateMigratedContext();
        await using var secondContext = await CreateMigratedContext();
        var firstCurrent = await new RecommendationRepository(firstContext)
            .GetByIdAsync(aggregate.Account.UserId, aggregate.Recommendation.Id);
        var secondCurrent = await new RecommendationRepository(secondContext)
            .GetByIdAsync(aggregate.Account.UserId, aggregate.Recommendation.Id);
        Assert.NotNull(firstCurrent);
        Assert.NotNull(secondCurrent);
        firstCurrent.Value.SupersedeBy(successorA);
        secondCurrent.Value.SupersedeBy(successorB);

        var firstAttempt = ReplaceAsync(
            firstContext,
            aggregate.Account.UserId,
            firstCurrent.Value,
            successorA);
        var secondAttempt = ReplaceAsync(
            secondContext,
            aggregate.Account.UserId,
            secondCurrent.Value,
            successorB);
        var failures = await Task.WhenAll(firstAttempt, secondAttempt);

        Assert.Equal(1, failures.Count(failure => failure is null));
        Assert.Equal(1, failures.Count(failure => failure is ConcurrencyConflictException));

        await using var verification = await CreateMigratedContext();
        var current = await new RecommendationRepository(verification)
            .GetCurrentForPositionAsync(aggregate.Account.UserId, aggregate.Position.Id);
        Assert.NotNull(current);
        Assert.Contains(current.Value.Id, new[] { successorA.Id, successorB.Id });
        Assert.Equal(
            2,
            await verification.Recommendations
                .CountAsync(recommendation => recommendation.PositionId == aggregate.Position.Id.Value));
    }

    [Fact]
    public async Task Concurrent_initial_publication_leaves_one_current_recommendation()
    {
        var aggregate = CreateAggregate(UserId.New());
        await PersistWithoutRecommendationAsync(aggregate);
        var evaluation = new Intelligence.TradeSystem.Domain.Recommendations.RecommendationPolicy().Evaluate(
            aggregate.Assessment,
            PolicyDefinition.Default,
            T0.AddMinutes(4));
        var candidateA = Recommendation.Create(aggregate.Assessment, evaluation);
        var candidateB = Recommendation.Create(aggregate.Assessment, evaluation);

        await using var firstContext = await CreateMigratedContext();
        await using var secondContext = await CreateMigratedContext();
        var firstAttempt = PublishAsync(
            firstContext,
            aggregate.Account.UserId,
            candidateA);
        var secondAttempt = PublishAsync(
            secondContext,
            aggregate.Account.UserId,
            candidateB);
        var failures = await Task.WhenAll(firstAttempt, secondAttempt);

        Assert.Equal(1, failures.Count(failure => failure is null));
        Assert.Equal(1, failures.Count(failure => failure is ConcurrencyConflictException));

        await using var verification = await CreateMigratedContext();
        var current = await new RecommendationRepository(verification)
            .GetCurrentForPositionAsync(aggregate.Account.UserId, aggregate.Position.Id);
        Assert.NotNull(current);
        Assert.Contains(current.Value.Id, new[] { candidateA.Id, candidateB.Id });
        Assert.Equal(
            1,
            await verification.Recommendations
                .CountAsync(recommendation => recommendation.PositionId == aggregate.Position.Id.Value));
    }

    private async Task PersistAsync(Aggregate aggregate)
    {
        await PersistWithoutRecommendationAsync(aggregate);
        await using var context = await CreateMigratedContext();
        await new RecommendationRepository(context)
            .SaveAsync(aggregate.Account.UserId, aggregate.Recommendation, expectedVersion: null);
    }

    private async Task PersistWithoutRecommendationAsync(Aggregate aggregate)
    {
        await using var context = await CreateMigratedContext();
        await new ExchangeAccountRepository(context)
            .SaveAsync(aggregate.Account.UserId, aggregate.Account, expectedVersion: null);
        await new PositionRepository(context)
            .SaveAsync(aggregate.Account.UserId, aggregate.Position, expectedVersion: null);
        await new PositionAssessmentRepository(context)
            .SaveAsync(aggregate.Account.UserId, aggregate.Assessment);
    }

    private static async Task<Exception?> ReplaceAsync(
        TradeSystemDbContext context,
        UserId userId,
        Recommendation current,
        Recommendation successor)
    {
        try
        {
            await new RecommendationPublicationTransaction(
                context,
                new RecommendationRepository(context),
                new RecommendationStabilityStateRepository(context))
                .ReplaceAsync(
                    userId,
                    current,
                    ConcurrencyVersion.Initial,
                    successor,
                    expectedPendingVersion: null);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static async Task<Exception?> PublishAsync(
        TradeSystemDbContext context,
        UserId userId,
        Recommendation candidate)
    {
        try
        {
            await new RecommendationPublicationTransaction(
                context,
                new RecommendationRepository(context),
                new RecommendationStabilityStateRepository(context))
                .PublishInitialAsync(userId, candidate, expectedPendingVersion: null);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static Recommendation CreateSuccessor(Aggregate aggregate) =>
        Recommendation.RestoreLegacy(
            RecommendationId.New(),
            aggregate.Assessment,
            PositionAction.Watch,
            AddDecision.DoNotAdd,
            new RuleVersion("policy-v1"),
            aggregate.Assessment.ReasonCodes,
            T0.AddMinutes(5),
            T0.AddMinutes(20),
            RecommendationStatus.Active,
            null,
            null,
            null,
            null,
            null);

    private async Task<TradeSystemDbContext> CreateMigratedContext()
    {
        var context = fixture.CreateContext();
        await context.Database.MigrateAsync();
        return context;
    }

    private static Aggregate CreateAggregate(UserId userId)
    {
        var account = ExchangeAccount.Create(
            ExchangeAccountId.New(),
            userId,
            ExchangeId.Bybit,
            ExchangeAccountConnectionStatus.Connected,
            ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions,
            T0,
            null);
        var position = Position.Create(
            ExchangePositionKey.Create(
                account.Id,
                InstrumentId.From("BTCUSDT"),
                PositionSide.Long,
                0),
            MarketCategory.Linear,
            1m,
            T0,
            T0,
            100m,
            100m,
            2m,
            100m,
            0m);
        var assessment = PositionAssessment.Create(
            new PositionAssessmentInputVersions(
                position.Id,
                account.Id,
                position.ExchangePositionKey.InstrumentId,
                T0,
                T0.AddMinutes(1),
                T0.AddMinutes(2)),
            new RuleVersion("assessment-v1"),
            RiskIncreasePolicyResult.Blocked([ReasonCode.PortfolioDataStale]),
            [],
            T0.AddMinutes(3),
            T0.AddHours(1));
        var recommendation = Recommendation.RestoreLegacy(
            RecommendationId.New(),
            assessment,
            PositionAction.Reduce,
            AddDecision.DoNotAdd,
            new RuleVersion("policy-v1"),
            assessment.ReasonCodes,
            T0.AddMinutes(4),
            T0.AddMinutes(30),
            RecommendationStatus.Active,
            null,
            null,
            null,
            null,
            null);

        return new(account, position, assessment, recommendation);
    }

    private sealed record Aggregate(
        ExchangeAccount Account,
        Position Position,
        PositionAssessment Assessment,
        Recommendation Recommendation);
}
