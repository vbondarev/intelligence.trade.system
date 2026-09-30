using Intelligence.TradeSystem.Api.Contracts.V1.Common;
using Intelligence.TradeSystem.Api.Contracts.V1.Positions;
using Intelligence.TradeSystem.Api.Errors;
using Intelligence.TradeSystem.Api.Mappers;
using Intelligence.TradeSystem.Api.RequestParsing;
using Intelligence.TradeSystem.Application.Portfolio.Read;
using Intelligence.TradeSystem.Application.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Api.Controllers;

[ApiController]
[Route("api/v1/positions")]
[Authorize(Policy = "TradeUser")]
public sealed class PositionsController(
    PositionReadService positionReadService,
    ICurrentUserContext currentUserContext) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(CursorPage<PositionListItemResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CursorPage<PositionListItemResponse>>> List(
        [FromQuery] Guid? exchangeAccountId,
        [FromQuery] string? trackingState,
        [FromQuery] string? symbol,
        [FromQuery] string? side,
        [FromQuery] int? pageSize,
        [FromQuery] string? cursor,
        CancellationToken cancellationToken)
    {
        if (!PositionListQueryParser.TryParse(
                exchangeAccountId,
                trackingState,
                symbol,
                side,
                pageSize,
                cursor,
                Request.Query,
                out var query,
                out var error))
        {
            return BadRequestProblem(error!);
        }

        var page = await positionReadService
            .ListAsync(currentUserContext.UserId, query!, cancellationToken)
            .ConfigureAwait(false);

        return Ok(PositionMapper.ToResponse(page));
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(PositionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PositionResponse>> Get(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
        {
            return BadRequestProblem("The position id must be a non-empty GUID.");
        }

        var detail = await positionReadService
            .GetByIdAsync(
                currentUserContext.UserId,
                PositionId.FromGuid(id),
                cancellationToken)
            .ConfigureAwait(false);
        return detail is null
            ? NotFoundProblem()
            : Ok(PositionMapper.ToResponse(detail));
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
