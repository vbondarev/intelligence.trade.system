using Intelligence.TradeSystem.Api.Contracts.V1.Common;
using Intelligence.TradeSystem.Api.Contracts.V1.Positions;
using Intelligence.TradeSystem.Api.Errors;
using Intelligence.TradeSystem.Api.Serialization;
using Intelligence.TradeSystem.Application.Portfolio.Timeline;
using Intelligence.TradeSystem.Application.Users;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Decisions;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Recommendations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Intelligence.TradeSystem.Api.Controllers;

[ApiController]
[Route("api/v1/positions")]
[Authorize(Policy = "TradeUser")]
public sealed class PositionTimelineController(
    PositionTimelineService positionTimelineService,
    ICurrentUserContext currentUserContext) : ControllerBase
{
    [HttpGet("{id}/timeline")]
    [ProducesResponseType(typeof(CursorPage<PositionTimelineItemResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CursorPage<PositionTimelineItemResponse>>> Get(
        [FromRoute] Guid id,
        [FromQuery] int? pageSize,
        [FromQuery(Name = "type")] string[]? types,
        [FromQuery] string? cursor,
        CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
            return BadRequestProblem("The position id must be a non-empty GUID.");

        if (Request.Query["pageSize"].Count > 1 ||
            (Request.Query.ContainsKey("pageSize") && pageSize is null) ||
            pageSize is < CursorPagination.MinPageSize or > CursorPagination.MaxPageSize)
        {
            return BadRequestProblem(
                $"The pageSize must be between {CursorPagination.MinPageSize} and {CursorPagination.MaxPageSize}.");
        }

        if (!TryParseKinds(types, out var kinds))
            return BadRequestProblem("The type value is invalid.");

        PositionTimelineCursor? parsedCursor = null;
        if (Request.Query.ContainsKey("cursor"))
        {
            if (!PositionTimelineCursorCodec.TryDecode(cursor ?? string.Empty, out var decodedCursor))
                return BadRequestProblem("The cursor value is invalid.");

            parsedCursor = decodedCursor;
        }

        PositionTimelineQuery query;
        try
        {
            query = new PositionTimelineQuery(
                PositionId.FromGuid(id),
                pageSize ?? CursorPagination.DefaultPageSize,
                kinds,
                parsedCursor);
        }
        catch (ArgumentException)
        {
            return BadRequestProblem("The cursor is incompatible with the selected timeline types.");
        }

        var page = await positionTimelineService
            .GetAsync(currentUserContext.UserId, query, cancellationToken)
            .ConfigureAwait(false);
        if (page is null)
            return NotFoundProblem();

        return Ok(new CursorPage<PositionTimelineItemResponse>(
            page.Items.Select(ToResponse).ToArray(),
            page.NextCursor is { } nextCursor
                ? PositionTimelineCursorCodec.Encode(nextCursor)
                : null,
            page.HasMore));
    }

    private static bool TryParseKinds(
        string[]? values,
        out IReadOnlyCollection<PositionTimelineItemKind> kinds)
    {
        if (values is null || values.Length == 0)
        {
            kinds =
            [
                PositionTimelineItemKind.PositionChange,
                PositionTimelineItemKind.Evaluation,
                PositionTimelineItemKind.Recommendation,
            ];
            return true;
        }

        var parsed = new HashSet<PositionTimelineItemKind>();
        foreach (var value in values!)
        {
            var kind = value switch
            {
                "positionChange" => PositionTimelineItemKind.PositionChange,
                "evaluation" => PositionTimelineItemKind.Evaluation,
                "recommendation" => PositionTimelineItemKind.Recommendation,
                _ => (PositionTimelineItemKind?)null,
            };
            if (kind is null)
            {
                kinds = [];
                return false;
            }

            parsed.Add(kind.Value);
        }

        kinds = parsed.ToArray();
        return true;
    }

    private static PositionTimelineItemResponse ToResponse(PositionTimelineItem item) => item.Kind switch
    {
        PositionTimelineItemKind.PositionChange => new(
            PositionTimelineItemTypeV1.PositionChange,
            item.OccurredAt,
            ToResponse(item.PositionChange
                ?? throw new InvalidOperationException("A position change timeline item must contain a change.")),
            null,
            null),
        PositionTimelineItemKind.Evaluation => new(
            PositionTimelineItemTypeV1.Evaluation,
            item.OccurredAt,
            null,
            ToResponse(item.Evaluation
                ?? throw new InvalidOperationException("An evaluation timeline item must contain an evaluation.")),
            null),
        PositionTimelineItemKind.Recommendation => new(
            PositionTimelineItemTypeV1.Recommendation,
            item.OccurredAt,
            null,
            null,
            ToResponse(item.Recommendation
                ?? throw new InvalidOperationException(
                    "A recommendation timeline item must contain a recommendation."))),
        _ => throw new InvalidOperationException($"Unsupported timeline item kind '{item.Kind}'."),
    };

    private static PositionTimelinePositionChangeResponse ToResponse(
        PositionTimelinePositionChange change) =>
        new(
            change.Sequence,
            ToWire<PositionChangeKind, PositionChangeKindV1>(change.Kind),
            ToWire<PositionChangeCause, PositionChangeCauseV1>(change.Cause),
            ToWire<PositionTrackingState, PositionTrackingStateV1>(change.TrackingStateAfter),
            change.Before is { } before ? ToResponse(before) : null,
            ToResponse(change.After));

    private static PositionTimelinePositionSnapshotResponse ToResponse(
        PositionTimelinePositionSnapshot snapshot) =>
        new(
            snapshot.Size,
            snapshot.AverageEntryPrice,
            snapshot.PositionValue,
            snapshot.Leverage,
            snapshot.MarkPrice,
            snapshot.BreakEvenPrice,
            snapshot.LiquidationPrice,
            snapshot.UnrealizedPnl,
            snapshot.TakeProfit,
            snapshot.StopLoss,
            snapshot.TrailingStop);

    private static PositionTimelineEvaluationResponse ToResponse(
        PositionTimelineEvaluation evaluation) =>
        new(
            evaluation.Id.Value,
            evaluation.EvaluatedAt,
            evaluation.ValidUntil,
            evaluation.RuleVersion.Value,
            evaluation.IsLegacy,
            new(
                ToWire<AssessmentDataQuality, AssessmentDataQualityV1>(evaluation.DataQuality.Market),
                ToWire<AssessmentDataQuality, AssessmentDataQualityV1>(evaluation.DataQuality.Portfolio),
                ToWire<AssessmentDataQuality, AssessmentDataQualityV1>(evaluation.DataQuality.Overall),
                ToWire<AssessmentSafetyState, AssessmentSafetyStateV1>(evaluation.DataQuality.SafetyState)),
            ToWire<RiskIncreaseDecision, RiskIncreaseDecisionV1>(evaluation.PortfolioRiskDecision),
            evaluation.ReasonCodes.Select(ToWire<ReasonCode, ReasonCodeV1>).ToArray());

    private static PositionTimelineRecommendationResponse ToResponse(
        PositionTimelineRecommendation recommendation) =>
        new(
            recommendation.Id.Value,
            recommendation.AssessmentId.Value,
            recommendation.CreatedAt,
            recommendation.ValidUntil,
            ToWire<RecommendationStatus, RecommendationStatusV1>(recommendation.Status),
            ToWire<PositionAction, PositionActionV1>(recommendation.Action),
            recommendation.Confidence,
            recommendation.Priority is { } priority
                ? ToWire<RecommendationPriority, RecommendationPriorityV1>(priority)
                : null,
            ToWire<AddDecision, AddDecisionV1>(recommendation.AddDecision),
            recommendation.ReasonCodes.Select(ToWire<ReasonCode, ReasonCodeV1>).ToArray(),
            recommendation.IsLegacy);

    private BadRequestObjectResult BadRequestProblem(string detail) =>
        BadRequest(ApiProblemDetails.CreateValidation(HttpContext, detail));

    private ObjectResult NotFoundProblem() =>
        StatusCode(
            StatusCodes.Status404NotFound,
            ApiProblemDetails.Create(
                HttpContext,
                ApiErrorDescriptors.ResourceNotFound,
                "The requested resource was not found."));

    private static TWire ToWire<TDomain, TWire>(TDomain value)
        where TDomain : struct, Enum
        where TWire : struct, Enum
    {
        if (Enum.TryParse<TWire>(value.ToString(), out var wire) && Enum.IsDefined(wire))
            return wire;

        throw new NotSupportedException(
            $"Domain enum value '{value}' is not mapped to v1 enum '{typeof(TWire).Name}'.");
    }
}
