using Intelligence.TradeSystem.Api.Contracts.V1.Positions.Market;
using Intelligence.TradeSystem.Api.Errors;
using Intelligence.TradeSystem.Api.Mappers;
using Intelligence.TradeSystem.Api.Serialization;
using Intelligence.TradeSystem.Application.Market.Positions;
using Intelligence.TradeSystem.Application.Users;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Intelligence.TradeSystem.Api.Controllers;

[ApiController]
[Route("api/v1/positions")]
[Authorize(Policy = "TradeUser")]
public sealed class PositionMarketController(
    PositionMarketService positionMarketService,
    ICurrentUserContext currentUserContext) : ControllerBase
{
    [HttpGet("{id}/market")]
    [ProducesResponseType(typeof(PositionMarketResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<PositionMarketResponse>> GetMarket(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
        {
            return BadRequestProblem("The position id must be a non-empty GUID.");
        }

        var result = await positionMarketService
            .GetMarketAsync(
                currentUserContext.UserId,
                PositionId.FromGuid(id),
                cancellationToken)
            .ConfigureAwait(false);

        return result is null
            ? NotFoundProblem()
            : Ok(PositionMarketMapper.ToResponse(result));
    }

    [HttpGet("{id}/candles")]
    [ProducesResponseType(typeof(PositionCandlesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<PositionCandlesResponse>> GetCandles(
        [FromRoute] Guid id,
        [FromQuery] string interval,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
        {
            return BadRequestProblem("The position id must be a non-empty GUID.");
        }

        if (Request.Query["interval"].Count != 1 ||
            string.IsNullOrWhiteSpace(interval) ||
            !CandleIntervalV1Codec.TryParse(interval, out var parsedInterval))
        {
            return BadRequestProblem("The interval value is invalid.");
        }

        if (Request.Query["limit"].Count > 1 ||
            (Request.Query.ContainsKey("limit") && limit is null) ||
            limit is < 1 or > PositionMarketService.MaxCandleLimit)
        {
            return BadRequestProblem(
                $"The limit must be between 1 and {PositionMarketService.MaxCandleLimit}.");
        }

        var result = await positionMarketService
            .GetCandlesAsync(
                currentUserContext.UserId,
                PositionId.FromGuid(id),
                parsedInterval,
                limit ?? PositionMarketService.DefaultCandleLimit,
                cancellationToken)
            .ConfigureAwait(false);

        return result is null
            ? NotFoundProblem()
            : Ok(PositionMarketMapper.ToResponse(result));
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
