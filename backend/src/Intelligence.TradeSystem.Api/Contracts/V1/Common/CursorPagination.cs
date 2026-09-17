namespace Intelligence.TradeSystem.Api.Contracts.V1.Common;

/// <summary>
/// Общие ограничения page size для cursor-пагинации API v1.
/// </summary>
public static class CursorPagination
{
    public const int MinPageSize = 1;
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;

    /// <summary>
    /// Проверяет optional page size без применения endpoint-specific defaults.
    /// </summary>
    public static bool IsValidPageSize(int? pageSize) =>
        pageSize is null or (>= MinPageSize and <= MaxPageSize);
}
