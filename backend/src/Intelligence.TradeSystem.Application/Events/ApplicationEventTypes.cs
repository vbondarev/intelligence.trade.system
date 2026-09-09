namespace Intelligence.TradeSystem.Application.Events;

public static class ApplicationEventTypes
{
    public const string PositionOpened = "position.opened";
    public const string PositionChanged = "position.changed";
    public const string PositionClosed = "position.closed";
    public const string ExchangeAccountSyncDegraded = "exchange-account.sync-degraded";
}

public static class ApplicationEventSchemaVersions
{
    public const int V1 = 1;
}
