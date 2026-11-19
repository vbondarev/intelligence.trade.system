using Intelligence.TradeSystem.Identity.Configuration;
using Intelligence.TradeSystem.Identity.Identity;
using Intelligence.TradeSystem.Identity.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace Intelligence.TradeSystem.Identity.Controllers;

[AllowAnonymous]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AccountController(SignInManager<ApplicationUser> signInManager, WebBffClientOptions webBff) : Controller
{
    private const string InvalidLoginMessage = "Неверное имя пользователя или пароль.";
    private const string PreparationPath = "/account/web-login";
    private const string ContinuationPath = "/account/web-login/continue";

    [HttpGet(PreparationPath)]
    public IActionResult PrepareWebLogin(string? returnUrl)
    {
        if (webBff.LoginContinuation is not { } target) return NotFound();
        if (!TryNormalizeWebPath(returnUrl, out var path)) return BadRequest();

        if (signInManager.IsSignedIn(User))
        {
            return Redirect(QueryHelpers.AddQueryString(target.AbsoluteUri, "returnUrl", path));
        }

        var afterLogin = QueryHelpers.AddQueryString(ContinuationPath, "returnUrl", path);
        return LocalRedirect(QueryHelpers.AddQueryString("/account/login", "returnUrl", afterLogin));
    }

    [HttpGet(ContinuationPath)]
    public IActionResult ContinueWebLogin(string? returnUrl)
    {
        if (webBff.LoginContinuation is not { } target) return NotFound();
        if (!TryNormalizeWebPath(returnUrl, out var path)) return BadRequest();

        return signInManager.IsSignedIn(User)
            ? Redirect(QueryHelpers.AddQueryString(target.AbsoluteUri, "returnUrl", path))
            : LocalRedirect(QueryHelpers.AddQueryString(PreparationPath, "returnUrl", path));
    }

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
            if (returnUrl.StartsWith(ContinuationPath + "?", StringComparison.Ordinal))
            {
                // Продолжение должно быть GET, чтобы POST с credentials не пересылался.
                Response.Headers.Location = returnUrl;
                return StatusCode(StatusCodes.Status303SeeOther);
            }

            return Redirect(returnUrl);
        }

        return Redirect("/");
    }

    private bool TryNormalizeWebPath(string? returnUrl, out string path)
    {
        path = string.IsNullOrEmpty(returnUrl) ? "/app" : returnUrl;
        return path.Length <= 2048 && IsWebPath(path) && IsWebPath(Uri.UnescapeDataString(path));
    }

    private bool IsWebPath(string value) =>
        value.StartsWith('/') && Url.IsLocalUrl(value) && !value.Contains('\\')
        && !value.Any(character => char.IsControl(character) || char.IsWhiteSpace(character));

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
