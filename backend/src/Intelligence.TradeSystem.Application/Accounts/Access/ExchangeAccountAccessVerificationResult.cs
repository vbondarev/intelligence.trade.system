using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Application.Accounts.Access;

public sealed record ExchangeAccountAccessVerificationResult(
    ExchangeAccountAccessVerificationStatus Status,
    ExchangeAccountCapabilities Capabilities)
{
    public static ExchangeAccountAccessVerificationResult Verified(
        ExchangeAccountCapabilities capabilities) =>
        new(ExchangeAccountAccessVerificationStatus.Verified, capabilities);

    public static ExchangeAccountAccessVerificationResult Failed(
        ExchangeAccountAccessVerificationStatus status) =>
        new(status, ExchangeAccountCapabilities.None);
}
