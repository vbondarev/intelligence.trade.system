using Intelligence.TradeSystem.Application.Accounts.Credentials;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Infrastructure.Security;

internal interface IExchangeCredentialProtector
{
    ProtectedCredentialEnvelope Protect(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        ExchangeAccountCredentialSecret secret);

    ExchangeAccountCredentialSecret Unprotect(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        ProtectedCredentialEnvelope envelope);
}
