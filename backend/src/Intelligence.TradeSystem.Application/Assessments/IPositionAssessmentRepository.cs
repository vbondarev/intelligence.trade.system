using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Assessments;

public interface IPositionAssessmentRepository
{
    Task<PositionAssessment?> GetByIdAsync(
        UserId userId,
        PositionAssessmentId id,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        UserId userId,
        PositionAssessment assessment,
        CancellationToken cancellationToken = default);
}
