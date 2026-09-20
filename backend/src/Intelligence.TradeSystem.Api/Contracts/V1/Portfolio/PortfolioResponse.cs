namespace Intelligence.TradeSystem.Api.Contracts.V1.Portfolio;

/// <summary>Текущая сводка портфеля одного биржевого аккаунта.</summary>
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
    bool IsFresh);
