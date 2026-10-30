namespace Intelligence.TradeSystem.Infrastructure.Realtime;

public sealed class ExchangeRealtimeObservationOptions
{
    public const string SectionName = "ExchangeRealtimeObservation";

    public bool Enabled { get; set; } = true;
    public int QueueCapacity { get; set; } = 256;
    public int AccountConsumerCount { get; set; } = 1;
    public TimeSpan ReconnectInitialDelay { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan ReconnectMaxDelay { get; set; } = TimeSpan.FromSeconds(30);
    public double ReconnectJitter { get; set; } = 0.2;
    public TimeSpan AccountDiscoveryInterval { get; set; } = TimeSpan.FromSeconds(20);
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(20);
    public TimeSpan HeartbeatStaleAfter { get; set; } = TimeSpan.FromSeconds(60);
}
