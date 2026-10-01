using FluentAssertions;
using Intelligence.TradeSystem.Api.Contracts.V1.Positions;
using Intelligence.TradeSystem.Api.Mappers;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Decisions;
using Intelligence.TradeSystem.Domain.Recommendations;
using DomainPositionSide = Intelligence.TradeSystem.Domain.Snapshots.PositionSide;
using Xunit;

namespace Intelligence.TradeSystem.Api.Tests;

public sealed class PositionV1EnumMapperTests
{
    [Fact]
    public void Explicit_mappings_cover_all_supported_source_values()
    {
        AssertAllSupportedValuesMap<DomainPositionSide, PositionSideV1>(
            PositionV1EnumMapper.ToWire,
            DomainPositionSide.Unknown);
        AssertAllSupportedValuesMap<PositionTrackingState, PositionTrackingStateV1>(
            PositionV1EnumMapper.ToWire);
        AssertAllSupportedValuesMap<MarketCategory, MarketCategoryV1>(
            PositionV1EnumMapper.ToWire,
            MarketCategory.Spot);
        AssertAllSupportedValuesMap<AssessmentDataQuality, AssessmentDataQualityV1>(
            PositionV1EnumMapper.ToWire);
        AssertAllSupportedValuesMap<AssessmentSafetyState, AssessmentSafetyStateV1>(
            PositionV1EnumMapper.ToWire);
        AssertAllSupportedValuesMap<RiskIncreaseDecision, RiskIncreaseDecisionV1>(
            PositionV1EnumMapper.ToWire);
        AssertAllSupportedValuesMap<PositionAction, PositionActionV1>(PositionV1EnumMapper.ToWire);
        AssertAllSupportedValuesMap<AddDecision, AddDecisionV1>(PositionV1EnumMapper.ToWire);
        AssertAllSupportedValuesMap<RecommendationPriority, RecommendationPriorityV1>(
            PositionV1EnumMapper.ToWire);
        AssertAllSupportedValuesMap<RecommendationStatus, RecommendationStatusV1>(
            PositionV1EnumMapper.ToWire);
        AssertAllSupportedValuesMap<ReasonCode, ReasonCodeV1>(PositionV1EnumMapper.ToWire);
    }

    [Fact]
    public void Explicit_mappings_preserve_representative_v1_values()
    {
        PositionV1EnumMapper.ToWire(DomainPositionSide.Short).Should().Be(PositionSideV1.Short);
        PositionV1EnumMapper.ToWire(PositionTrackingState.Stale)
            .Should().Be(PositionTrackingStateV1.Stale);
        PositionV1EnumMapper.ToWire(MarketCategory.Inverse).Should().Be(MarketCategoryV1.Inverse);
        PositionV1EnumMapper.ToWire(AssessmentDataQuality.Uncertain)
            .Should().Be(AssessmentDataQualityV1.Uncertain);
        PositionV1EnumMapper.ToWire(AssessmentSafetyState.Blocked)
            .Should().Be(AssessmentSafetyStateV1.Blocked);
        PositionV1EnumMapper.ToWire(RiskIncreaseDecision.Blocked)
            .Should().Be(RiskIncreaseDecisionV1.Blocked);
        PositionV1EnumMapper.ToWire(PositionAction.TakePartialProfit)
            .Should().Be(PositionActionV1.TakePartialProfit);
        PositionV1EnumMapper.ToWire(AddDecision.AddAllowed).Should().Be(AddDecisionV1.AddAllowed);
        PositionV1EnumMapper.ToWire(RecommendationPriority.Critical)
            .Should().Be(RecommendationPriorityV1.Critical);
        PositionV1EnumMapper.ToWire(RecommendationStatus.Dismissed)
            .Should().Be(RecommendationStatusV1.Dismissed);
        PositionV1EnumMapper.ToWire(ReasonCode.AddAllowedWithinLimits)
            .Should().Be(ReasonCodeV1.AddAllowedWithinLimits);
    }

    [Fact]
    public void Undefined_source_values_fail_fast()
    {
        Action[] mappings =
        [
            () => PositionV1EnumMapper.ToWire(DomainPositionSide.Unknown),
            () => PositionV1EnumMapper.ToWire((DomainPositionSide)999),
            () => PositionV1EnumMapper.ToWire((PositionTrackingState)999),
            () => PositionV1EnumMapper.ToWire((MarketCategory)999),
            () => PositionV1EnumMapper.ToWire((AssessmentDataQuality)999),
            () => PositionV1EnumMapper.ToWire((AssessmentSafetyState)999),
            () => PositionV1EnumMapper.ToWire((RiskIncreaseDecision)999),
            () => PositionV1EnumMapper.ToWire((PositionAction)999),
            () => PositionV1EnumMapper.ToWire((AddDecision)999),
            () => PositionV1EnumMapper.ToWire((RecommendationPriority)999),
            () => PositionV1EnumMapper.ToWire((RecommendationStatus)999),
            () => PositionV1EnumMapper.ToWire((ReasonCode)999),
        ];

        foreach (var mapping in mappings)
        {
            mapping.Should().Throw<NotSupportedException>();
        }
    }

    private static void AssertAllSupportedValuesMap<TDomain, TWire>(
        Func<TDomain, TWire> mapping,
        params TDomain[] unsupportedValues)
        where TDomain : struct, Enum
        where TWire : struct, Enum =>
        Enum.GetValues<TDomain>()
            .Where(value => !unsupportedValues.Contains(value))
            .Should()
            .OnlyContain(value => Record.Exception(() => mapping(value)) == null);
}
