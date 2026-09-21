using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Recommendations;

namespace Intelligence.TradeSystem.Application.Evaluations;

public sealed record PositionEvaluationSnapshot(
    PositionAssessment Assessment,
    Recommendation? Recommendation);

public enum PositionEvaluationReadOutcome
{
    Found,
    NotEvaluated,
    NotFound,
}

public sealed record PositionEvaluationReadResult(
    PositionEvaluationReadOutcome Outcome,
    PositionEvaluationSnapshot? Snapshot)
{
    public static PositionEvaluationReadResult Found(PositionEvaluationSnapshot snapshot) =>
        new(PositionEvaluationReadOutcome.Found, snapshot);

    public static PositionEvaluationReadResult NotEvaluated() =>
        new(PositionEvaluationReadOutcome.NotEvaluated, null);

    public static PositionEvaluationReadResult NotFound() =>
        new(PositionEvaluationReadOutcome.NotFound, null);
}

public enum PositionEvaluationOutcome
{
    Succeeded,
    NotFound,
    NotEvaluable,
}

public enum PositionEvaluationNotEvaluableReason
{
    ClosedPosition,
    PortfolioUnavailable,
    PortfolioInconsistent,
    TemporalInconsistency,
}

public sealed record PositionEvaluationResult(
    PositionEvaluationOutcome Outcome,
    PositionEvaluationSnapshot? Snapshot,
    PositionEvaluationNotEvaluableReason? NotEvaluableReason)
{
    public static PositionEvaluationResult Succeeded(PositionEvaluationSnapshot snapshot) =>
        new(PositionEvaluationOutcome.Succeeded, snapshot, null);

    public static PositionEvaluationResult NotFound() =>
        new(PositionEvaluationOutcome.NotFound, null, null);

    public static PositionEvaluationResult NotEvaluable(
        PositionEvaluationNotEvaluableReason reason) =>
        new(PositionEvaluationOutcome.NotEvaluable, null, reason);
}
