namespace Intelligence.TradeSystem.Api.Contracts.V1.Portfolio;

/// <summary>Текущая сводка портфеля одного биржевого аккаунта.</summary>
/// <remarks>
/// <see cref="TotalUnrealizedPnl"/> — нормализованный биржей account-level PnL в USD, а не сумма
/// position-level PnL. <see cref="Exposures"/> сгруппированы по активу расчёта и не суммируются
/// между активами. <see cref="IsFresh"/> оценивается на момент ответа.
/// </remarks>
public sealed record PortfolioResponse(
    Guid ExchangeAccountId,
    DateTimeOffset CalculatedAt,
    PortfolioCapitalResponse Capital,
    decimal? GrossExposure,
    decimal? LongExposure,
    decimal? ShortExposure,
    decimal? NetExposure,
    decimal? TotalUnrealizedPnl,
    decimal? UsedCapital,
    decimal? FreeCapital,
    decimal? FreeCapitalPercent,
    decimal? GrossExposureToEquityPercent,
    decimal? LargestPositionConcentrationPercent,
    Guid? LargestPositionId,
    bool PositionsFullyReconciled,
    bool IsComplete,
    bool IsFresh,
    int CurrentPositionCount,
    IReadOnlyList<PortfolioExposureResponse> Exposures);
