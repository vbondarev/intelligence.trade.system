using Microsoft.Extensions.Hosting;

namespace Intelligence.TradeSystem.Infrastructure.Realtime;

public sealed class ExchangeRealtimeObservationWorker(
    ExchangeRealtimeObservationSupervisor supervisor)
    : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        supervisor.RunAsync(stoppingToken);
}
