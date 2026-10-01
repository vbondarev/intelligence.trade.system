using Intelligence.TradeSystem.Api.Serialization;
using Intelligence.TradeSystem.Api.Contracts.V1.Common;
using Intelligence.TradeSystem.Application.Portfolio.Read;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Snapshots;
using Microsoft.AspNetCore.Http;

namespace Intelligence.TradeSystem.Api.RequestParsing;

internal static class PositionListQueryParser
{
    public static bool TryParse(
        Guid? exchangeAccountId,
        string? trackingState,
        string? symbol,
        string? side,
        int? pageSize,
        string? cursor,
        IQueryCollection query,
        out PositionReadQuery? result,
        out string? error)
    {
        result = null;
        error = null;

        if (query["exchangeAccountId"].Count > 1 ||
            (query.ContainsKey("exchangeAccountId") && exchangeAccountId is null) ||
            exchangeAccountId == Guid.Empty)
        {
            return Fail("The exchangeAccountId must be a non-empty GUID.", out result, out error);
        }

        if (query["pageSize"].Count > 1 ||
            (query.ContainsKey("pageSize") && pageSize is null) ||
            pageSize is < CursorPagination.MinPageSize or > CursorPagination.MaxPageSize)
        {
            return Fail(
                $"The pageSize must be between {CursorPagination.MinPageSize} and {CursorPagination.MaxPageSize}.",
                out result,
                out error);
        }

        var parsedTrackingState = ParseTrackingState(trackingState);
        if (query["trackingState"].Count > 1 ||
            (query.ContainsKey("trackingState") && string.IsNullOrEmpty(trackingState)) ||
            (trackingState is not null && parsedTrackingState is null))
        {
            return Fail("The trackingState value is invalid.", out result, out error);
        }

        var parsedSide = ParseSide(side);
        if (query["side"].Count > 1 ||
            (query.ContainsKey("side") && string.IsNullOrEmpty(side)) ||
            (side is not null && parsedSide is null))
        {
            return Fail("The side value is invalid.", out result, out error);
        }

        var normalizedSymbol = symbol?.Trim();
        if (query.ContainsKey("symbol") && string.IsNullOrWhiteSpace(normalizedSymbol))
        {
            return Fail("The symbol value cannot be empty.", out result, out error);
        }

        PositionReadCursor? parsedCursor = null;
        if (query.ContainsKey("cursor"))
        {
            if (!PositionCursorCodec.TryDecode(cursor ?? string.Empty, out var decodedCursor))
            {
                return Fail("The cursor value is invalid.", out result, out error);
            }

            parsedCursor = decodedCursor;
        }

        result = PositionReadQuery.Create(
            exchangeAccountId is { } accountId
                ? ExchangeAccountId.FromGuid(accountId)
                : null,
            parsedTrackingState,
            normalizedSymbol,
            parsedSide,
            pageSize ?? CursorPagination.DefaultPageSize,
            parsedCursor);
        return true;
    }

    private static PositionTrackingState? ParseTrackingState(string? value) => value switch
    {
        null => null,
        "active" => PositionTrackingState.Active,
        "unknown" => PositionTrackingState.Unknown,
        "stale" => PositionTrackingState.Stale,
        "closed" => PositionTrackingState.Closed,
        _ => null,
    };

    private static PositionSide? ParseSide(string? value) => value switch
    {
        null => null,
        "long" => PositionSide.Long,
        "short" => PositionSide.Short,
        _ => null,
    };

    private static bool Fail(
        string detail,
        out PositionReadQuery? result,
        out string? error)
    {
        result = null;
        error = detail;
        return false;
    }
}
