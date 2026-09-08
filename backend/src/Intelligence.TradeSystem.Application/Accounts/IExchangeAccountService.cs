using Intelligence.TradeSystem.Application.Accounts.Credentials;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Accounts;

public interface IExchangeAccountService
{
    Task<ExchangeAccountConnectionResult> ConnectAsync(
        ExchangeId exchange,
        ExchangeAccountCredentialSecret credentials,
        CancellationToken cancellationToken = default);

    Task<ExchangeAccount?> DisconnectAsync(
        ExchangeAccountId exchangeAccountId,
        CancellationToken cancellationToken = default);
}
