using Intelligence.TradeSystem.Application.Accounts.Credentials;
using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Application.Accounts.Access;

/// <summary>
/// Exchange-neutral boundary for checking transient credentials and read-only access.
/// </summary>
public interface IExchangeAccountAccessVerifier
{
    Task<ExchangeAccountAccessVerificationResult> VerifyAsync(
        ExchangeId exchange,
        ExchangeAccountCredentialSecret credentials,
        CancellationToken cancellationToken = default);
}
