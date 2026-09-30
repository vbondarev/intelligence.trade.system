using Intelligence.TradeSystem.Api.Contracts.V1.Portfolio;
using Intelligence.TradeSystem.Api.Errors;
using Intelligence.TradeSystem.Api.Mappers;
using Intelligence.TradeSystem.Application.Portfolio.Read;
using Intelligence.TradeSystem.Application.Users;
using Intelligence.TradeSystem.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Intelligence.TradeSystem.Api.Controllers;

[ApiController]
[Route("api/v1/exchange-accounts")]
[Authorize(Policy = "TradeUser")]
public sealed class ExchangeAccountPortfolioController(
    PortfolioReadService portfolioReadService,
    ICurrentUserContext currentUserContext) : ControllerBase
{
    [HttpGet("{id}/portfolio")]
    [ProducesResponseType(typeof(PortfolioResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PortfolioResponse>> Get(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
        {
            return BadRequestProblem("The exchange account id must be a non-empty GUID.");
        }

        var result = await portfolioReadService
            .GetLatestAsync(
                currentUserContext.UserId,
                ExchangeAccountId.FromGuid(id),
                cancellationToken)
            .ConfigureAwait(false);
        if (!result.AccountExists)
        {
            return NotFoundProblem();
        }

        return result.Summary is null
            ? NoContent()
            : Ok(PortfolioMapper.ToResponse(result.Summary));
    }

    private BadRequestObjectResult BadRequestProblem(string detail) =>
        BadRequest(ApiProblemDetails.CreateValidation(HttpContext, detail));

    private ObjectResult NotFoundProblem() =>
        StatusCode(
            StatusCodes.Status404NotFound,
            ApiProblemDetails.Create(
                HttpContext,
                ApiErrorDescriptors.ResourceNotFound,
                "The requested resource was not found."));
}
