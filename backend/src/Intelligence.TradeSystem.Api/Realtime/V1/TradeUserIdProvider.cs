using Intelligence.TradeSystem.Api.Authentication;
using Microsoft.AspNetCore.SignalR;

namespace Intelligence.TradeSystem.Api.Realtime.V1;

internal sealed class TradeUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        return TradeAuthorization.TryGetUserId(connection.User, out var userId)
            ? userId.Value.ToString("D")
            : null;
    }
}
