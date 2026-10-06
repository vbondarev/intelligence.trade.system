using Intelligence.TradeSystem.Api.Contracts.V1.ExchangeAccounts;
using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Api.Mappers;

internal static class ExchangeAccountMapper
{
    public static ExchangeAccountListResponse ToListResponse(
        IReadOnlyList<ExchangeAccount> accounts) =>
        new(accounts.Select(ToResponse).ToArray());

    public static ExchangeAccountResponse ToResponse(ExchangeAccount account) => new(
        account.Id.Value,
        account.DisplayName,
        ToWireProvider(account.ExchangeId),
        ToWireStatus(account.ConnectionStatus),
        ToWireCapabilities(account.Capabilities),
        account.LastSyncedAt);

    private static ExchangeProvider ToWireProvider(ExchangeId exchangeId) => exchangeId switch
    {
        ExchangeId.Bybit => ExchangeProvider.Bybit,
        _ => throw new NotSupportedException(
            $"Exchange '{exchangeId}' is not mapped to a v1 wire contract."),
    };

    private static ExchangeAccountStatus ToWireStatus(
        ExchangeAccountConnectionStatus status) => status switch
    {
        ExchangeAccountConnectionStatus.Unknown => ExchangeAccountStatus.Unknown,
        ExchangeAccountConnectionStatus.Connected => ExchangeAccountStatus.Connected,
        ExchangeAccountConnectionStatus.Unavailable => ExchangeAccountStatus.Unavailable,
        ExchangeAccountConnectionStatus.Disabled => ExchangeAccountStatus.Disabled,
        _ => throw new NotSupportedException(
            $"Connection status '{status}' is not mapped to a v1 wire contract."),
    };

    private static ExchangeAccountCapability[] ToWireCapabilities(
        ExchangeAccountCapabilities capabilities)
    {
        var result = new List<ExchangeAccountCapability>(2);
        if (capabilities.HasFlag(ExchangeAccountCapabilities.ReadBalance))
            result.Add(ExchangeAccountCapability.ReadBalance);
        if (capabilities.HasFlag(ExchangeAccountCapabilities.ReadPositions))
            result.Add(ExchangeAccountCapability.ReadPositions);
        return result.ToArray();
    }
}
