using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Intelligence.TradeSystem.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
[Authorize(Policy = "TradeApi")]
public sealed class AuthController : ControllerBase
{
    [HttpGet("me")]
    public IActionResult GetCurrentPrincipal() =>
        Ok(new
        {
            Subject = User.FindFirst("sub")?.Value,
            Authenticated = User.Identity?.IsAuthenticated == true
        });
}
