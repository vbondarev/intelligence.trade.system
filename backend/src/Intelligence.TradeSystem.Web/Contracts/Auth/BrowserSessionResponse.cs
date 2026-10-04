using System.Text.Json.Serialization;

namespace Intelligence.TradeSystem.Web.Contracts.Auth;

/// <summary>
/// Состояние browser session для React-клиента. Не содержит tokens и claims Identity.
/// </summary>
public sealed record BrowserSessionResponse(
    bool Authenticated,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BrowserSessionUser? User)
{
    public static BrowserSessionResponse Anonymous { get; } = new(false, null);
}

public sealed record BrowserSessionUser(Guid UserId, string Subject);
