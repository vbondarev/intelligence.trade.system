using Intelligence.TradeSystem.Application.Realtime.Observations;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Realtime;

public interface IMarketMarkPriceSubscriptionProvider
{
    Task<IAsyncDisposable> SubscribeAsync(
        InstrumentId symbol,
        Action<MarketMarkPriceObservation> onObservation,
        Action onFailure,
        CancellationToken cancellationToken = default);
}
