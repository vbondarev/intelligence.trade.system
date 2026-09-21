using System.Data.Common;
using Intelligence.TradeSystem.Application.Portfolio.Timeline;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Decisions;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Recommendations;
using Intelligence.TradeSystem.Domain.Snapshots;
using Intelligence.TradeSystem.Infrastructure.Persistence;
using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;
using Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests;

[Collection("PostgreSql")]
public sealed class PositionTimelineReadPostgreSqlTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly string[] TimelineSourceTables =
        ["position_changes", "position_assessments", "recommendations"];

    [Fact]
    public async Task Timeline_is_owner_scoped_orders_ties_filters_sources_and_pages_bounded_candidates()
    {
        var owner = UserId.New();
        var foreign = UserId.New();
        var positionId = PositionId.New();
        await using (var setup = await CreateMigratedContext())
        {
            await SeedAsync(setup, owner, foreign, positionId);
        }

        var interceptor = new CommandCaptureInterceptor();
        await using var context = await CreateMigratedContext(interceptor);
        var repository = new PositionTimelineReadRepository(context);
        var query = CreateQuery(positionId, 1);

        var candidates = await repository.ReadCandidatesAsync(owner, query);

        Assert.NotNull(candidates);
        Assert.Equal(2, candidates!.PositionChanges.Count);
        Assert.Single(candidates.Evaluations);
        Assert.Single(candidates.Recommendations);
        Assert.Equal(
            [ReasonCode.PortfolioDataStale, ReasonCode.RiskIncreaseBlockedByDataQuality],
            candidates.Evaluations[0].Evaluation!.ReasonCodes);
        Assert.True(candidates.Evaluations[0].Evaluation!.IsLegacy);
        Assert.Equal(
            AssessmentSafetyState.NotEvaluated,
            candidates.Evaluations[0].Evaluation!.DataQuality.SafetyState);
        Assert.Equal(
            [ReasonCode.ProfitProtectionNeeded, ReasonCode.RiskIncreaseBlockedByDataQuality],
            candidates.Recommendations[0].Recommendation!.ReasonCodes);
        Assert.True(candidates.Recommendations[0].Recommendation!.IsLegacy);
        Assert.Equal(1m, candidates.PositionChanges[0].PositionChange!.Before!.Size);
        Assert.Equal(0m, candidates.PositionChanges[0].PositionChange!.After.Size);
        Assert.All(
            TimelineSourceTables,
            table => Assert.Contains(
                interceptor.Commands,
                command =>
                    command.Contains(table, StringComparison.OrdinalIgnoreCase) &&
                    command.Contains("LIMIT", StringComparison.OrdinalIgnoreCase)));

        var service = new PositionTimelineService(repository);
        var first = await service.GetAsync(owner, query);
        var second = await service.GetAsync(
            owner,
            CreateQuery(positionId, 1, first!.NextCursor));
        var third = await service.GetAsync(
            owner,
            CreateQuery(positionId, 1, second!.NextCursor));
        var fourth = await service.GetAsync(
            owner,
            CreateQuery(positionId, 1, third!.NextCursor));

        Assert.Equal(PositionTimelineItemKind.Recommendation, first.Items.Single().Kind);
        Assert.Equal(PositionTimelineItemKind.Evaluation, second.Items.Single().Kind);
        Assert.Equal(PositionTimelineItemKind.PositionChange, third.Items.Single().Kind);
        Assert.Equal(PositionTimelineItemKind.PositionChange, fourth!.Items.Single().Kind);
        Assert.False(fourth.HasMore);
        Assert.Null(fourth.NextCursor);

        var recommendationOnly = await repository.ReadCandidatesAsync(
            owner,
            CreateQuery(positionId, 10, null, [PositionTimelineItemKind.Recommendation]));
        Assert.NotNull(recommendationOnly);
        Assert.Empty(recommendationOnly!.PositionChanges);
        Assert.Empty(recommendationOnly.Evaluations);
        Assert.Single(recommendationOnly.Recommendations);

        Assert.Null(await repository.ReadCandidatesAsync(foreign, query));

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => repository.ReadCandidatesAsync(owner, query, cancellation.Token));
    }

    private async Task<TradeSystemDbContext> CreateMigratedContext(
        DbCommandInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<TradeSystemDbContext>()
            .UseNpgsql(
                fixture.ConnectionString,
                npgsqlOptions => npgsqlOptions.MigrationsAssembly(
                    typeof(TradeSystemDbContext).Assembly.GetName().Name));
        if (interceptor is not null)
            options.AddInterceptors(interceptor);

        var context = new TradeSystemDbContext(options.Options);
        await context.Database.MigrateAsync();
        return context;
    }

    private static PositionTimelineQuery CreateQuery(
        PositionId positionId,
        int pageSize,
        PositionTimelineCursor? cursor = null,
        IEnumerable<PositionTimelineItemKind>? kinds = null) =>
        new(
            positionId,
            pageSize,
            kinds ??
            [
                PositionTimelineItemKind.PositionChange,
                PositionTimelineItemKind.Evaluation,
                PositionTimelineItemKind.Recommendation,
            ],
            cursor);

    private static async Task SeedAsync(
        TradeSystemDbContext context,
        UserId owner,
        UserId foreign,
        PositionId positionId)
    {
        var accountId = ExchangeAccountId.New();
        var foreignAccountId = ExchangeAccountId.New();
        context.ExchangeAccounts.AddRange(
            CreateAccount(accountId, owner, "owner"),
            CreateAccount(foreignAccountId, foreign, "foreign"));
        context.Positions.AddRange(
            CreatePosition(positionId, accountId),
            CreatePosition(PositionId.New(), foreignAccountId));

        var assessmentId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        context.PositionAssessments.Add(new PositionAssessmentEntity
        {
            Id = assessmentId,
            PositionId = positionId.Value,
            ExchangeAccountId = accountId.Value,
            InstrumentId = "BTCUSDT",
            PositionObservedAt = T0,
            PortfolioCalculatedAt = T0,
            MarketCapturedAt = T0,
            RuleVersion = "assessment-v1",
            BasePolicyConfigurationVersion = "base-v1",
            BasePolicyConfigurationHash = "base-hash",
            PolicyConfigurationVersion = "policy-v1",
            PolicyConfigurationHash = "policy-hash",
            ResultJson = null,
            CreatedAt = T0,
            ValidUntil = T0.AddHours(1),
            PortfolioRiskDecision = RiskIncreaseDecision.Blocked,
        });
        context.PositionAssessmentReasons.AddRange(
            new PositionAssessmentReasonEntity
            {
                PositionAssessmentId = assessmentId,
                Sequence = 2,
                ReasonCode = ReasonCode.RiskIncreaseBlockedByDataQuality,
            },
            new PositionAssessmentReasonEntity
            {
                PositionAssessmentId = assessmentId,
                Sequence = 1,
                ReasonCode = ReasonCode.PortfolioDataStale,
            });

        var recommendationId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        context.Recommendations.Add(new RecommendationEntity
        {
            Id = recommendationId,
            AssessmentId = assessmentId,
            PositionId = positionId.Value,
            RecommendedAction = PositionAction.Watch,
            AddDecision = AddDecision.DoNotAdd,
            PolicyVersion = "recommendation-v1",
            CreatedAt = T0,
            ValidUntil = T0.AddHours(1),
            Status = RecommendationStatus.Active,
            Version = 1,
        });
        context.RecommendationReasons.AddRange(
            new RecommendationReasonEntity
            {
                RecommendationId = recommendationId,
                Sequence = 2,
                ReasonCode = ReasonCode.RiskIncreaseBlockedByDataQuality,
            },
            new RecommendationReasonEntity
            {
                RecommendationId = recommendationId,
                Sequence = 1,
                ReasonCode = ReasonCode.ProfitProtectionNeeded,
            });
        context.PositionChanges.AddRange(
            CreateChange(positionId, 1),
            CreateChange(positionId, 2));

        await context.SaveChangesAsync();
    }

    private static ExchangeAccountEntity CreateAccount(
        ExchangeAccountId id,
        UserId userId,
        string providerAccountId) =>
        new()
        {
            Id = id.Value,
            UserId = userId.Value,
            ExchangeId = ExchangeId.Bybit,
            ProviderAccountId = providerAccountId,
            ConnectionStatus = ExchangeAccountConnectionStatus.Connected,
            Capabilities = ExchangeAccountCapabilities.ReadPositions,
            Version = 1,
        };

    private static PositionEntity CreatePosition(PositionId id, ExchangeAccountId accountId) =>
        new()
        {
            Id = id.Value,
            ExchangeAccountId = accountId.Value,
            InstrumentId = "BTCUSDT",
            PositionSide = PositionSide.Long,
            PositionIdx = 0,
            MarketCategory = MarketCategory.Linear,
            Size = 1m,
            FirstDetectedAt = T0,
            LastObservedAt = T0,
            ClosedAt = T0,
            TrackingState = PositionTrackingState.Closed,
            Version = 1,
        };

    private static PositionChangeEntity CreateChange(PositionId positionId, int sequence) =>
        new()
        {
            PositionId = positionId.Value,
            Sequence = sequence,
            Kind = PositionChangeKind.Closed,
            Cause = PositionChangeCause.ExchangeObservation,
            OccurredAt = T0,
            TrackingStateAfter = PositionTrackingState.Closed,
            BeforeSize = 1m,
            AfterSize = 0m,
        };

    private sealed class CommandCaptureInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
