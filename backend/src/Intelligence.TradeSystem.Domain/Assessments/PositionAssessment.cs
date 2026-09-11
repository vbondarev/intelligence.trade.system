using System.Collections.ObjectModel;
using Intelligence.TradeSystem.Domain.Decisions;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Portfolio;
using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Domain.Assessments;

public sealed class PositionAssessment
{
    private PositionAssessment(
        PositionAssessmentId id,
        PositionAssessmentInputVersions inputVersions,
        RuleVersion ruleVersion,
        DateTimeOffset createdAt,
        DateTimeOffset validUntil,
        RiskIncreaseDecision portfolioRiskDecision,
        IReadOnlyList<ReasonCode> reasonCodes,
        PositionAssessmentResult result)
    {
        Id = id;
        InputVersions = inputVersions;
        RuleVersion = ruleVersion;
        CreatedAt = createdAt;
        ValidUntil = validUntil;
        PortfolioRiskDecision = portfolioRiskDecision;
        ReasonCodes = reasonCodes;
        Result = result;
    }

    public PositionAssessmentId Id { get; }
    public PositionAssessmentInputVersions InputVersions { get; }
    public PositionId PositionId => InputVersions.PositionId;
    public RuleVersion RuleVersion { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset ValidUntil { get; }
    public RiskIncreaseDecision PortfolioRiskDecision { get; }
    public IReadOnlyList<ReasonCode> ReasonCodes { get; }
    public PositionAssessmentResult Result { get; }
    public PolicyConfigurationIdentity PolicyConfigurationIdentity =>
        InputVersions.PolicyConfigurationIdentity;

    public static PositionAssessment Create(
        PositionAssessmentInputVersions inputVersions,
        RuleVersion ruleVersion,
        RiskIncreasePolicyResult portfolioRiskResult,
        IEnumerable<ReasonCode> additionalReasonCodes,
        DateTimeOffset createdAt,
        DateTimeOffset validUntil)
        => Create(
            inputVersions,
            ruleVersion,
            portfolioRiskResult,
            PositionAssessmentResult.Legacy(portfolioRiskResult?.Decision ?? default),
            additionalReasonCodes,
            createdAt,
            validUntil);

    public static PositionAssessment Create(
        PositionAssessmentInputVersions inputVersions,
        RuleVersion ruleVersion,
        RiskIncreasePolicyResult portfolioRiskResult,
        PositionAssessmentResult result,
        IEnumerable<ReasonCode> additionalReasonCodes,
        DateTimeOffset createdAt,
        DateTimeOffset validUntil)
    {
        ArgumentNullException.ThrowIfNull(portfolioRiskResult);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(additionalReasonCodes);
        inputVersions.Validate();
        ValidateRuleVersion(ruleVersion);

        if (createdAt < inputVersions.PositionObservedAt ||
            createdAt < inputVersions.PortfolioCalculatedAt ||
            createdAt < inputVersions.MarketCapturedAt)
            throw new ArgumentException("CreatedAt cannot precede an input observation.", nameof(createdAt));
        if (validUntil <= createdAt)
            throw new ArgumentException("ValidUntil must be after CreatedAt.", nameof(validUntil));
        if (!Enum.IsDefined(portfolioRiskResult.Decision))
            throw new ArgumentOutOfRangeException(nameof(portfolioRiskResult));
        if (!result.IsLegacy &&
            result.PositionSide is not (PositionSide.Long or PositionSide.Short))
            throw new ArgumentException(
                "Structured assessments must contain a Long or Short position side.",
                nameof(result));
        if (result.PortfolioRisk.PolicyDecision != portfolioRiskResult.Decision)
            throw new ArgumentException(
                "Assessment result must describe the supplied portfolio risk result.",
                nameof(result));

        var additionalReasons = additionalReasonCodes.Distinct().ToArray();
        ValidateReasons(additionalReasons);
        if (additionalReasons.Any(ReasonCodeClassification.IsPortfolioRiskReason))
            throw new ArgumentException(
                "Portfolio risk reasons must come from RiskIncreasePolicyResult.", nameof(additionalReasonCodes));

        var safetyBlocked = !result.IsLegacy &&
            (result.DataQuality.Overall != AssessmentDataQuality.FreshCompleteReliable ||
             result.DataQuality.SafetyState != AssessmentSafetyState.Allowed);
        var effectiveDecision = safetyBlocked
            ? RiskIncreaseDecision.Blocked
            : portfolioRiskResult.Decision;
        var reasons = portfolioRiskResult.ReasonCodes
            .Concat(additionalReasons)
            .Where(reason => !(safetyBlocked && reason == ReasonCode.RiskWithinLimits))
            .Distinct()
            .ToList();
        if (safetyBlocked && !reasons.Any(reason => !ReasonCodeClassification.IsPortfolioRiskReason(reason)))
            reasons.Add(ReasonCode.RiskIncreaseBlockedByDataQuality);

        ValidateReasons(reasons);
        ValidatePortfolioRiskConsistency(effectiveDecision, reasons, safetyBlocked);
        if (reasons.Count == 0)
            throw new ArgumentException("At least one reason code is required.", nameof(additionalReasonCodes));

        return new(
            PositionAssessmentId.New(),
            inputVersions,
            ruleVersion,
            createdAt,
            validUntil,
            effectiveDecision,
            new ReadOnlyCollection<ReasonCode>(reasons.OrderBy(reason => (int)reason).ToArray()),
            result);
    }

    /// <summary>
    /// Восстанавливает ранее сохранённую оценку с исходным идентификатором и входными версиями.
    /// </summary>
    public static PositionAssessment Restore(
        PositionAssessmentId id,
        PositionAssessmentInputVersions inputVersions,
        RuleVersion ruleVersion,
        DateTimeOffset createdAt,
        DateTimeOffset validUntil,
        RiskIncreaseDecision portfolioRiskDecision,
        IEnumerable<ReasonCode> reasonCodes)
        => Restore(
            id,
            inputVersions,
            ruleVersion,
            createdAt,
            validUntil,
            portfolioRiskDecision,
            PositionAssessmentResult.Legacy(portfolioRiskDecision),
            reasonCodes);

    public static PositionAssessment Restore(
        PositionAssessmentId id,
        PositionAssessmentInputVersions inputVersions,
        RuleVersion ruleVersion,
        DateTimeOffset createdAt,
        DateTimeOffset validUntil,
        RiskIncreaseDecision portfolioRiskDecision,
        PositionAssessmentResult result,
        IEnumerable<ReasonCode> reasonCodes)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(reasonCodes);
        if (id == default)
            throw new ArgumentException("PositionAssessmentId must be initialized.", nameof(id));

        inputVersions.Validate();
        ValidateRuleVersion(ruleVersion);

        if (createdAt < inputVersions.PositionObservedAt ||
            createdAt < inputVersions.PortfolioCalculatedAt ||
            createdAt < inputVersions.MarketCapturedAt)
            throw new ArgumentException("CreatedAt cannot precede an input observation.", nameof(createdAt));
        if (validUntil <= createdAt)
            throw new ArgumentException("ValidUntil must be after CreatedAt.", nameof(validUntil));
        if (!Enum.IsDefined(portfolioRiskDecision))
            throw new ArgumentOutOfRangeException(nameof(portfolioRiskDecision));
        if (result.PortfolioRisk.PolicyDecision is not (RiskIncreaseDecision.Allowed or RiskIncreaseDecision.Blocked))
            throw new ArgumentOutOfRangeException(nameof(result));
        if (!result.IsLegacy &&
            result.PositionSide is not (PositionSide.Long or PositionSide.Short))
            throw new ArgumentException(
                "Structured assessments must contain a Long or Short position side.",
                nameof(result));
        var safetyBlocked = !result.IsLegacy &&
            (result.DataQuality.Overall != AssessmentDataQuality.FreshCompleteReliable ||
             result.DataQuality.SafetyState != AssessmentSafetyState.Allowed);
        var expectedDecision = safetyBlocked
            ? RiskIncreaseDecision.Blocked
            : result.PortfolioRisk.PolicyDecision;
        if (portfolioRiskDecision != expectedDecision)
            throw new ArgumentException(
                "Restored assessment decision does not match its portfolio and data-quality contexts.",
                nameof(portfolioRiskDecision));

        var reasons = reasonCodes.ToArray();
        ValidateReasons(reasons);
        if (reasons.Length == 0)
            throw new ArgumentException("At least one reason code is required.", nameof(reasonCodes));
        if (reasons.Distinct().Count() != reasons.Length)
            throw new ArgumentException("Reason codes cannot contain duplicates.", nameof(reasonCodes));
        ValidatePortfolioRiskConsistency(
            portfolioRiskDecision,
            reasons,
            safetyBlocked);

        return new(
            id,
            inputVersions,
            ruleVersion,
            createdAt,
            validUntil,
            portfolioRiskDecision,
            new ReadOnlyCollection<ReasonCode>(reasons.OrderBy(reason => (int)reason).ToArray()),
            result);
    }

    public bool IsValidAt(DateTimeOffset at) => CreatedAt <= at && at < ValidUntil;

    private static void ValidateRuleVersion(RuleVersion version)
    {
        if (string.IsNullOrWhiteSpace(version.Value))
            throw new ArgumentException("RuleVersion must be initialized.", nameof(version));
    }

    private static void ValidateReasons(IEnumerable<ReasonCode> reasons)
    {
        if (reasons.Any(reason => !Enum.IsDefined(reason)))
            throw new ArgumentOutOfRangeException(nameof(reasons), "Reason code must be defined.");
    }

    private static void ValidatePortfolioRiskConsistency(
        RiskIncreaseDecision decision,
        IEnumerable<ReasonCode> reasons,
        bool safetyBlocked)
    {
        var portfolioRiskReasons = reasons
            .Where(ReasonCodeClassification.IsPortfolioRiskReason)
            .ToArray();

        switch (decision)
        {
            case RiskIncreaseDecision.Allowed:
                if (!portfolioRiskReasons.SequenceEqual(RiskIncreasePolicyResult.Allowed().ReasonCodes))
                    throw new ArgumentException(
                        "Allowed assessments must contain exactly the RiskWithinLimits reason.");
                break;
            case RiskIncreaseDecision.Blocked:
                if (portfolioRiskReasons.Length > 0)
                    _ = RiskIncreasePolicyResult.Blocked(portfolioRiskReasons);
                else if (!safetyBlocked)
                    throw new ArgumentException(
                        "Blocked assessments must contain a portfolio risk reason.");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(decision), decision, "Risk decision must be defined.");
        }
    }
}
