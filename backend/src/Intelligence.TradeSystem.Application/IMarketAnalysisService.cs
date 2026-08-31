using Intelligence.TradeSystem.Abstractions;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.MarketIntelligence.Snapshots;

namespace Intelligence.TradeSystem.Application;

/// <summary>
/// Оркестрирует сбор сырых данных и построение финального <see cref="MarketAnalysisSnapshot"/>.
/// </summary>
public interface IMarketAnalysisService
{
    /// <summary>
    /// Собирает финальный рыночный снимок по инструменту.
    /// </summary>
    /// <param name="exchangeId">Идентификатор биржи, для которой строится снимок.</param>
    /// <param name="symbol">Тикер инструмента. Например: <c>BTCUSDT</c>.</param>
    /// <param name="category">Категория рынка инструмента.</param>
    /// <param name="cancellationToken">Токен отмены асинхронной операции.</param>
    /// <returns>Корневой агрегат <see cref="MarketAnalysisSnapshot"/>.</returns>
    /// <exception cref="ArgumentException">Если <paramref name="symbol"/> пустой или состоит из пробелов.</exception>
    /// <exception cref="NotSupportedException">Если <paramref name="exchangeId"/> не поддерживается текущей конфигурацией.</exception>
    /// <exception cref="InvalidOperationException">Если не удалось собрать критически важные входные данные для анализа.</exception>
    Task<MarketAnalysisSnapshot> BuildSnapshotAsync(
        ExchangeId exchangeId,
        string symbol,
        MarketCategory category,
        CancellationToken cancellationToken = default);
}
