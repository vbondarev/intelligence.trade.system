using System.Collections.ObjectModel;
using Intelligence.TradeSystem.Domain.Decisions;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Portfolio;

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
        IReadOnlyList<ReasonCode> reasonCodes)
    {
        Id = id;
        InputVersions = inputVersions;
        RuleVersion = ruleVersion;
        CreatedAt = createdAt;
        ValidUntil = validUntil;
        PortfolioRiskDecision = portfolioRiskDecision;
        ReasonCodes = reasonCodes;
    }

    public PositionAssessmentId Id { get; }
    public PositionAssessmentInputVersions InputVersions { get; }
    public PositionId PositionId => InputVersions.PositionId;
    public RuleVersion RuleVersion { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset ValidUntil { get; }
    public RiskIncreaseDecision PortfolioRiskDecision { get; }
    public IReadOnlyList<ReasonCode> ReasonCodes { get; }

    public static PositionAssessment Create(
        PositionAssessmentInputVersions inputVersions,
        RuleVersion ruleVersion,
        RiskIncreasePolicyResult portfolioRiskResult,
        IEnumerable<ReasonCode> additionalReasonCodes,
        DateTimeOffset createdAt,
        DateTimeOffset validUntil)
    {
        ArgumentNullException.ThrowIfNull(portfolioRiskResult);
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

        var additionalReasons = additionalReasonCodes.Distinct().ToArray();
        ValidateReasons(additionalReasons);
        if (additionalReasons.Any(ReasonCodeClassification.IsPortfolioRiskReason))
            throw new ArgumentException(
                "Portfolio risk reasons must come from RiskIncreasePolicyResult.", nameof(additionalReasonCodes));

        var reasons = portfolioRiskResult.ReasonCodes.Concat(additionalReasons).Distinct().ToArray();
        ValidateReasons(reasons);
        if (reasons.Length == 0)
            throw new ArgumentException("At least one reason code is required.", nameof(additionalReasonCodes));

        return new(
            PositionAssessmentId.New(),
            inputVersions,
            ruleVersion,
            createdAt,
            validUntil,
            portfolioRiskResult.Decision,
            new ReadOnlyCollection<ReasonCode>(reasons));
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
    {
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

        var reasons = reasonCodes.ToArray();
        ValidateReasons(reasons);
        if (reasons.Length == 0)
            throw new ArgumentException("At least one reason code is required.", nameof(reasonCodes));
        if (reasons.Distinct().Count() != reasons.Length)
            throw new ArgumentException("Reason codes cannot contain duplicates.", nameof(reasonCodes));

        return new(
            id,
            inputVersions,
            ruleVersion,
            createdAt,
            validUntil,
            portfolioRiskDecision,
            new ReadOnlyCollection<ReasonCode>(reasons));
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
}
