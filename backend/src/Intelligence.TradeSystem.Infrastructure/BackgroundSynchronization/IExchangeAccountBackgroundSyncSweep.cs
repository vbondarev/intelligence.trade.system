namespace Intelligence.TradeSystem.Infrastructure.BackgroundSynchronization;

public interface IExchangeAccountBackgroundSyncSweep
{
    Task<ExchangeAccountBackgroundSyncSweepResult> RunAsync(
        CancellationToken cancellationToken = default);
}
