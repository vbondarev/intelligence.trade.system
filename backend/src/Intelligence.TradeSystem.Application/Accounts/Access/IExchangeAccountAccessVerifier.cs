using Intelligence.TradeSystem.Application.Accounts.Credentials;
using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Application.Accounts.Access;

/// <summary>
/// Не зависящая от биржи граница проверки временных учётных данных и доступа только для чтения.
/// </summary>
public interface IExchangeAccountAccessVerifier
{
    Task<ExchangeAccountAccessVerificationResult> VerifyAsync(
        ExchangeId exchange,
        ExchangeAccountCredentialSecret credentials,
        CancellationToken cancellationToken = default);
}
