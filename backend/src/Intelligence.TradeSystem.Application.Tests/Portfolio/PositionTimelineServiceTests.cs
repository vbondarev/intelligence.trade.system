using Intelligence.TradeSystem.Application.Portfolio.Timeline;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Decisions;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Recommendations;
using Moq;

namespace Intelligence.TradeSystem.Application.Tests.Portfolio;

public sealed class PositionTimelineServiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Query_rejects_invalid_inputs_and_cursor_incompatible_with_selected_kinds()
    {
        var positionId = PositionId.New();

        Action emptyKinds = () => { _ = new PositionTimelineQuery(positionId, 10, [], null); };
        Action invalidPageSize = () =>
        {
            _ = new PositionTimelineQuery(
                positionId,
                PositionTimelineQuery.MaxPageSize + 1,
                [PositionTimelineItemKind.Evaluation],
                null);
        };
        Action incompatibleCursor = () =>
        {
            _ = new PositionTimelineQuery(
                positionId,
                10,
                [PositionTimelineItemKind.Evaluation],
                new PositionTimelineCursor(T0, PositionTimelineItemKind.PositionChange, null, 1));
        };

        emptyKinds.Should().Throw<ArgumentException>();
        invalidPageSize.Should().Throw<ArgumentOutOfRangeException>();
        incompatibleCursor.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task Service_returns_missing_or_foreign_position_as_null_and_forwards_cancellation()
    {
        var userId = UserId.New();
        var query = CreateQuery(pageSize: 10);
        using var cancellation = new CancellationTokenSource();
        var store = new Mock<IPositionTimelineReadStore>(MockBehavior.Strict);
        store.Setup(store => store.ReadCandidatesAsync(userId, query, cancellation.Token))
            .ReturnsAsync((PositionTimelineCandidates?)null);
        var service = new PositionTimelineService(store.Object);

        var result = await service.GetAsync(userId, query, cancellation.Token);

        result.Should().BeNull();
        store.VerifyAll();
    }

    [Fact]
    public async Task Service_merges_sources_using_global_order_and_emits_the_page_boundary_cursor()
    {
        var userId = UserId.New();
        var recommendationId = RecommendationId.FromGuid(Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"));
        var evaluationId = PositionAssessmentId.FromGuid(Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"));
        var candidates = new PositionTimelineCandidates(
            [CreateChange(T0, 9)],
            [CreateEvaluation(T0, evaluationId)],
            [CreateRecommendation(T0, recommendationId)]);
        var query = CreateQuery(pageSize: 2);
        var store = new Mock<IPositionTimelineReadStore>(MockBehavior.Strict);
        store.Setup(store => store.ReadCandidatesAsync(userId, query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidates);
        var service = new PositionTimelineService(store.Object);

        var page = await service.GetAsync(userId, query);

        page.Should().NotBeNull();
        page!.HasMore.Should().BeTrue();
        page.Items.Select(item => item.Kind).Should().Equal(
            PositionTimelineItemKind.Recommendation,
            PositionTimelineItemKind.Evaluation);
        page.NextCursor.Should().Be(new PositionTimelineCursor(
            T0,
            PositionTimelineItemKind.Evaluation,
            evaluationId.Value,
            null));
    }

    [Fact]
    public async Task Service_orders_equal_source_timestamps_by_descending_source_identity()
    {
        var first = RecommendationId.FromGuid(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var second = RecommendationId.FromGuid(Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"));
        var userId = UserId.New();
        var query = CreateQuery(pageSize: 10, [PositionTimelineItemKind.Recommendation]);
        var candidates = new PositionTimelineCandidates(
            [],
            [],
            [CreateRecommendation(T0, first), CreateRecommendation(T0, second)]);
        var store = new Mock<IPositionTimelineReadStore>(MockBehavior.Strict);
        store.Setup(store => store.ReadCandidatesAsync(userId, query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidates);

        var page = await new PositionTimelineService(store.Object).GetAsync(userId, query);

        page!.Items.Select(item => item.Recommendation!.Id)
            .Should().Equal(second, first);
        page.HasMore.Should().BeFalse();
        page.NextCursor.Should().BeNull();
    }

    private static PositionTimelineQuery CreateQuery(
        int pageSize,
        IEnumerable<PositionTimelineItemKind>? kinds = null) =>
        new(
            PositionId.New(),
            pageSize,
            kinds ??
            [
                PositionTimelineItemKind.PositionChange,
                PositionTimelineItemKind.Evaluation,
                PositionTimelineItemKind.Recommendation,
            ],
            null);

    private static PositionTimelineItem CreateChange(DateTimeOffset occurredAt, int sequence) =>
        PositionTimelineItem.ForPositionChange(
            occurredAt,
            new PositionTimelinePositionChange(
                sequence,
                PositionChangeKind.Updated,
                PositionChangeCause.ExchangeObservation,
                PositionTrackingState.Active,
                null,
                new PositionTimelinePositionSnapshot(
                    1m,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null)));

    private static PositionTimelineItem CreateEvaluation(
        DateTimeOffset evaluatedAt,
        PositionAssessmentId id) =>
        PositionTimelineItem.ForEvaluation(
            new PositionTimelineEvaluation(
                id,
                evaluatedAt,
                evaluatedAt.AddHours(1),
                new RuleVersion("assessment-v1"),
                false,
                new PositionAssessmentDataQualityContext(
                    AssessmentDataQuality.FreshCompleteReliable,
                    AssessmentDataQuality.FreshCompleteReliable),
                RiskIncreaseDecision.Allowed,
                []));

    private static PositionTimelineItem CreateRecommendation(
        DateTimeOffset createdAt,
        RecommendationId id) =>
        PositionTimelineItem.ForRecommendation(
            new PositionTimelineRecommendation(
                id,
                PositionAssessmentId.New(),
                createdAt,
                createdAt.AddHours(1),
                RecommendationStatus.Active,
                PositionAction.Watch,
                null,
                null,
                AddDecision.DoNotAdd,
                [],
                true));
}
