using System.Text.Encodings.Web;
using Intelligence.TradeSystem.Identity.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Intelligence.TradeSystem.Identity.Controllers;

[AllowAnonymous]
public sealed class AccountController(
    SignInManager<ApplicationUser> signInManager,
    IAntiforgery antiforgery) : Controller
{
    [HttpGet("/account/login")]
    public IActionResult Login(string? returnUrl = null)
    {
        var token = antiforgery.GetAndStoreTokens(HttpContext).RequestToken;
        var encodedToken = HtmlEncoder.Default.Encode(token ?? string.Empty);
        var encodedReturnUrl = HtmlEncoder.Default.Encode(returnUrl ?? string.Empty);

        return Content(
            $$"""
            <!doctype html>
            <html lang="en">
            <head><meta charset="utf-8"><title>Sign in</title></head>
            <body>
              <form method="post" action="/account/login">
                <input type="hidden" name="__RequestVerificationToken" value="{{encodedToken}}">
                <input type="hidden" name="returnUrl" value="{{encodedReturnUrl}}">
                <label>Username <input name="username" autocomplete="username"></label>
                <label>Password <input name="password" type="password" autocomplete="current-password"></label>
                <button type="submit">Sign in</button>
              </form>
            </body>
            </html>
            """,
            "text/html");
    }

    [HttpPost("/account/login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(
        [FromForm] string? username,
        [FromForm] string? password,
        [FromForm] string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return InvalidLogin();
        }

        var result = await signInManager.PasswordSignInAsync(
            username,
            password,
            isPersistent: false,
            lockoutOnFailure: true);

        if (!result.Succeeded)
        {
            return InvalidLogin();
        }

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return Redirect("/");
    }

    private BadRequestObjectResult InvalidLogin() =>
        BadRequest("Invalid username or password.");
}
