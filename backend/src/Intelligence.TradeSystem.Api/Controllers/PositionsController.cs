using Intelligence.TradeSystem.Api.Contracts.V1.Common;
using Intelligence.TradeSystem.Api.Contracts.V1.Positions;
using Intelligence.TradeSystem.Api.Errors;
using Intelligence.TradeSystem.Api.Serialization;
using Intelligence.TradeSystem.Application.Portfolio.Read;
using Intelligence.TradeSystem.Application.Users;
using Intelligence.TradeSystem.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DomainMarketCategory = Intelligence.TradeSystem.Domain.MarketCategory;
using DomainPositionSide = Intelligence.TradeSystem.Domain.Snapshots.PositionSide;
using DomainPositionTrackingState = Intelligence.TradeSystem.Domain.PositionTrackingState;
using WireMarketCategory = Intelligence.TradeSystem.Api.Contracts.V1.Positions.MarketCategoryV1;
using WirePositionSide = Intelligence.TradeSystem.Api.Contracts.V1.Positions.PositionSideV1;
using WirePositionTrackingState = Intelligence.TradeSystem.Api.Contracts.V1.Positions.PositionTrackingStateV1;

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
        if (Request.Query["exchangeAccountId"].Count > 1 ||
            (Request.Query.ContainsKey("exchangeAccountId") && exchangeAccountId is null) ||
            exchangeAccountId == Guid.Empty)
        {
            return BadRequestProblem("The exchangeAccountId must be a non-empty GUID.");
        }

        if (Request.Query["pageSize"].Count > 1 ||
            (Request.Query.ContainsKey("pageSize") && pageSize is null) ||
            pageSize is < CursorPagination.MinPageSize or > CursorPagination.MaxPageSize)
        {
            return BadRequestProblem(
                $"The pageSize must be between {CursorPagination.MinPageSize} and {CursorPagination.MaxPageSize}.");
        }

        if (Request.Query["trackingState"].Count > 1 ||
            (Request.Query.ContainsKey("trackingState") && string.IsNullOrEmpty(trackingState)) ||
            !TryParseTrackingState(trackingState, out var parsedTrackingState))
        {
            return BadRequestProblem("The trackingState value is invalid.");
        }

        if (Request.Query["side"].Count > 1 ||
            (Request.Query.ContainsKey("side") && string.IsNullOrEmpty(side)) ||
            !TryParseSide(side, out var parsedSide))
        {
            return BadRequestProblem("The side value is invalid.");
        }

        var normalizedSymbol = symbol?.Trim();
        if (Request.Query.ContainsKey("symbol") &&
            string.IsNullOrWhiteSpace(normalizedSymbol))
        {
            return BadRequestProblem("The symbol value cannot be empty.");
        }

        PositionReadCursor? parsedCursor = null;
        if (Request.Query.ContainsKey("cursor"))
        {
            if (!PositionCursorCodec.TryDecode(cursor ?? string.Empty, out var decodedCursor))
            {
                return BadRequestProblem("The cursor value is invalid.");
            }

            parsedCursor = decodedCursor;
        }

        var query = PositionReadQuery.Create(
            exchangeAccountId is { } accountId
                ? ExchangeAccountId.FromGuid(accountId)
                : null,
            parsedTrackingState,
            normalizedSymbol,
            parsedSide,
            pageSize ?? CursorPagination.DefaultPageSize,
            parsedCursor);
        var page = await positionReadService
            .ListAsync(currentUserContext.UserId, query, cancellationToken)
            .ConfigureAwait(false);

        return Ok(new CursorPage<PositionListItemResponse>(
            page.Items.Select(ToResponse).ToArray(),
            page.NextCursor is { } nextCursor
                ? PositionCursorCodec.Encode(nextCursor)
                : null,
            page.HasMore));
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
            : Ok(ToResponse(detail));
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

    private static PositionListItemResponse ToResponse(PositionReadListItem item) => new(
        item.Id.Value,
        item.ExchangeAccountId.Value,
        item.Symbol,
        ToWireSide(item.Side),
        ToWireTrackingState(item.TrackingState),
        item.Size,
        item.AverageEntryPrice,
        item.MarkPrice,
        item.PositionValue,
        item.UnrealizedPnl,
        item.Leverage,
        item.LiquidationPrice,
        item.FirstDetectedAt,
        item.LastObservedAt,
        item.ClosedAt);

    private static PositionResponse ToResponse(PositionReadDetail detail)
    {
        var item = detail.ListItem;
        return new PositionResponse(
            item.Id.Value,
            item.ExchangeAccountId.Value,
            item.Symbol,
            ToWireSide(item.Side),
            ToWireTrackingState(item.TrackingState),
            item.Size,
            item.AverageEntryPrice,
            item.MarkPrice,
            item.PositionValue,
            item.UnrealizedPnl,
            item.Leverage,
            item.LiquidationPrice,
            item.FirstDetectedAt,
            item.LastObservedAt,
            item.ClosedAt,
            ToWireMarketCategory(detail.MarketCategory),
            detail.BreakEvenPrice,
            detail.TakeProfit,
            detail.StopLoss,
            detail.TrailingStop);
    }

    private static bool TryParseTrackingState(
        string? value,
        out DomainPositionTrackingState? trackingState)
    {
        trackingState = value switch
        {
            null => null,
            "active" => DomainPositionTrackingState.Active,
            "unknown" => DomainPositionTrackingState.Unknown,
            "stale" => DomainPositionTrackingState.Stale,
            "closed" => DomainPositionTrackingState.Closed,
            _ => null,
        };
        return value is null || trackingState.HasValue;
    }

    private static bool TryParseSide(string? value, out DomainPositionSide? side)
    {
        side = value switch
        {
            null => null,
            "long" => DomainPositionSide.Long,
            "short" => DomainPositionSide.Short,
            _ => null,
        };
        return value is null || side.HasValue;
    }

    private static WirePositionSide ToWireSide(DomainPositionSide side) => side switch
    {
        DomainPositionSide.Long => WirePositionSide.Long,
        DomainPositionSide.Short => WirePositionSide.Short,
        _ => throw new NotSupportedException($"Position side '{side}' is not mapped to a v1 wire contract."),
    };

    private static WirePositionTrackingState ToWireTrackingState(
        DomainPositionTrackingState trackingState) => trackingState switch
    {
        DomainPositionTrackingState.Active => WirePositionTrackingState.Active,
        DomainPositionTrackingState.Unknown => WirePositionTrackingState.Unknown,
        DomainPositionTrackingState.Stale => WirePositionTrackingState.Stale,
        DomainPositionTrackingState.Closed => WirePositionTrackingState.Closed,
        _ => throw new NotSupportedException(
            $"Position tracking state '{trackingState}' is not mapped to a v1 wire contract."),
    };

    private static WireMarketCategory ToWireMarketCategory(DomainMarketCategory category) => category switch
    {
        DomainMarketCategory.Linear => WireMarketCategory.Linear,
        DomainMarketCategory.Inverse => WireMarketCategory.Inverse,
        _ => throw new NotSupportedException(
            $"Market category '{category}' is not mapped to a v1 wire contract."),
    };
}
