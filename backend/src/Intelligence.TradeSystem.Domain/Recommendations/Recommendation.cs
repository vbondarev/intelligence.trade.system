using System.Collections.ObjectModel;
using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Decisions;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Domain.Recommendations;

public sealed class Recommendation
{
    private Recommendation(
        RecommendationId id,
        PositionAssessmentId assessmentId,
        PositionId positionId,
        PositionAction recommendedAction,
        AddDecision addDecision,
        RuleVersion policyVersion,
        IReadOnlyList<ReasonCode> reasonCodes,
        DateTimeOffset createdAt,
        DateTimeOffset validUntil,
        RecommendationStatus status = RecommendationStatus.Active,
        DateTimeOffset? acknowledgedAt = null,
        DateTimeOffset? dismissedAt = null,
        DateTimeOffset? supersededAt = null,
        DateTimeOffset? expiredAt = null,
        RecommendationId? supersededByRecommendationId = null)
    {
        Id = id;
        AssessmentId = assessmentId;
        PositionId = positionId;
        RecommendedAction = recommendedAction;
        AddDecision = addDecision;
        PolicyVersion = policyVersion;
        ReasonCodes = reasonCodes;
        CreatedAt = createdAt;
        ValidUntil = validUntil;
        Status = status;
        AcknowledgedAt = acknowledgedAt;
        DismissedAt = dismissedAt;
        SupersededAt = supersededAt;
        ExpiredAt = expiredAt;
        SupersededByRecommendationId = supersededByRecommendationId;
    }

    public RecommendationId Id { get; }
    public PositionAssessmentId AssessmentId { get; }
    public PositionId PositionId { get; }
    public PositionAction RecommendedAction { get; }
    public AddDecision AddDecision { get; }
    public RuleVersion PolicyVersion { get; }
    public IReadOnlyList<ReasonCode> ReasonCodes { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset ValidUntil { get; }
    public RecommendationStatus Status { get; private set; }
    public DateTimeOffset? AcknowledgedAt { get; private set; }
    public DateTimeOffset? DismissedAt { get; private set; }
    public DateTimeOffset? SupersededAt { get; private set; }
    public DateTimeOffset? ExpiredAt { get; private set; }
    public RecommendationId? SupersededByRecommendationId { get; private set; }

    public static Recommendation Create(
        PositionAssessment assessment,
        PositionAction recommendedAction,
        AddDecision addDecision,
        RuleVersion policyVersion,
        IEnumerable<ReasonCode> reasonCodes,
        DateTimeOffset createdAt,
        DateTimeOffset validUntil)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        ArgumentNullException.ThrowIfNull(reasonCodes);
        ValidateEnum(recommendedAction, nameof(recommendedAction));
        ValidateEnum(addDecision, nameof(addDecision));
        if (addDecision == AddDecision.AddAllowed &&
            assessment.PortfolioRiskDecision == RiskIncreaseDecision.Blocked)
            throw new InvalidOperationException("A blocked portfolio risk decision cannot produce AddAllowed.");
        if (createdAt < assessment.CreatedAt || createdAt >= assessment.ValidUntil)
            throw new ArgumentException("CreatedAt must be within the assessment validity window.", nameof(createdAt));
        if (validUntil <= createdAt)
            throw new ArgumentException("ValidUntil must be after CreatedAt.", nameof(validUntil));
        if (validUntil > assessment.ValidUntil)
            throw new ArgumentException("Recommendation cannot outlive its assessment.", nameof(validUntil));
        if (string.IsNullOrWhiteSpace(policyVersion.Value))
            throw new ArgumentException("PolicyVersion must be initialized.", nameof(policyVersion));

        var specificReasons = reasonCodes.Distinct().ToArray();
        if (specificReasons.Any(reason => !Enum.IsDefined(reason)))
            throw new ArgumentOutOfRangeException(nameof(reasonCodes), "Reason code must be defined.");
        if (specificReasons.Any(ReasonCodeClassification.IsPortfolioRiskReason))
            throw new ArgumentException(
                "Portfolio risk reasons must be inherited from the assessment.", nameof(reasonCodes));

        var reasons = assessment.ReasonCodes
            .Where(ReasonCodeClassification.IsPortfolioRiskReason)
            .Concat(specificReasons)
            .Distinct()
            .ToArray();
        if (reasons.Length == 0)
            throw new ArgumentException("At least one reason code is required.", nameof(reasonCodes));

        return new(
            RecommendationId.New(), assessment.Id, assessment.PositionId, recommendedAction, addDecision,
            policyVersion, new ReadOnlyCollection<ReasonCode>(reasons), createdAt, validUntil);
    }

    /// <summary>
    /// Восстанавливает рекомендацию с сохранённым идентификатором и текущим состоянием lifecycle.
    /// Переходы состояния при восстановлении не выполняются.
    /// </summary>
    public static Recommendation Restore(
        RecommendationId id,
        PositionAssessmentId assessmentId,
        PositionId positionId,
        PositionAction recommendedAction,
        AddDecision addDecision,
        RuleVersion policyVersion,
        IEnumerable<ReasonCode> reasonCodes,
        DateTimeOffset createdAt,
        DateTimeOffset validUntil,
        RecommendationStatus status,
        DateTimeOffset? acknowledgedAt,
        DateTimeOffset? dismissedAt,
        DateTimeOffset? supersededAt,
        DateTimeOffset? expiredAt,
        RecommendationId? supersededByRecommendationId,
        PositionId assessmentPositionId,
        RiskIncreaseDecision assessmentPortfolioRiskDecision,
        DateTimeOffset assessmentCreatedAt,
        DateTimeOffset assessmentValidUntil)
    {
        ArgumentNullException.ThrowIfNull(reasonCodes);
        if (id == default)
            throw new ArgumentException("RecommendationId must be initialized.", nameof(id));
        if (assessmentId == default)
            throw new ArgumentException("PositionAssessmentId must be initialized.", nameof(assessmentId));
        if (positionId == default)
            throw new ArgumentException("PositionId must be initialized.", nameof(positionId));
        if (assessmentPositionId != positionId)
            throw new ArgumentException("Recommendation position must match its assessment.", nameof(positionId));

        ValidateEnum(recommendedAction, nameof(recommendedAction));
        ValidateEnum(addDecision, nameof(addDecision));
        ValidateEnum(status, nameof(status));
        ValidateEnum(assessmentPortfolioRiskDecision, nameof(assessmentPortfolioRiskDecision));
        if (addDecision == AddDecision.AddAllowed &&
            assessmentPortfolioRiskDecision == RiskIncreaseDecision.Blocked)
            throw new InvalidOperationException("A blocked portfolio risk decision cannot produce AddAllowed.");
        if (string.IsNullOrWhiteSpace(policyVersion.Value))
            throw new ArgumentException("PolicyVersion must be initialized.", nameof(policyVersion));
        if (createdAt < assessmentCreatedAt || createdAt >= assessmentValidUntil)
            throw new ArgumentException("CreatedAt must be within the assessment validity window.", nameof(createdAt));
        if (validUntil <= createdAt || validUntil > assessmentValidUntil)
            throw new ArgumentException("Recommendation validity window is invalid.", nameof(validUntil));

        var reasons = reasonCodes.ToArray();
        if (reasons.Length == 0)
            throw new ArgumentException("At least one reason code is required.", nameof(reasonCodes));
        if (reasons.Any(reason => !Enum.IsDefined(reason)))
            throw new ArgumentOutOfRangeException(nameof(reasonCodes), "Reason code must be defined.");
        if (reasons.Distinct().Count() != reasons.Length)
            throw new ArgumentException("Reason codes cannot contain duplicates.", nameof(reasonCodes));

        ValidateLifecycle(
            id,
            createdAt,
            validUntil,
            status,
            acknowledgedAt,
            dismissedAt,
            supersededAt,
            expiredAt,
            supersededByRecommendationId);

        return new(
            id,
            assessmentId,
            positionId,
            recommendedAction,
            addDecision,
            policyVersion,
            new ReadOnlyCollection<ReasonCode>(reasons),
            createdAt,
            validUntil,
            status,
            acknowledgedAt,
            dismissedAt,
            supersededAt,
            expiredAt,
            supersededByRecommendationId);
    }

    public void Acknowledge(DateTimeOffset at)
    {
        if (Status == RecommendationStatus.Acknowledged)
            return;
        EnsureNotPastValidity(at, "Acknowledge");
        EnsureStatus(RecommendationStatus.Active, "Acknowledge");
        Status = RecommendationStatus.Acknowledged;
        AcknowledgedAt = at;
    }

    public void Dismiss(DateTimeOffset at)
    {
        if (Status == RecommendationStatus.Dismissed)
            return;
        EnsureNotPastValidity(at, "Dismiss");
        EnsureStatus("Dismiss", RecommendationStatus.Active, RecommendationStatus.Acknowledged);
        if (AcknowledgedAt.HasValue && at < AcknowledgedAt.Value)
            throw new InvalidOperationException("DismissedAt cannot precede AcknowledgedAt.");
        Status = RecommendationStatus.Dismissed;
        DismissedAt = at;
    }

    public void SupersedeBy(Recommendation successor)
    {
        ArgumentNullException.ThrowIfNull(successor);
        if (SupersededByRecommendationId == successor.Id)
            return;
        EnsureStatus("Supersede", RecommendationStatus.Active, RecommendationStatus.Acknowledged);
        if (successor.Id == Id || successor.PositionId != PositionId ||
            successor.CreatedAt <= CreatedAt || successor.CreatedAt >= ValidUntil ||
            (AcknowledgedAt.HasValue && successor.CreatedAt < AcknowledgedAt.Value))
            throw new InvalidOperationException("Successor must be newer, same-position, and within validity.");
        Status = RecommendationStatus.Superseded;
        SupersededAt = successor.CreatedAt;
        SupersededByRecommendationId = successor.Id;
    }

    public void ExpireIfDue(DateTimeOffset now)
    {
        if (now < ValidUntil || Status is RecommendationStatus.Dismissed or RecommendationStatus.Superseded or RecommendationStatus.Expired)
            return;
        Status = RecommendationStatus.Expired;
        ExpiredAt = now;
    }

    /// <summary>
    /// Determines effectiveness in the current lifecycle state at a point in time.
    /// This is not a historical lifecycle reconstruction query.
    /// </summary>
    public bool IsEffectiveAt(DateTimeOffset at) =>
        Status is RecommendationStatus.Active or RecommendationStatus.Acknowledged &&
        CreatedAt <= at && at < ValidUntil;

    private void EnsureNotPastValidity(DateTimeOffset at, string operation)
    {
        if (at >= ValidUntil)
            throw new InvalidOperationException($"{operation} cannot occur at or after ValidUntil.");
        if (at < CreatedAt)
            throw new InvalidOperationException($"{operation} cannot occur before CreatedAt.");
    }

    private void EnsureStatus(string operation, params RecommendationStatus[] allowed)
    {
        if (!allowed.Contains(Status))
            throw new InvalidOperationException($"{operation} cannot transition recommendation from {Status}.");
    }

    private void EnsureStatus(RecommendationStatus expected, string operation) => EnsureStatus(operation, expected);

    private static void ValidateEnum<T>(T value, string name) where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
            throw new ArgumentOutOfRangeException(name, value, "Value must be defined.");
    }

    private static void ValidateLifecycle(
        RecommendationId id,
        DateTimeOffset createdAt,
        DateTimeOffset validUntil,
        RecommendationStatus status,
        DateTimeOffset? acknowledgedAt,
        DateTimeOffset? dismissedAt,
        DateTimeOffset? supersededAt,
        DateTimeOffset? expiredAt,
        RecommendationId? supersededByRecommendationId)
    {
        if (acknowledgedAt is { } acknowledged &&
            (acknowledged < createdAt || acknowledged >= validUntil))
            throw new ArgumentException("AcknowledgedAt must be within the recommendation validity window.");

        if (dismissedAt is { } dismissed &&
            (dismissed < createdAt || dismissed >= validUntil ||
             acknowledgedAt.HasValue && dismissed < acknowledgedAt.Value))
            throw new ArgumentException("DismissedAt is invalid.");

        if (supersededAt is { } superseded &&
            (superseded <= createdAt || superseded >= validUntil))
            throw new ArgumentException("SupersededAt must be within the recommendation validity window.");

        if (expiredAt is { } expired && expired < validUntil)
            throw new ArgumentException("ExpiredAt cannot precede ValidUntil.");

        switch (status)
        {
            case RecommendationStatus.Active when acknowledgedAt is not null ||
                dismissedAt is not null || supersededAt is not null || expiredAt is not null ||
                supersededByRecommendationId is not null:
                throw new ArgumentException("Active recommendation cannot have lifecycle transition values.");
            case RecommendationStatus.Acknowledged when acknowledgedAt is null ||
                dismissedAt is not null || supersededAt is not null || expiredAt is not null ||
                supersededByRecommendationId is not null:
                throw new ArgumentException("Acknowledged recommendation has invalid lifecycle values.");
            case RecommendationStatus.Dismissed when dismissedAt is null ||
                supersededAt is not null || expiredAt is not null || supersededByRecommendationId is not null:
                throw new ArgumentException("Dismissed recommendation has invalid lifecycle values.");
            case RecommendationStatus.Superseded when supersededAt is null ||
                supersededByRecommendationId is null || dismissedAt is not null || expiredAt is not null:
                throw new ArgumentException("Superseded recommendation has invalid lifecycle values.");
            case RecommendationStatus.Expired when expiredAt is null ||
                dismissedAt is not null || supersededAt is not null || supersededByRecommendationId is not null:
                throw new ArgumentException("Expired recommendation has invalid lifecycle values.");
        }

        if (supersededByRecommendationId is { } successor && successor == id)
            throw new ArgumentException("A recommendation cannot supersede itself.", nameof(supersededByRecommendationId));
    }
}
