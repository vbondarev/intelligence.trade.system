using Intelligence.TradeSystem.Api.Serialization;
using Intelligence.TradeSystem.Api.Contracts.V1.Common;
using Intelligence.TradeSystem.Application.Portfolio.Timeline;
using Intelligence.TradeSystem.Domain.Identity;
using Microsoft.AspNetCore.Http;

namespace Intelligence.TradeSystem.Api.RequestParsing;

internal static class PositionTimelineQueryParser
{
    public static bool TryParse(
        PositionId positionId,
        int? pageSize,
        string[]? types,
        string? cursor,
        IQueryCollection query,
        out PositionTimelineQuery? result,
        out string? error)
    {
        result = null;
        error = null;

        if (query["pageSize"].Count > 1 ||
            (query.ContainsKey("pageSize") && pageSize is null) ||
            pageSize is < CursorPagination.MinPageSize or > CursorPagination.MaxPageSize)
        {
            return Fail(
                $"The pageSize must be between {CursorPagination.MinPageSize} and {CursorPagination.MaxPageSize}.",
                out result,
                out error);
        }

        if (!TryParseKinds(types, out var kinds))
        {
            return Fail("The type value is invalid.", out result, out error);
        }

        PositionTimelineCursor? parsedCursor = null;
        if (query.ContainsKey("cursor"))
        {
            if (!PositionTimelineCursorCodec.TryDecode(cursor ?? string.Empty, out var decodedCursor))
            {
                return Fail("The cursor value is invalid.", out result, out error);
            }

            if (!kinds.Contains(decodedCursor.Kind))
            {
                return Fail(
                    "The cursor is incompatible with the selected timeline types.",
                    out result,
                    out error);
            }

            parsedCursor = decodedCursor;
        }

        result = new PositionTimelineQuery(
            positionId,
            pageSize ?? CursorPagination.DefaultPageSize,
            kinds,
            parsedCursor);
        return true;
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
        foreach (var value in values)
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

    private static bool Fail(
        string detail,
        out PositionTimelineQuery? result,
        out string? error)
    {
        result = null;
        error = detail;
        return false;
    }
}
