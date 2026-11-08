using Intelligence.TradeSystem.Api.Contracts.V1.ExchangeAccounts;
using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Api.Mappers;

internal static class ExchangeAccountMapper
{
    public static ExchangeAccountListResponse ToListResponse(
        IReadOnlyList<ExchangeAccount> accounts,
        IReadOnlyDictionary<ExchangeAccountId, RealtimeObservationReadModel> observations) =>
        new(accounts
            .Select(account => ToResponse(account, observations[account.Id]))
            .ToArray());

    public static ExchangeAccountResponse ToResponse(
        ExchangeAccount account,
        RealtimeObservationReadModel observation) =>
        new(
            account.Id.Value,
            account.DisplayName,
            ToWireProvider(account.ExchangeId),
            ToWireStatus(account.ConnectionStatus),
            ToWireCapabilities(account.Capabilities),
            account.LastSyncedAt)
        {
            RealtimeObservation = new(
                ToWireStatus(observation.Status),
                observation.LastConnectedAt,
                observation.LastHeartbeatAt,
                observation.ReasonCode),
        };

    private static RealtimeObservationStatusV1 ToWireStatus(
        RealtimeObservationReadStatus status) => status switch
    {
        RealtimeObservationReadStatus.Unknown => RealtimeObservationStatusV1.Unknown,
        RealtimeObservationReadStatus.Initializing => RealtimeObservationStatusV1.Initializing,
        RealtimeObservationReadStatus.Connected => RealtimeObservationStatusV1.Connected,
        RealtimeObservationReadStatus.Reconnecting => RealtimeObservationStatusV1.Reconnecting,
        RealtimeObservationReadStatus.Degraded => RealtimeObservationStatusV1.Degraded,
        RealtimeObservationReadStatus.Stopped => RealtimeObservationStatusV1.Stopped,
        _ => throw new NotSupportedException(
            $"Observation status '{status}' is not mapped to a v1 wire contract."),
    };

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
