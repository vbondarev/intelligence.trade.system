using Intelligence.TradeSystem.Identity.Identity;
using Intelligence.TradeSystem.Identity.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Intelligence.TradeSystem.Identity.Controllers;

[AllowAnonymous]
public sealed class AccountController(SignInManager<ApplicationUser> signInManager) : Controller
{
    private const string InvalidLoginMessage = "Неверное имя пользователя или пароль.";

    [HttpGet("/account/login")]
    public IActionResult Login(string? returnUrl = null) =>
        View(new LoginViewModel { ReturnUrl = returnUrl });

    [HttpPost("/account/login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(
        [FromForm] string? username,
        [FromForm] string? password,
        [FromForm] string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return InvalidLogin(username, returnUrl);
        }

        var result = await signInManager.PasswordSignInAsync(
            username,
            password,
            isPersistent: false,
            lockoutOnFailure: true);

        if (!result.Succeeded)
        {
            return InvalidLogin(username, returnUrl);
        }

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return Redirect("/");
    }

    // Неизвестный пользователь, неверный пароль и lockout дают одинаковый ответ,
    // чтобы страница не раскрывала существование учётной записи.
    private ViewResult InvalidLogin(string? username, string? returnUrl)
    {
        var view = View(nameof(Login), new LoginViewModel
        {
            Username = username,
            ReturnUrl = returnUrl,
            ErrorMessage = InvalidLoginMessage,
        });
        view.StatusCode = StatusCodes.Status400BadRequest;
        return view;
    }
}
