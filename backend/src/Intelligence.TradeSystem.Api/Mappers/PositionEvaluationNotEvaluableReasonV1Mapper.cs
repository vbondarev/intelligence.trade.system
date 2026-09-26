using Intelligence.TradeSystem.Application.Evaluations;

namespace Intelligence.TradeSystem.Api.Mappers;

internal static class PositionEvaluationNotEvaluableReasonV1Mapper
{
    public static string ToWireValue(PositionEvaluationNotEvaluableReason reason) =>
        reason switch
        {
            PositionEvaluationNotEvaluableReason.ClosedPosition => "closed_position",
            PositionEvaluationNotEvaluableReason.PortfolioUnavailable => "portfolio_unavailable",
            PositionEvaluationNotEvaluableReason.PortfolioInconsistent => "portfolio_inconsistent",
            PositionEvaluationNotEvaluableReason.TemporalInconsistency => "temporal_inconsistency",
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
        };
}
