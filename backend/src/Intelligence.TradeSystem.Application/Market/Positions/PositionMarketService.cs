using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Market.Positions;

public sealed class PositionMarketService(
    IPositionMarketIdentityStore identityStore,
    IMarketSnapshotService marketSnapshotService,
    IMarketDataProvider marketDataProvider)
{
    public const int DefaultCandleLimit = 200;
    public const int MaxCandleLimit = 500;

    public async Task<PositionMarketContext?> GetMarketAsync(
        UserId userId,
        PositionId positionId,
        CancellationToken cancellationToken = default)
    {
        var identity = await ResolveIdentityAsync(userId, positionId, cancellationToken)
            .ConfigureAwait(false);
        if (identity is null)
        {
            return null;
        }

        EnsureSupportedExchange(identity.ExchangeId);
        var snapshot = await marketSnapshotService
            .BuildSnapshotAsync(
                identity.ExchangeId,
                identity.Symbol,
                identity.MarketCategory,
                cancellationToken)
            .ConfigureAwait(false);

        return new PositionMarketContext(identity, snapshot);
    }

    public async Task<PositionCandles?> GetCandlesAsync(
        UserId userId,
        PositionId positionId,
        KlineInterval interval,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > MaxCandleLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                limit,
                $"Candle limit must be between 1 and {MaxCandleLimit}.");
        }

        var identity = await ResolveIdentityAsync(userId, positionId, cancellationToken)
            .ConfigureAwait(false);
        if (identity is null)
        {
            return null;
        }

        EnsureSupportedExchange(identity.ExchangeId);
        var klines = await marketDataProvider
            .GetKlinesAsync(
                identity.Symbol,
                identity.MarketCategory,
                interval,
                startTime: null,
                endTime: null,
                limit,
                cancellationToken)
            .ConfigureAwait(false);

        if (klines.Count == 0)
        {
            throw new MarketDataUnavailableException(
                $"No candles were returned for symbol '{identity.Symbol}'.");
        }

        return new PositionCandles(
            identity,
            interval,
            klines.OrderBy(kline => kline.StartTime).ToArray());
    }

    private async Task<PositionMarketIdentity?> ResolveIdentityAsync(
        UserId userId,
        PositionId positionId,
        CancellationToken cancellationToken)
    {
        if (userId == default)
        {
            throw new ArgumentException("UserId must be initialized.", nameof(userId));
        }

        if (positionId == default)
        {
            throw new ArgumentException("PositionId must be initialized.", nameof(positionId));
        }

        return await identityStore
            .GetAsync(userId, positionId, cancellationToken)
            .ConfigureAwait(false);
    }

    private static void EnsureSupportedExchange(ExchangeId exchangeId)
    {
        if (exchangeId != ExchangeId.Bybit)
        {
            throw new NotSupportedException(
                $"Exchange '{exchangeId}' is not supported by the current market service configuration.");
        }
    }
}
