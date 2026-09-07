using Intelligence.TradeSystem.Application.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Intelligence.TradeSystem.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
[Authorize(Policy = "TradeUser")]
public sealed class AuthController(ICurrentUserContext currentUserContext) : ControllerBase
{
    [HttpGet("me")]
    public IActionResult GetCurrentPrincipal() =>
        Ok(new
        {
            UserId = currentUserContext.UserId.Value,
            Subject = currentUserContext.UserId.Value.ToString(),
            Authenticated = true,
        });
}
