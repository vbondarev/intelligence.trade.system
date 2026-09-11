using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Users;

/// <summary>
/// Предоставляет проверенную идентичность пользователя для прикладной операции от его имени.
/// </summary>
public interface ICurrentUserContext
{
    UserId UserId { get; }
}
