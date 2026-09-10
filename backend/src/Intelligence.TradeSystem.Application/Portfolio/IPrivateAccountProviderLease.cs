namespace Intelligence.TradeSystem.Application.Portfolio;

/// <summary>
/// Управляет временем жизни одного приватного провайдера учётной записи, связанного с учётными данными.
/// </summary>
public interface IPrivateAccountProviderLease : IDisposable
{
    IPrivateAccountProvider Provider { get; }
}
