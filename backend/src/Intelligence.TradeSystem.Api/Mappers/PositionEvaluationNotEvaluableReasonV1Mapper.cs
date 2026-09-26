using Intelligence.TradeSystem.Api.Contracts.V1.Positions;
using Intelligence.TradeSystem.Application.Evaluations;

namespace Intelligence.TradeSystem.Api.Mappers;

internal static class PositionEvaluationNotEvaluableReasonV1Mapper
{
    public static PositionNotEvaluableReasonV1 ToWireValue(
        PositionEvaluationNotEvaluableReason reason) =>
        reason switch
        {
            PositionEvaluationNotEvaluableReason.ClosedPosition =>
                PositionNotEvaluableReasonV1.ClosedPosition,
            PositionEvaluationNotEvaluableReason.PortfolioUnavailable =>
                PositionNotEvaluableReasonV1.PortfolioUnavailable,
            PositionEvaluationNotEvaluableReason.PortfolioInconsistent =>
                PositionNotEvaluableReasonV1.PortfolioInconsistent,
            PositionEvaluationNotEvaluableReason.TemporalInconsistency =>
                PositionNotEvaluableReasonV1.TemporalInconsistency,
            _ => throw new ArgumentOutOfRangeException(
                nameof(reason),
                reason,
                "Неизвестная причина невозможности оценки позиции."),
        };
}
