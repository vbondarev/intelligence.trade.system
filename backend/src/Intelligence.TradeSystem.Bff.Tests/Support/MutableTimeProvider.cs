namespace Intelligence.TradeSystem.Bff.Tests.Support;

/// <summary>
/// Управляемое время для сценариев истечения tokens, sessions и logout intent.
/// Таймеры остаются системными, поэтому resilience и timeouts работают как обычно.
/// </summary>
internal sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
{
    private readonly Lock gate = new();
    private DateTimeOffset now = start;

    public override DateTimeOffset GetUtcNow()
    {
        lock (gate)
        {
            return now;
        }
    }

    public void Advance(TimeSpan delta)
    {
        lock (gate)
        {
            now = now.Add(delta);
        }
    }
}
