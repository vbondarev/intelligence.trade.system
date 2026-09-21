using System.Text.Json;
using Intelligence.TradeSystem.Api.Contracts.V1.Positions;
using Intelligence.TradeSystem.Api.Serialization;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Decisions;
using Intelligence.TradeSystem.Domain.Recommendations;
using Xunit;
using DomainAddDecision = Intelligence.TradeSystem.Domain.Decisions.AddDecision;
using DomainAssessmentDataQuality = Intelligence.TradeSystem.Domain.Assessments.AssessmentDataQuality;
using DomainAssessmentLiquidationState = Intelligence.TradeSystem.Domain.Assessments.AssessmentLiquidationState;
using DomainAssessmentMomentumState = Intelligence.TradeSystem.Domain.Assessments.AssessmentMomentumState;
using DomainAssessmentPricePosition = Intelligence.TradeSystem.Domain.Assessments.AssessmentPricePosition;
using DomainAssessmentSafetyState = Intelligence.TradeSystem.Domain.Assessments.AssessmentSafetyState;
using DomainAssessmentStopState = Intelligence.TradeSystem.Domain.Assessments.AssessmentStopState;
using DomainAssessmentTrendDirection = Intelligence.TradeSystem.Domain.Assessments.AssessmentTrendDirection;
using DomainPositionAction = Intelligence.TradeSystem.Domain.Decisions.PositionAction;
using DomainPositionTrendAlignment = Intelligence.TradeSystem.Domain.Assessments.PositionTrendAlignment;
using DomainRecommendationConditionKind =
    Intelligence.TradeSystem.Domain.Recommendations.RecommendationContinuationConditionKind;
using DomainRecommendationConditionScope =
    Intelligence.TradeSystem.Domain.Recommendations.RecommendationContinuationConditionScope;
using DomainRecommendationPriority = Intelligence.TradeSystem.Domain.Recommendations.RecommendationPriority;
using DomainRecommendationStatus = Intelligence.TradeSystem.Domain.Recommendations.RecommendationStatus;
using DomainRiskIncreaseDecision = Intelligence.TradeSystem.Domain.Decisions.RiskIncreaseDecision;

namespace Intelligence.TradeSystem.Api.Tests;

public sealed class PositionEvaluationJsonContractTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Nullable_evaluation_fields_are_serialized_as_explicit_json_null()
    {
        var response = new PositionEvaluationResponse(
            Guid.NewGuid(),
            new PositionAssessmentResponse(
                Guid.NewGuid(),
                T0,
                T0.AddMinutes(5),
                "assessment-v1",
                false,
                new(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    "BTCUSDT",
                    T0.AddMinutes(-2),
                    T0.AddMinutes(-1),
                    T0),
                new("policy-v1", "sha256:policy"),
                new("policy-v1", "sha256:effective"),
                RiskIncreaseDecisionV1.Blocked,
                [],
                new(
                    AssessmentDataQualityV1.Stale,
                    AssessmentDataQualityV1.FreshCompleteReliable,
                    AssessmentDataQualityV1.Stale,
                    AssessmentSafetyStateV1.Blocked),
                null),
            new(
                Guid.NewGuid(),
                Guid.NewGuid(),
                T0,
                T0.AddMinutes(5),
                RecommendationStatusV1.Active,
                new("policy-v1", "sha256:policy"),
                new(
                    PositionActionV1.Watch,
                    null,
                    null,
                    []),
                new(
                    AddDecisionV1.DoNotAdd,
                    [],
                    null,
                    null,
                    null),
                [],
                null));

        using var document = JsonDocument.Parse(
            JsonSerializer.Serialize(response, V1JsonSerializerOptions.Default));
        var root = document.RootElement;

        Assert.Equal(JsonValueKind.Null, root.GetProperty("assessment").GetProperty("result").ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            root.GetProperty("recommendation").GetProperty("action").GetProperty("confidence").ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            root.GetProperty("recommendation").GetProperty("action").GetProperty("priority").ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            root.GetProperty("recommendation")
                .GetProperty("addDecision")
                .GetProperty("maximumAdditionalPositionValue")
                .ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            root.GetProperty("recommendation")
                .GetProperty("addDecision")
                .GetProperty("maximumAdditionalQuantity")
                .ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            root.GetProperty("recommendation").GetProperty("addDecision").GetProperty("conditions").ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            root.GetProperty("recommendation").GetProperty("continuation").ValueKind);
    }

    [Fact]
    public void Non_null_priority_is_serialized_as_the_v1_string_enum_value()
    {
        var action = new PositionRecommendationActionResponse(
            PositionActionV1.Watch,
            null,
            RecommendationPriorityV1.High,
            []);

        using var document = JsonDocument.Parse(
            JsonSerializer.Serialize(action, V1JsonSerializerOptions.Default));
        var priority = document.RootElement.GetProperty("priority");

        Assert.Equal(JsonValueKind.String, priority.ValueKind);
        Assert.Equal("high", priority.GetString());
    }

    [Fact]
    public void Timeline_envelope_serializes_exactly_one_typed_payload_and_explicit_nulls()
    {
        var response = new PositionTimelineItemResponse(
            PositionTimelineItemTypeV1.Evaluation,
            T0,
            null,
            new(
                Guid.NewGuid(),
                T0,
                T0.AddMinutes(5),
                "assessment-v1",
                false,
                new(
                    AssessmentDataQualityV1.FreshCompleteReliable,
                    AssessmentDataQualityV1.FreshCompleteReliable,
                    AssessmentDataQualityV1.FreshCompleteReliable,
                    AssessmentSafetyStateV1.Allowed),
                RiskIncreaseDecisionV1.Allowed,
                []),
            null);

        using var document = JsonDocument.Parse(
            JsonSerializer.Serialize(response, V1JsonSerializerOptions.Default));
        var root = document.RootElement;

        root.GetProperty("type").GetString().Should().Be("evaluation");
        root.GetProperty("positionChange").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("evaluation").ValueKind.Should().Be(JsonValueKind.Object);
        root.GetProperty("recommendation").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void Domain_reason_codes_and_evaluation_enums_have_explicit_v1_members()
    {
        AssertSameMembers<ReasonCode, ReasonCodeV1>();
        AssertSameMembers<DomainRiskIncreaseDecision, RiskIncreaseDecisionV1>();
        AssertSameMembers<DomainAssessmentDataQuality, AssessmentDataQualityV1>();
        AssertSameMembers<DomainAssessmentSafetyState, AssessmentSafetyStateV1>();
        AssertSameMembers<DomainAssessmentTrendDirection, AssessmentTrendDirectionV1>();
        AssertSameMembers<DomainPositionTrendAlignment, PositionTrendAlignmentV1>();
        AssertSameMembers<DomainAssessmentMomentumState, AssessmentMomentumStateV1>();
        AssertSameMembers<DomainAssessmentPricePosition, AssessmentPricePositionV1>();
        AssertSameMembers<DomainAssessmentStopState, AssessmentStopStateV1>();
        AssertSameMembers<DomainAssessmentLiquidationState, AssessmentLiquidationStateV1>();
        AssertSameMembers<DomainPositionAction, PositionActionV1>();
        AssertSameMembers<DomainAddDecision, AddDecisionV1>();
        AssertSameMembers<DomainRecommendationPriority, RecommendationPriorityV1>();
        AssertSameMembers<DomainRecommendationStatus, RecommendationStatusV1>();
        AssertSameMembers<DomainRecommendationConditionScope, RecommendationConditionScopeV1>();
        AssertSameMembers<DomainRecommendationConditionKind, RecommendationConditionKindV1>();
        AssertSameMembers<PositionChangeKind, PositionChangeKindV1>();
        AssertSameMembers<PositionChangeCause, PositionChangeCauseV1>();
    }

    private static void AssertSameMembers<TDomain, TV1>()
        where TDomain : struct, Enum
        where TV1 : struct, Enum =>
        Assert.Equal(
            Enum.GetNames<TDomain>(),
            Enum.GetNames<TV1>());
}
