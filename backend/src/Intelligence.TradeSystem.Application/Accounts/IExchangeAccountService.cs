using Intelligence.TradeSystem.Application.Accounts.Credentials;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Accounts;

public interface IExchangeAccountService
{
    Task<IReadOnlyList<ExchangeAccount>> ListActiveAsync(
        UserId userId,
        CancellationToken cancellationToken = default);

    Task<ExchangeAccountConnectionResult> ConnectAsync(
        UserId userId,
        ExchangeId exchange,
        ExchangeAccountCredentialSecret credentials,
        CancellationToken cancellationToken = default);

    Task<ExchangeAccount?> DisconnectAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        CancellationToken cancellationToken = default);

    Task<ExchangeAccountVerificationResult> VerifyAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        CancellationToken cancellationToken = default);

    Task<ExchangeAccountCredentialRotationResult> RotateCredentialsAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        ExchangeAccountCredentialSecret replacement,
        CancellationToken cancellationToken = default);
}
