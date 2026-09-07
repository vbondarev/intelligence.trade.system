using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Users;

/// <summary>
/// Supplies the validated user identity for a user-delegated application operation.
/// </summary>
public interface ICurrentUserContext
{
    UserId UserId { get; }
}
