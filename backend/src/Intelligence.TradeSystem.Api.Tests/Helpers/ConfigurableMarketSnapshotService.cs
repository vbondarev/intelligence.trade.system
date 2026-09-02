using Intelligence.TradeSystem.Application;
using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Api.Tests.Helpers;

/// <summary>
/// Тестовая реализация <see cref="IMarketSnapshotService"/>,
/// возвращающая заранее сконфигурированный снапшот.
/// Позволяет переиспользовать один экземпляр <c>WebApplicationFactory</c>
/// для всего тест-класса вместо создания нового хоста на каждый тест.
/// </summary>
public sealed class ConfigurableMarketSnapshotService : IMarketSnapshotService
{
    private volatile MarketSnapshot? _snapshot;

    /// <summary>
    /// Устанавливает снапшот, который будет возвращён при следующем вызове
    /// <see cref="BuildSnapshotAsync"/>.
    /// </summary>
    public void Configure(MarketSnapshot snapshot) => _snapshot = snapshot;

    /// <inheritdoc/>
    public Task<MarketSnapshot> BuildSnapshotAsync(
        ExchangeId exchangeId,
        string symbol,
        MarketCategory category,
        CancellationToken cancellationToken = default)
    {
        var snapshot = _snapshot
            ?? throw new InvalidOperationException(
                $"{nameof(ConfigurableMarketSnapshotService)} has not been configured. " +
                $"Call {nameof(Configure)} before making a request.");

        return Task.FromResult(snapshot);
    }
}
