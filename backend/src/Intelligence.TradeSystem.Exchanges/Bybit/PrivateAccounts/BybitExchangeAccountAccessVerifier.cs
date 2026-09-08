using Intelligence.TradeSystem.Application.Accounts.Access;
using Intelligence.TradeSystem.Application.Accounts.Credentials;
using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Exchanges.Bybit.ClientFactory;

namespace Intelligence.TradeSystem.Exchanges.Bybit.PrivateAccounts;

public sealed class BybitExchangeAccountAccessVerifier(
    BybitPrivateAccountProviderFactory providerFactory)
    : IExchangeAccountAccessVerifier
{
    public async Task<ExchangeAccountAccessVerificationResult> VerifyAsync(
        ExchangeId exchange,
        ExchangeAccountCredentialSecret credentials,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        if (exchange != ExchangeId.Bybit)
        {
            return ExchangeAccountAccessVerificationResult.Failed(
                ExchangeAccountAccessVerificationStatus.UnsupportedExchange);
        }

        var verificationTask = credentials.Use(
            (apiKey, apiSecret) => VerifyBybitAsync(
                new BybitCredentials(apiKey, apiSecret),
                cancellationToken));
        return await verificationTask.ConfigureAwait(false);
    }

    private async Task<ExchangeAccountAccessVerificationResult> VerifyBybitAsync(
        BybitCredentials credentials,
        CancellationToken cancellationToken)
    {
        using var lease = providerFactory.Create(credentials);

        var metadata = await lease.Provider
            .GetApiKeyAccessMetadataAsync(cancellationToken)
            .ConfigureAwait(false);
        if (metadata.Status != ApiKeyAccessMetadataObservationStatus.Complete)
        {
            return FromFailure(metadata.Failure);
        }

        // Bybit's permissions arrays describe product scopes, while readOnly=1 is
        // the authoritative flag that disables every write operation for the key.
        if (metadata.Metadata is not
            {
                IsReadOnly: true,
                HasReadBalancePermission: true,
                HasReadPositionsPermission: true,
            })
        {
            return ExchangeAccountAccessVerificationResult.Failed(
                ExchangeAccountAccessVerificationStatus.PermissionsRejected);
        }

        var balance = await lease.Provider
            .GetWalletBalanceAsync(AccountType.Unified, cancellationToken)
            .ConfigureAwait(false);
        if (balance.Status != AccountBalanceObservationStatus.Complete)
        {
            return FromFailure(balance.Failure);
        }

        var positions = await lease.Provider
            .GetOpenPositionsAsync(MarketCategory.Linear, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (positions.Status != OpenPositionsObservationStatus.Complete)
        {
            return ExchangeAccountAccessVerificationResult.Failed(
                ExchangeAccountAccessVerificationStatus.Unavailable);
        }

        return ExchangeAccountAccessVerificationResult.Verified(
            ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions);
    }

    private static ExchangeAccountAccessVerificationResult FromFailure(ExchangeFailure? failure)
    {
        var status = failure?.Kind switch
        {
            ExchangeFailureKind.InvalidCredentials =>
                ExchangeAccountAccessVerificationStatus.InvalidCredentials,
            ExchangeFailureKind.PermissionDenied =>
                ExchangeAccountAccessVerificationStatus.PermissionsRejected,
            _ => ExchangeAccountAccessVerificationStatus.Unavailable,
        };

        return ExchangeAccountAccessVerificationResult.Failed(status);
    }
}
