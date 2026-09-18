namespace Intelligence.TradeSystem.Application.Accounts;

/// <summary>Атомарная граница сохранения lifecycle-операций биржевой учётной записи.</summary>
public interface IExchangeAccountLifecycleTransaction
{
    Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default);
}
