using Intelligence.TradeSystem.Application.Portfolio.Read;
using Intelligence.TradeSystem.Application.Realtime.State;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Realtime;

public sealed class MarketMarkPriceService(
    IMarketMarkPriceStore store,
    TimeProvider timeProvider)
{
    public async Task<IReadOnlyDictionary<PositionId, MarketValuation>> GetForPositionsAsync(
        IReadOnlyCollection<PositionReadListItem> positions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(positions);

        var eligible = positions
            .Where(IsMarketValuationApplicable)
            .ToArray();
        var symbols = eligible
            .Select(position => InstrumentId.From(position.Symbol))
            .Distinct()
            .ToArray();

        IReadOnlyCollection<MarketMarkPrice> storedPrices = symbols.Length == 0
            ? []
            : await store.GetAsync(
                ExchangeId.Bybit,
                MarketCategory.Linear,
                symbols,
                cancellationToken).ConfigureAwait(false);
        var pricesBySymbol = storedPrices.ToDictionary(price => price.Symbol);
        var calculatedAt = timeProvider.GetUtcNow();

        return positions.ToDictionary(
            position => position.Id,
            position =>
            {
                var price = IsMarketValuationApplicable(position)
                    && pricesBySymbol.TryGetValue(InstrumentId.From(position.Symbol), out var storedPrice)
                        ? storedPrice
                        : null;
                return MarketValuationProjection.Project(
                    position.ExchangeId,
                    position.MarketCategory,
                    position.SettlementAsset,
                    position.Size,
                    position.TrackingState,
                    price,
                    calculatedAt);
            });
    }

    private static bool IsMarketValuationApplicable(PositionReadListItem position) =>
        position.ExchangeId == ExchangeId.Bybit
        && position.MarketCategory == MarketCategory.Linear
        && position.TrackingState != PositionTrackingState.Closed
        && (string.Equals(position.SettlementAsset.Value, "USDT", StringComparison.OrdinalIgnoreCase)
            || string.Equals(position.SettlementAsset.Value, "USDC", StringComparison.OrdinalIgnoreCase));
}
