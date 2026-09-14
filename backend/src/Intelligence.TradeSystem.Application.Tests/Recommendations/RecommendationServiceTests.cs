using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Assessments;
using Intelligence.TradeSystem.Application.Recommendations;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Decisions;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Portfolio;
using Intelligence.TradeSystem.Domain.Recommendations;
using Intelligence.TradeSystem.Domain.Snapshots;
using Xunit;

namespace Intelligence.TradeSystem.Application.Tests.Recommendations;

public sealed class RecommendationServiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task No_current_recommendation_publishes_only_after_stability_decision()
    {
        var assessment = CreateAssessment();
        var userId = UserId.New();
        var repository = new FakeRecommendationRepository();
        var states = new FakeStabilityStateRepository();
        var publication = new FakePublicationTransaction();
        var service = CreateService(assessment, repository, states, publication);

        var result = await service.CreateAsync(userId, assessment, T0.AddMinutes(4));

        Assert.Equal(
            RecommendationApplicationResultKind.Published,
            result.Kind);
        Assert.NotNull(result.Recommendation);
        Assert.Equal(1, publication.PublishInitialCalls);
        Assert.Equal(0, states.SaveCalls);
        Assert.Equal(0, states.DeleteCalls);
    }

    [Fact]
    public async Task Duplicate_keeps_current_and_clears_pending_without_publication()
    {
        var assessment = CreateAssessment();
        var asOf = T0.AddMinutes(4);
        var evaluation = new RecommendationPolicy().Evaluate(
            assessment,
            PolicyDefinition.Default,
            asOf);
        var current = Recommendation.Create(assessment, evaluation);
        var states = new FakeStabilityStateRepository
        {
            Pending = new Versioned<RecommendationStabilityStateSnapshot>(
                new(
                    current.Id,
                    new RecommendationStabilityState(
                        RecommendationSemanticState.From(current),
                        T0.AddMinutes(3),
                        T0.AddMinutes(3),
                        1)),
                ConcurrencyVersion.Initial)
        };
        var publication = new FakePublicationTransaction();
        var service = CreateService(
            assessment,
            new FakeRecommendationRepository
            {
                Current = new Versioned<Recommendation>(current, ConcurrencyVersion.Initial)
            },
            states,
            publication);

        var result = await service.CreateAsync(UserId.New(), assessment, asOf);

        Assert.Equal(RecommendationApplicationResultKind.KeptExisting, result.Kind);
        Assert.Equal(current.Id, result.Recommendation!.Id);
        Assert.Equal(0, publication.PublishInitialCalls);
        Assert.Equal(0, states.SaveCalls);
        Assert.Equal(1, publication.ConfirmKeepCalls);
    }

    [Fact]
    public async Task Publication_conflict_retries_from_a_fresh_persistence_read()
    {
        var repository = new FakeRecommendationRepository();
        var publication = new FakePublicationTransaction { ConflictsBeforeSuccess = 1 };
        var assessment = CreateAssessment();
        var service = CreateService(
            assessment,
            repository,
            new FakeStabilityStateRepository(),
            publication);

        var result = await service.CreateAsync(
            UserId.New(),
            assessment,
            T0.AddMinutes(4));

        Assert.Equal(RecommendationApplicationResultKind.Published, result.Kind);
        Assert.Equal(2, publication.PublishInitialCalls);
        Assert.Equal(2, repository.CurrentReads);
    }

    [Fact]
    public async Task Expired_active_current_is_terminalized_before_successor_publication()
    {
        var assessment = CreateAssessment();
        var current = Recommendation.Create(
            assessment,
            PositionAction.Watch,
            AddDecision.DoNotAdd,
            new RuleVersion("policy-v1"),
            [],
            T0.AddMinutes(3),
            T0.AddMinutes(4));
        var publication = new FakePublicationTransaction();
        var service = CreateService(
            assessment,
            new FakeRecommendationRepository
            {
                Current = new Versioned<Recommendation>(current, ConcurrencyVersion.Initial)
            },
            new FakeStabilityStateRepository(),
            publication);

        var result = await service.CreateAsync(UserId.New(), assessment, T0.AddMinutes(4));

        Assert.Equal(RecommendationApplicationResultKind.Published, result.Kind);
        Assert.Equal(1, publication.ReplaceCalls);
        Assert.Equal(RecommendationStatus.Expired, current.Status);
    }

    private static RecommendationService CreateService(
        PositionAssessment assessment,
        FakeRecommendationRepository repository,
        FakeStabilityStateRepository states,
        FakePublicationTransaction publication) =>
        new(
            new FixedPolicyProvider(),
            new RecommendationPolicy(),
            new RecommendationStabilityPolicy(),
            repository,
            states,
            publication,
            new FakePositionAssessmentRepository(assessment));

    private static PositionAssessment CreateAssessment()
    {
        var userAccountId = ExchangeAccountId.New();
        var position = PositionId.New();
        return PositionAssessment.Create(
            new PositionAssessmentInputVersions(
                position,
                userAccountId,
                InstrumentId.From("BTCUSDT"),
                T0,
                T0.AddMinutes(1),
                T0.AddMinutes(2)),
            new RuleVersion("assessment-v1"),
            RiskIncreasePolicyResult.Blocked([ReasonCode.PortfolioDataStale]),
            [],
            T0.AddMinutes(3),
            T0.AddHours(1));
    }

    private sealed class FixedPolicyProvider : IRecommendationPolicyDefinitionProvider
    {
        public ValueTask<PolicyDefinition> GetAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(PolicyDefinition.Default);
        }
    }

    private sealed class FakePositionAssessmentRepository(PositionAssessment assessment)
        : IPositionAssessmentRepository
    {
        public Task<PositionAssessment?> GetByIdAsync(
            UserId userId,
            PositionAssessmentId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<PositionAssessment?>(
                id == assessment.Id ? assessment : null);

        public Task SaveAsync(
            UserId userId,
            PositionAssessment value,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeRecommendationRepository : IRecommendationRepository
    {
        public Versioned<Recommendation>? Current { get; init; }
        public int CurrentReads { get; private set; }

        public Task<Versioned<Recommendation>?> GetByIdAsync(
            UserId userId,
            RecommendationId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Versioned<Recommendation>?>(null);

        public Task<Versioned<Recommendation>?> GetCurrentForPositionAsync(
            UserId userId,
            PositionId positionId,
            CancellationToken cancellationToken = default)
        {
            CurrentReads++;
            return Task.FromResult(Current);
        }

        public Task EnsureCurrentAsync(
            UserId userId,
            PositionId positionId,
            RecommendationCurrentExpectation expectation,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<ConcurrencyVersion> SaveAsync(
            UserId userId,
            Recommendation recommendation,
            ConcurrencyVersion? expectedVersion,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ConcurrencyVersion.Initial);
    }

    private sealed class FakeStabilityStateRepository : IRecommendationStabilityStateRepository
    {
        public Versioned<RecommendationStabilityStateSnapshot>? Pending { get; init; }
        public int SaveCalls { get; private set; }
        public int DeleteCalls { get; private set; }

        public Task<Versioned<RecommendationStabilityStateSnapshot>?> GetAsync(
            UserId userId,
            PositionId positionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Pending);

        public Task<ConcurrencyVersion> SaveAsync(
            UserId userId,
            PositionId positionId,
            RecommendationStabilityStateSnapshot state,
            RecommendationStabilityStateExpectation expectedState,
            CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            return Task.FromResult(ConcurrencyVersion.Initial);
        }

        public Task DeleteExpectedAsync(
            UserId userId,
            PositionId positionId,
            RecommendationStabilityStateExpectation expectedState,
            CancellationToken cancellationToken = default)
        {
            DeleteCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakePublicationTransaction : IRecommendationPublicationTransaction
    {
        public int ConflictsBeforeSuccess { get; init; }
        public int PublishInitialCalls { get; private set; }
        public int ReplaceCalls { get; private set; }
        public int ConfirmKeepCalls { get; private set; }

        public Task PublishInitialAsync(
            UserId userId,
            Recommendation successor,
            RecommendationCurrentExpectation expectedCurrent,
            RecommendationStabilityStateExpectation expectedPending,
            CancellationToken cancellationToken = default)
        {
            PublishInitialCalls++;
            if (PublishInitialCalls <= ConflictsBeforeSuccess)
                throw new ConcurrencyConflictException("test conflict");
            return Task.CompletedTask;
        }

        public Task ReplaceAsync(
            UserId userId,
            Recommendation current,
            Recommendation successor,
            RecommendationCurrentExpectation expectedCurrent,
            RecommendationStabilityStateExpectation expectedPending,
            CancellationToken cancellationToken = default) =>
            ReplaceCoreAsync();

        public Task SavePendingAsync(
            UserId userId,
            PositionId positionId,
            RecommendationCurrentExpectation expectedCurrent,
            RecommendationStabilityStateSnapshot state,
            RecommendationStabilityStateExpectation expectedPending,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ConfirmKeepExistingAsync(
            UserId userId,
            PositionId positionId,
            RecommendationCurrentExpectation expectedCurrent,
            RecommendationStabilityStateExpectation expectedPending,
            CancellationToken cancellationToken = default) =>
            ConfirmKeepCoreAsync();

        private Task ConfirmKeepCoreAsync()
        {
            ConfirmKeepCalls++;
            return Task.CompletedTask;
        }

        private Task ReplaceCoreAsync()
        {
            ReplaceCalls++;
            return Task.CompletedTask;
        }
    }
}
