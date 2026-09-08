namespace Intelligence.TradeSystem.Api.Contracts;

public sealed class ConnectExchangeAccountRequest
{
    public string? ApiKey { get; init; }
    public string? ApiSecret { get; init; }
}
