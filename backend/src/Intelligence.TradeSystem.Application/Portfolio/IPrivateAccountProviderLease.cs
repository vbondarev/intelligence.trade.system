namespace Intelligence.TradeSystem.Application.Portfolio;

/// <summary>
/// Owns the lifetime of one credentials-bound private account provider.
/// </summary>
public interface IPrivateAccountProviderLease : IDisposable
{
    IPrivateAccountProvider Provider { get; }
}
