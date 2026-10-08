namespace Intelligence.TradeSystem.Api.Contracts.V1.Portfolio;

/// <summary>Экспозиция текущих позиций портфеля в пределах одного актива расчёта.</summary>
/// <remarks>
/// Значения выражены в <see cref="SettlementAsset"/>. Агрегат равен <see langword="null"/>, если
/// стоимость хотя бы одной входящей позиции неизвестна.
/// </remarks>
public sealed record PortfolioExposureResponse(
    string SettlementAsset,
    decimal? GrossExposure,
    decimal? LongExposure,
    decimal? ShortExposure);
