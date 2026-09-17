namespace Intelligence.TradeSystem.Api.Contracts.V1.Common;

/// <summary>
/// Общий response-контракт cursor pagination для пользовательского API v1.
/// </summary>
/// <typeparam name="TItem">Тип элемента страницы.</typeparam>
public sealed record CursorPage<TItem>(
    IReadOnlyList<TItem> Items,
    string? NextCursor,
    bool HasMore);
