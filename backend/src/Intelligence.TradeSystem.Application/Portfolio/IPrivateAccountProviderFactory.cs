using Intelligence.TradeSystem.Application.Accounts.Credentials;
using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Application.Portfolio;

/// <summary>
/// Creates a private provider for one exchange account without exposing transport types.
/// </summary>
public interface IPrivateAccountProviderFactory
{
    IPrivateAccountProviderLease Create(
        ExchangeId exchange,
        ExchangeAccountCredential credentials);
}
