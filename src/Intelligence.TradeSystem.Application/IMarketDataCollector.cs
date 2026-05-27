using Intelligence.TradeSystem.Abstractions;
using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Application;

/// <summary>
/// Собирает полный пакет сырых рыночных и аккаунтных данных по инструменту.
/// </summary>
public interface IMarketDataCollector
{
    /// <summary>
    /// Выполняет сбор всех данных, необходимых для построения <c>MarketAnalysisSnapshot</c>.
    /// </summary>
    /// <param name="exchangeId">Идентификатор биржи, для которой выполняется сбор.</param>
    /// <param name="symbol">Тикер инструмента. Например: <c>BTCUSDT</c>.</param>
    /// <param name="category">Категория рынка инструмента.</param>
    /// <param name="cancellationToken">Токен отмены асинхронной операции.</param>
    /// <returns>Нормализованный пакет сырых данных для downstream-оркестрации.</returns>
    /// <exception cref="ArgumentException">Если <paramref name="symbol"/> пустой или состоит из пробелов.</exception>
    /// <exception cref="NotSupportedException">Если <paramref name="exchangeId"/> не поддерживается текущей конфигурацией.</exception>
    Task<CollectedMarketData> CollectAsync(
        ExchangeId exchangeId,
        string symbol,
        MarketCategory category,
        CancellationToken cancellationToken = default);
}
