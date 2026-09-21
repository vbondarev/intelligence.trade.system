using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Portfolio.Timeline;

public interface IPositionTimelineReadStore
{
    Task<PositionTimelineCandidates?> ReadCandidatesAsync(
        UserId userId,
        PositionTimelineQuery query,
        CancellationToken cancellationToken = default);
}
