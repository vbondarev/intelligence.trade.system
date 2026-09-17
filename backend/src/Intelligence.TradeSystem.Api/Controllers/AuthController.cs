using Intelligence.TradeSystem.Api.Contracts.V1.Auth;
using Intelligence.TradeSystem.Api.Serialization;
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
    [ProducesResponseType(typeof(CurrentUserResponse), StatusCodes.Status200OK)]
    public ActionResult<CurrentUserResponse> GetCurrentPrincipal() =>
        new JsonResult(
            new CurrentUserResponse(
                currentUserContext.UserId.Value,
                currentUserContext.UserId.Value.ToString("D"),
                Authenticated: true),
            V1JsonSerializerOptions.Default);
}
