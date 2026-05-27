using Intelligence.TradeSystem.Abstractions;
using Intelligence.TradeSystem.Application;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Api.Tests.Helpers;

/// <summary>
/// Тестовая реализация <see cref="IMarketAnalysisService"/>,
/// возвращающая заранее сконфигурированный снапшот.
/// Позволяет переиспользовать один экземпляр <c>WebApplicationFactory</c>
/// для всего тест-класса вместо создания нового хоста на каждый тест.
/// </summary>
public sealed class ConfigurableMarketAnalysisService : IMarketAnalysisService
{
    private volatile MarketAnalysisSnapshot? _snapshot;

    /// <summary>
    /// Устанавливает снапшот, который будет возвращён при следующем вызове
    /// <see cref="BuildSnapshotAsync"/>.
    /// </summary>
    public void Configure(MarketAnalysisSnapshot snapshot) => _snapshot = snapshot;

    /// <inheritdoc/>
    public Task<MarketAnalysisSnapshot> BuildSnapshotAsync(
        ExchangeId exchangeId,
        string symbol,
        MarketCategory category,
        CancellationToken cancellationToken = default)
    {
        var snapshot = _snapshot
            ?? throw new InvalidOperationException(
                $"{nameof(ConfigurableMarketAnalysisService)} has not been configured. " +
                $"Call {nameof(Configure)} before making a request.");

        return Task.FromResult(snapshot);
    }
}
