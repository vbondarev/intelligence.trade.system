using Intelligence.TradeSystem.Api.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Intelligence.TradeSystem.Api.Realtime.V1;

[Authorize(Policy = TradeAuthorization.UserPolicy)]
public sealed class UpdatesHub : Hub
{
}
