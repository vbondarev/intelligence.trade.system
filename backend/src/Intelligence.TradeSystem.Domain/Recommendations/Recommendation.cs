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
        RecommendedActionDecision action,
        AddDecisionResult addDecision,
        PolicyConfigurationIdentity policyIdentity,
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
        ActionDecision = action;
        AddDecisionResult = addDecision;
        PolicyIdentity = policyIdentity;
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
    public RecommendedActionDecision ActionDecision { get; }
    public AddDecisionResult AddDecisionResult { get; }
    public PolicyConfigurationIdentity PolicyIdentity { get; }
    public PositionAction RecommendedAction => ActionDecision.Action;
    public AddDecision AddDecision => AddDecisionResult.Decision;
    public RuleVersion PolicyVersion => RuleVersion.From(PolicyIdentity.Version);
    public string PolicyHash => PolicyIdentity.Hash;
    public decimal? Confidence => ActionDecision.ReasonCodes.Count == 0 ? null : ActionDecision.Confidence;
    public RecommendationPriority? Priority =>
        ActionDecision.ReasonCodes.Count == 0 ? null : ActionDecision.Priority;
    public IReadOnlyList<ReasonCode> ActionReasonCodes => ActionDecision.ReasonCodes;
    public IReadOnlyList<ReasonCode> AddReasonCodes => AddDecisionResult.ReasonCodes;
    public decimal? MaximumAdditionalPositionValue => AddDecisionResult.MaximumAdditionalPositionValue;
    public decimal? MaximumAdditionalQuantity => AddDecisionResult.MaximumAdditionalQuantity;
    public AddDecisionConditions? AddConditions => AddDecisionResult.Conditions;
    public IReadOnlyList<ReasonCode> ReasonCodes { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset ValidUntil { get; }
    public RecommendationStatus Status { get; private set; }
    public DateTimeOffset? AcknowledgedAt { get; private set; }
    public DateTimeOffset? DismissedAt { get; private set; }
    public DateTimeOffset? SupersededAt { get; private set; }
    public DateTimeOffset? ExpiredAt { get; private set; }
    public RecommendationId? SupersededByRecommendationId { get; private set; }

    /// <summary>Есть ли у записи полные E.2 decision metadata, а не legacy semantics.</summary>
    public bool HasStructuredDecision =>
        PolicyIdentity.Hash != PolicyConfigurationIdentity.Legacy.Hash &&
        ActionDecision.ReasonCodes.Count > 0 &&
        AddDecision != Decisions.AddDecision.NotEvaluated;

    /// <summary>
    /// Совместимый factory для старых callers. Такие записи сохраняют legacy metadata и
    /// допускают NotEvaluated только как backward-compatible semantics.
    /// </summary>
    public static Recommendation Create(
        PositionAssessment assessment,
        PositionAction recommendedAction,
        AddDecision addDecision,
        RuleVersion policyVersion,
        IEnumerable<ReasonCode> reasonCodes,
        DateTimeOffset createdAt,
        DateTimeOffset validUntil)
    {
        ArgumentNullException.ThrowIfNull(reasonCodes);
        if (!Enum.IsDefined(recommendedAction))
            throw new ArgumentOutOfRangeException(
                nameof(recommendedAction),
                recommendedAction,
                "Action must be defined.");
        if (!Enum.IsDefined(addDecision))
            throw new ArgumentOutOfRangeException(
                nameof(addDecision),
                addDecision,
                "Add decision must be defined.");
        if (addDecision == AddDecision.AddAllowed)
            throw new InvalidOperationException(
                "AddAllowed can only be produced by RecommendationPolicy.");
        if (addDecision == AddDecision.NotEvaluated)
            throw new ArgumentException(
                "NotEvaluated is reserved for restoring persisted legacy recommendations.");

        var specificReasons = reasonCodes.Distinct().ToArray();
        var action = RecommendedActionDecision.Legacy(recommendedAction);
        var add = AddDecisionResult.Legacy(addDecision);
        return CreateCore(
            assessment,
            action,
            add,
            PolicyConfigurationIdentity.From(policyVersion.Value, PolicyConfigurationIdentity.Legacy.Hash),
            specificReasons,
            createdAt,
            validUntil,
            legacy: true);
    }

    /// <summary>Создаёт новую рекомендацию из полного результата E.2 policy.</summary>
    public static Recommendation Create(
        PositionAssessment assessment,
        RecommendationPolicyEvaluation evaluation)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        return CreateCore(
            assessment,
            evaluation.Action,
            evaluation.AddDecision,
            evaluation.PolicyIdentity,
            evaluation.Action.ReasonCodes.Concat(evaluation.AddDecision.ReasonCodes).ToArray(),
            evaluation.CreatedAt,
            evaluation.ValidUntil,
            legacy: false);
    }

    private static Recommendation CreateCore(
        PositionAssessment assessment,
        RecommendedActionDecision action,
        AddDecisionResult addDecision,
        PolicyConfigurationIdentity policyIdentity,
        IEnumerable<ReasonCode> specificReasonCodes,
        DateTimeOffset createdAt,
        DateTimeOffset validUntil,
        bool legacy)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(addDecision);
        ArgumentNullException.ThrowIfNull(specificReasonCodes);
        ValidateIdentity(policyIdentity);
        ValidateDecision(assessment, action, addDecision, legacy);
        if (!legacy &&
            !IsSafetyBlocked(assessment) &&
            policyIdentity != assessment.InputVersions.BasePolicyConfigurationIdentity)
            throw new InvalidOperationException(
                "Recommendation policy identity must match the assessment base policy identity.");

        if (createdAt < assessment.CreatedAt || createdAt >= assessment.ValidUntil)
            throw new ArgumentException("CreatedAt must be within the assessment validity window.", nameof(createdAt));
        if (validUntil <= createdAt)
            throw new ArgumentException("ValidUntil must be after CreatedAt.", nameof(validUntil));
        if (validUntil > assessment.ValidUntil)
            throw new ArgumentException("Recommendation cannot outlive its assessment.", nameof(validUntil));

        var specificReasons = specificReasonCodes.Distinct().ToArray();
        ValidateReasons(specificReasons);
        if (specificReasons.Any(ReasonCodeClassification.IsPortfolioRiskReason))
            throw new ArgumentException(
                "Portfolio risk reasons must be inherited from the assessment.", nameof(specificReasonCodes));

        var reasons = BuildReasonCodes(
            assessment,
            action,
            addDecision,
            specificReasons,
            legacy);
        if (reasons.Length == 0)
            throw new ArgumentException("At least one reason code is required.", nameof(specificReasonCodes));
        ValidateReasonInheritance(assessment, reasons);

        return new(
            RecommendationId.New(),
            assessment.Id,
            assessment.PositionId,
            action,
            addDecision,
            policyIdentity,
            new ReadOnlyCollection<ReasonCode>(reasons.ToArray()),
            createdAt,
            validUntil);
    }

    /// <summary>
    /// Восстанавливает старую запись, для которой structured E.2 metadata ещё отсутствует.
    /// </summary>
    public static Recommendation Restore(
        RecommendationId id,
        PositionAssessment assessment,
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
        RecommendationId? supersededByRecommendationId)
    {
        ArgumentNullException.ThrowIfNull(reasonCodes);
        return RestoreCore(
            id,
            assessment,
            RecommendedActionDecision.Legacy(recommendedAction),
            AddDecisionResult.Legacy(addDecision),
            PolicyConfigurationIdentity.From(policyVersion.Value, PolicyConfigurationIdentity.Legacy.Hash),
            reasonCodes,
            createdAt,
            validUntil,
            status,
            acknowledgedAt,
            dismissedAt,
            supersededAt,
            expiredAt,
            supersededByRecommendationId,
            legacy: true);
    }

    /// <summary>Восстанавливает recommendation с полным E.2 decision context.</summary>
    public static Recommendation Restore(
        RecommendationId id,
        PositionAssessment assessment,
        RecommendedActionDecision action,
        AddDecisionResult addDecision,
        PolicyConfigurationIdentity policyIdentity,
        IEnumerable<ReasonCode> reasonCodes,
        DateTimeOffset createdAt,
        DateTimeOffset validUntil,
        RecommendationStatus status,
        DateTimeOffset? acknowledgedAt,
        DateTimeOffset? dismissedAt,
        DateTimeOffset? supersededAt,
        DateTimeOffset? expiredAt,
        RecommendationId? supersededByRecommendationId)
    {
        return RestoreCore(
            id,
            assessment,
            action,
            addDecision,
            policyIdentity,
            reasonCodes,
            createdAt,
            validUntil,
            status,
            acknowledgedAt,
            dismissedAt,
            supersededAt,
            expiredAt,
            supersededByRecommendationId,
            legacy: false);
    }

    private static Recommendation RestoreCore(
        RecommendationId id,
        PositionAssessment assessment,
        RecommendedActionDecision action,
        AddDecisionResult addDecision,
        PolicyConfigurationIdentity policyIdentity,
        IEnumerable<ReasonCode> reasonCodes,
        DateTimeOffset createdAt,
        DateTimeOffset validUntil,
        RecommendationStatus status,
        DateTimeOffset? acknowledgedAt,
        DateTimeOffset? dismissedAt,
        DateTimeOffset? supersededAt,
        DateTimeOffset? expiredAt,
        RecommendationId? supersededByRecommendationId,
        bool legacy)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(addDecision);
        ArgumentNullException.ThrowIfNull(reasonCodes);
        if (id == default)
            throw new ArgumentException("RecommendationId must be initialized.", nameof(id));
        ValidateIdentity(policyIdentity);
        ValidateDecision(assessment, action, addDecision, legacy);
        if (!legacy &&
            !IsSafetyBlocked(assessment) &&
            policyIdentity != assessment.InputVersions.BasePolicyConfigurationIdentity)
            throw new InvalidOperationException(
                "Recommendation policy identity must match the assessment base policy identity.");
        ValidateEnum(status, nameof(status));

        if (createdAt < assessment.CreatedAt || createdAt >= assessment.ValidUntil)
            throw new ArgumentException("CreatedAt must be within the assessment validity window.", nameof(createdAt));
        if (validUntil <= createdAt || validUntil > assessment.ValidUntil)
            throw new ArgumentException("Recommendation validity window is invalid.", nameof(validUntil));

        var reasons = reasonCodes.ToArray();
        if (reasons.Length == 0)
            throw new ArgumentException("At least one reason code is required.", nameof(reasonCodes));
        ValidateReasons(reasons);
        if (reasons.Distinct().Count() != reasons.Length)
            throw new ArgumentException("Reason codes cannot contain duplicates.", nameof(reasonCodes));
        ValidateReasonInheritance(assessment, reasons);
        if (!legacy &&
            !reasons.SequenceEqual(BuildReasonCodes(assessment, action, addDecision, [], legacy)))
            throw new ArgumentException(
                "Recommendation reason codes must match the persisted action and add decisions.",
                nameof(reasonCodes));

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
            assessment.Id,
            assessment.PositionId,
            action,
            addDecision,
            policyIdentity,
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

    public bool IsEffectiveAt(DateTimeOffset at) =>
        Status is RecommendationStatus.Active or RecommendationStatus.Acknowledged &&
        CreatedAt <= at && at < ValidUntil;

    private static ReasonCode[] BuildReasonCodes(
        PositionAssessment assessment,
        RecommendedActionDecision action,
        AddDecisionResult addDecision,
        IEnumerable<ReasonCode> legacySpecificReasons,
        bool legacy)
    {
        var inherited = assessment.ReasonCodes
            .Where(ReasonCodeClassification.IsPortfolioRiskReason)
            .ToArray();
        if (legacy)
            return inherited
                .Concat(legacySpecificReasons)
                .Distinct()
                .ToArray();

        return inherited
            .Concat(action.ReasonCodes)
            .Concat(addDecision.ReasonCodes)
            .Distinct()
            .ToArray();
    }

    private static void ValidateDecision(
        PositionAssessment assessment,
        RecommendedActionDecision action,
        AddDecisionResult addDecision,
        bool legacy)
    {
        if (!Enum.IsDefined(action.Action))
            throw new ArgumentOutOfRangeException(nameof(action), action.Action, "Action must be defined.");
        if (!Enum.IsDefined(addDecision.Decision))
            throw new ArgumentOutOfRangeException(
                nameof(addDecision),
                addDecision.Decision,
                "Add decision must be defined.");
        if (addDecision.Decision == Decisions.AddDecision.AddAllowed &&
            assessment.PortfolioRiskDecision == RiskIncreaseDecision.Blocked)
            throw new InvalidOperationException("A blocked portfolio risk decision cannot produce AddAllowed.");
        if (!legacy && addDecision.Decision == Decisions.AddDecision.NotEvaluated)
            throw new InvalidOperationException("New recommendations cannot use NotEvaluated.");
        if (!legacy &&
            (action.ReasonCodes.Any(ReasonCodeClassification.IsPortfolioRiskReason) ||
             addDecision.ReasonCodes.Any(ReasonCodeClassification.IsPortfolioRiskReason)))
            throw new ArgumentException(
                "Structured decision reasons cannot contain inherited portfolio-risk codes.");
    }

    private static void ValidateIdentity(PolicyConfigurationIdentity identity)
    {
        if (string.IsNullOrWhiteSpace(identity.Version) || string.IsNullOrWhiteSpace(identity.Hash))
            throw new ArgumentException("Policy identity must contain version and hash.", nameof(identity));
    }

    private static bool IsSafetyBlocked(PositionAssessment assessment) =>
        assessment.Result.IsLegacy ||
        assessment.Result.DataQuality.Overall != AssessmentDataQuality.FreshCompleteReliable ||
        assessment.Result.DataQuality.SafetyState != AssessmentSafetyState.Allowed;

    private static void ValidateReasons(IEnumerable<ReasonCode> reasons)
    {
        if (reasons.Any(reason => !Enum.IsDefined(reason)))
            throw new ArgumentOutOfRangeException(nameof(reasons), "Reason code must be defined.");
    }

    private static void ValidateReasonInheritance(
        PositionAssessment assessment,
        IReadOnlyList<ReasonCode> reasons)
    {
        var inheritedReasons = assessment.ReasonCodes
            .Where(ReasonCodeClassification.IsPortfolioRiskReason)
            .ToArray();
        var persistedInheritedReasons = reasons
            .Where(ReasonCodeClassification.IsPortfolioRiskReason)
            .ToArray();

        if (!persistedInheritedReasons.SequenceEqual(inheritedReasons))
            throw new ArgumentException(
                "Recommendation portfolio-risk reasons must match its assessment.");

        var specificReasons = reasons
            .Where(reason => !ReasonCodeClassification.IsPortfolioRiskReason(reason))
            .ToArray();
        if (!reasons.SequenceEqual(inheritedReasons.Concat(specificReasons)))
            throw new ArgumentException(
                "Recommendation reasons must contain inherited reasons before specific reasons.");
    }

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

    private void EnsureStatus(RecommendationStatus expected, string operation) =>
        EnsureStatus(operation, expected);

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
