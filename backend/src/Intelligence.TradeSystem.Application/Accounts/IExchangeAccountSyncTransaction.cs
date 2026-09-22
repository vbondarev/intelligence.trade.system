using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Accounts;

/// <summary>
/// Атомарная граница сохранения для одной синхронизации биржевой учётной записи.
/// Вызовы внешней биржи должны завершиться до входа в эту границу.
/// </summary>
public interface IExchangeAccountSyncTransaction
{
    Task LockAccountAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        CancellationToken cancellationToken = default);

    Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default);
}
