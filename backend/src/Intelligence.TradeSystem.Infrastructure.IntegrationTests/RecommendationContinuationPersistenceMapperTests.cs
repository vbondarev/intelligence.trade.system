using System.Text.Json.Nodes;
using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Decisions;
using Intelligence.TradeSystem.Domain.Recommendations;
using Intelligence.TradeSystem.Infrastructure.Persistence.Mapping;
using Xunit;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests;

public sealed class RecommendationContinuationPersistenceMapperTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 13, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Semantic_json_comparison_ignores_order_and_format_but_detects_payload_changes()
    {
        var plan = CreatePlan();
        var json = RecommendationContinuationPersistenceMapper.Serialize(plan);
        var reordered = JsonNode.Parse(json)!.AsObject();
        var schemaVersion = reordered["schemaVersion"];
        reordered.Remove("schemaVersion");
        reordered.Add("schemaVersion", schemaVersion);
        var reorderedJson = reordered.ToJsonString(new() { WriteIndented = true });
        var changedJson = json.Replace(
            "\"requiredAlignment\":\"aligned\"",
            "\"requiredAlignment\":\"flatOrUnknown\"",
            StringComparison.Ordinal);

        Assert.True(RecommendationMapper.ContinuationContextsEqual(json, reorderedJson));
        Assert.False(RecommendationMapper.ContinuationContextsEqual(json, changedJson));
    }

    [Fact]
    public void Persistence_mapper_rejects_unknown_schema_and_payload_for_discriminator()
    {
        var plan = CreatePlan();
        var json = RecommendationContinuationPersistenceMapper.Serialize(plan);
        var malformedPayload = json.Replace(
            "\"requiredAlignment\":\"aligned\"",
            "\"requiredAlignment\":\"aligned\",\"maximumPositionValue\":1",
            StringComparison.Ordinal);
        var unknownSchema = json.Replace(
            "\"schemaVersion\":2",
            "\"schemaVersion\":99",
            StringComparison.Ordinal);

        Assert.Throws<InvalidOperationException>(() =>
                RecommendationContinuationPersistenceMapper.Deserialize(
                    malformedPayload,
                    Guid.NewGuid(),
                    plan.CreatedAt,
                    plan.ValidUntil,
                    plan.NextEvaluationAt));
        Assert.Throws<InvalidOperationException>(() =>
                RecommendationContinuationPersistenceMapper.Deserialize(
                    unknownSchema,
                    Guid.NewGuid(),
                    plan.CreatedAt,
                    plan.ValidUntil,
                    plan.NextEvaluationAt));
    }

    [Fact]
    public void Higher_priority_actions_condition_round_trips_with_value_equality()
    {
        var plan = CreatePlan();
        var json = RecommendationContinuationPersistenceMapper.Serialize(plan);
        var restored = RecommendationContinuationPersistenceMapper.Deserialize(
            json,
            Guid.NewGuid(),
            plan.CreatedAt,
            plan.ValidUntil,
            plan.NextEvaluationAt);
        var original = Assert.IsType<HigherPriorityActionsCondition>(
            plan.ReevaluationConditions.Single(condition =>
                condition is HigherPriorityActionsCondition));
        var rehydrated = Assert.IsType<HigherPriorityActionsCondition>(
            restored.ReevaluationConditions.Single(condition =>
                condition is HigherPriorityActionsCondition));

        Assert.Equal(original, rehydrated);
        Assert.Equal(original.RequiredActions, rehydrated.RequiredActions);
    }

    [Theory]
    [InlineData("action")]
    [InlineData("addDecision")]
    public void Recommendation_expiry_rejects_persisted_non_recommendation_scope(
        string corruptedScope)
    {
        var plan = CreatePlan();
        var document = JsonNode.Parse(
            RecommendationContinuationPersistenceMapper.Serialize(plan))!.AsObject();
        var invalidation = document["invalidationConditions"]!.AsArray();
        var expiry = invalidation
            .OfType<JsonObject>()
            .Single(condition => condition["kind"]?.GetValue<string>() == "recommendationExpiry");
        expiry["scope"] = corruptedScope;
        var corruptedJson = document.ToJsonString();

        Assert.Throws<InvalidOperationException>(() =>
            RecommendationContinuationPersistenceMapper.Deserialize(
                corruptedJson,
                Guid.NewGuid(),
                plan.CreatedAt,
                plan.ValidUntil,
                plan.NextEvaluationAt));
    }

    private static RecommendationContinuationPlan CreatePlan()
    {
        var validUntil = T0.AddMinutes(30);
        return new RecommendationContinuationPlan(
            [
                new PolicyIdentityCondition(
                    RecommendationContinuationConditionScope.Recommendation,
                    PolicyDefinition.Default.Identity),
                new RecommendationExpiryCondition(validUntil),
                new TrendAlignmentCondition(
                    RecommendationContinuationConditionScope.Action,
                    PositionTrendAlignment.Aligned),
                new AddAllowedCapacityCondition(10m, null)
            ],
            [
                new TrendAlignmentCondition(
                    RecommendationContinuationConditionScope.Action,
                    PositionTrendAlignment.Aligned),
                new HigherPriorityActionsCondition(
                    RecommendationContinuationConditionScope.Action,
                    [PositionAction.Close, PositionAction.Reduce]),
                new LiquidationDistanceEligibilityCondition(
                    RecommendationContinuationConditionScope.AddDecision,
                    5m,
                    false),
                new AdditionalCapacityEligibilityCondition(
                    RecommendationContinuationConditionScope.AddDecision,
                    false)
            ],
            T0,
            validUntil,
            T0.AddMinutes(5));
    }
}
