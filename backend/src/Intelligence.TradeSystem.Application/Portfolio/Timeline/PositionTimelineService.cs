using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Portfolio.Timeline;

public sealed class PositionTimelineService(IPositionTimelineReadStore store)
{
    public async Task<PositionTimelinePage?> GetAsync(
        UserId userId,
        PositionTimelineQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var candidates = await store
            .ReadCandidatesAsync(userId, query, cancellationToken)
            .ConfigureAwait(false);
        if (candidates is null)
            return null;

        var ordered = candidates.PositionChanges
            .Concat(candidates.Evaluations)
            .Concat(candidates.Recommendations)
            .OrderByDescending(item => item.OccurredAt)
            .ThenByDescending(item => GetTypeRank(item.Kind))
            .ThenByDescending(item => GetSourceId(item))
            .ThenByDescending(item => item.PositionChange?.Sequence)
            .Take(query.PageSize + 1)
            .ToArray();

        var hasMore = ordered.Length > query.PageSize;
        var items = hasMore ? ordered[..query.PageSize] : ordered;
        PositionTimelineCursor? nextCursor = hasMore && items.Length > 0
            ? items[^1].ToCursor()
            : null;

        return new PositionTimelinePage(items, nextCursor, hasMore);
    }

    private static int GetTypeRank(PositionTimelineItemKind kind) => kind switch
    {
        PositionTimelineItemKind.Recommendation => 30,
        PositionTimelineItemKind.Evaluation => 20,
        PositionTimelineItemKind.PositionChange => 10,
        _ => throw new InvalidOperationException($"Unsupported timeline item kind '{kind}'."),
    };

    private static Guid? GetSourceId(PositionTimelineItem item) => item.Kind switch
    {
        PositionTimelineItemKind.Recommendation => item.Recommendation!.Id.Value,
        PositionTimelineItemKind.Evaluation => item.Evaluation!.Id.Value,
        PositionTimelineItemKind.PositionChange => null,
        _ => throw new InvalidOperationException($"Unsupported timeline item kind '{item.Kind}'."),
    };
}
