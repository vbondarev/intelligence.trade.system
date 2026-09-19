using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Accounts.Access;

public sealed record ExchangeAccountAccessVerificationResult(
    ExchangeAccountAccessVerificationStatus Status,
    ExchangeAccountCapabilities Capabilities,
    ExchangeAccountProviderIdentity? ProviderIdentity)
{
    public static ExchangeAccountAccessVerificationResult Verified(
        ExchangeAccountProviderIdentity providerIdentity,
        ExchangeAccountCapabilities capabilities) =>
        new(ExchangeAccountAccessVerificationStatus.Verified, capabilities, providerIdentity);

    public static ExchangeAccountAccessVerificationResult Failed(
        ExchangeAccountAccessVerificationStatus status) =>
        new(status, ExchangeAccountCapabilities.None, null);
}
