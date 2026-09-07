using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Intelligence.TradeSystem.Authentication.IntegrationTests;

[ApiController]
[Route("test-only/protected")]
[Authorize(Policy = "TradeApi")]
public sealed class TestOnlyProtectedController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() =>
        Ok(new
        {
            Subject = User.FindFirst("sub")?.Value,
        });
}
