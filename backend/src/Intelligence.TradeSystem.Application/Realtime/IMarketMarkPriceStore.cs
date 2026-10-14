using Intelligence.TradeSystem.Application.Realtime.State;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Realtime;

/// <summary>
/// Хранилище последних публичных mark price инструментов, общих для всех пользователей.
/// </summary>
public interface IMarketMarkPriceStore
{
    /// <returns>Сохранённые цены запрошенных символов; отсутствующие символы пропускаются.</returns>
    Task<IReadOnlyCollection<MarketMarkPrice>> GetAsync(
        ExchangeId exchange,
        MarketCategory category,
        IReadOnlyCollection<InstrumentId> symbols,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Сохраняет цены, которые новее сохранённых по <see cref="MarketMarkPrice.ObservedAt"/>.
    /// Наблюдение с той же или более ранней отметкой не применяется; из нескольких цен одного
    /// инструмента в пакете учитывается самая новая.
    /// </summary>
    /// <returns>Число применённых цен.</returns>
    Task<int> UpsertAsync(
        IReadOnlyCollection<MarketMarkPrice> prices,
        CancellationToken cancellationToken = default);
}
