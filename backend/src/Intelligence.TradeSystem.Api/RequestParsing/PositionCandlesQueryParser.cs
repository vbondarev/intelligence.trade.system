using Intelligence.TradeSystem.Application.Market.Positions;
using Intelligence.TradeSystem.Api.Serialization;
using Intelligence.TradeSystem.Domain;
using Microsoft.AspNetCore.Http;

namespace Intelligence.TradeSystem.Api.RequestParsing;

internal static class PositionCandlesQueryParser
{
    public static bool TryParse(
        string interval,
        int? limit,
        IQueryCollection query,
        out KlineInterval parsedInterval,
        out int parsedLimit,
        out string? error)
    {
        parsedInterval = default;
        parsedLimit = default;
        error = null;

        if (query["interval"].Count != 1 ||
            string.IsNullOrWhiteSpace(interval) ||
            !CandleIntervalV1Codec.TryParse(interval, out parsedInterval))
        {
            error = "The interval value is invalid.";
            return false;
        }

        if (query["limit"].Count > 1 ||
            (query.ContainsKey("limit") && limit is null) ||
            limit is < 1 or > PositionMarketService.MaxCandleLimit)
        {
            error = $"The limit must be between 1 and {PositionMarketService.MaxCandleLimit}.";
            return false;
        }

        parsedLimit = limit ?? PositionMarketService.DefaultCandleLimit;
        return true;
    }
}
