using Intelligence.TradeSystem.Api.Contracts.V1.Positions;
using Intelligence.TradeSystem.Api.Errors;
using Intelligence.TradeSystem.Api.Mappers;
using Intelligence.TradeSystem.Application.Evaluations;
using Intelligence.TradeSystem.Application.Users;
using Intelligence.TradeSystem.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Intelligence.TradeSystem.Api.Controllers;

[ApiController]
[Route("api/v1/positions")]
[Authorize(Policy = "TradeUser")]
public sealed class PositionEvaluationController(
    PositionEvaluationService positionEvaluationService,
    ICurrentUserContext currentUserContext) : ControllerBase
{
    [HttpGet("{id}/evaluation")]
    [ProducesResponseType(typeof(PositionEvaluationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PositionEvaluationResponse>> Get(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
            return BadRequestProblem("The position id must be a non-empty GUID.");

        var result = await positionEvaluationService
            .GetAsync(
                currentUserContext.UserId,
                PositionId.FromGuid(id),
                cancellationToken)
            .ConfigureAwait(false);

        return result.Outcome switch
        {
            PositionEvaluationReadOutcome.Found =>
                Ok(PositionEvaluationMapper.ToResponse(
                    result.Snapshot ?? throw new InvalidOperationException(
                        "A found evaluation must contain a snapshot."))),
            PositionEvaluationReadOutcome.NotEvaluated => NoContent(),
            PositionEvaluationReadOutcome.NotFound => NotFoundProblem(),
            _ => throw new InvalidOperationException("Unknown position evaluation read outcome."),
        };
    }

    [HttpPost("{id}/evaluation")]
    [ProducesResponseType(typeof(PositionEvaluationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<PositionEvaluationResponse>> Evaluate(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
            return BadRequestProblem("The position id must be a non-empty GUID.");

        var result = await positionEvaluationService
            .EvaluateAsync(
                currentUserContext.UserId,
                PositionId.FromGuid(id),
                cancellationToken)
            .ConfigureAwait(false);

        return result.Outcome switch
        {
            PositionEvaluationOutcome.Succeeded =>
                Ok(PositionEvaluationMapper.ToResponse(
                    result.Snapshot ?? throw new InvalidOperationException(
                        "A successful evaluation must contain a snapshot."))),
            PositionEvaluationOutcome.NotFound => NotFoundProblem(),
            PositionEvaluationOutcome.NotEvaluable => ConflictProblem(
                result.NotEvaluableReason
                    ?? throw new InvalidOperationException(
                        "Результат невозможной оценки должен содержать причину.")),
            _ => throw new InvalidOperationException("Unknown position evaluation outcome."),
        };
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

    private ConflictObjectResult ConflictProblem(
        PositionEvaluationNotEvaluableReason reason)
    {
        var problemDetails = ApiProblemDetails.Create(
            HttpContext,
            ApiErrorDescriptors.PositionNotEvaluable,
            "The position cannot be evaluated with the current state.");
        problemDetails.Extensions["reason"] =
            PositionEvaluationNotEvaluableReasonV1Mapper.ToWireValue(reason);
        return Conflict(problemDetails);
    }
}
