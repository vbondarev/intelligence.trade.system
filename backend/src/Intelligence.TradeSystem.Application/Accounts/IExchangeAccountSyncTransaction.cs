namespace Intelligence.TradeSystem.Application.Accounts;

/// <summary>
/// Atomic persistence boundary for one exchange-account synchronization.
/// External exchange calls must happen before this boundary is entered.
/// </summary>
public interface IExchangeAccountSyncTransaction
{
    Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default);
}
