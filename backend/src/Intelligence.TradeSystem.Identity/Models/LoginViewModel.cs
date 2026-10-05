namespace Intelligence.TradeSystem.Identity.Models;

/// <summary>
/// Состояние страницы входа. Пароль в модель намеренно не входит и после неудачной попытки не возвращается в HTML.
/// </summary>
public sealed class LoginViewModel
{
    public string? Username { get; init; }

    public string? ReturnUrl { get; init; }

    /// <summary>
    /// Общая ошибка аутентификации, одинаковая для неизвестного пользователя, неверного пароля и lockout.
    /// </summary>
    public string? ErrorMessage { get; init; }
}
