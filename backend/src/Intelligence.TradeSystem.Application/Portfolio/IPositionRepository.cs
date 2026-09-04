using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Portfolio;

public interface IPositionRepository
{
    Task<Position?> GetByIdAsync(PositionId id, CancellationToken cancellationToken = default);

    Task SaveAsync(Position position, CancellationToken cancellationToken = default);
}
