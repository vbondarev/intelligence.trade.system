namespace Intelligence.TradeSystem.Infrastructure.MarketCaching;

public sealed class PublicMarketSnapshotCacheOptions
{
    public const string SectionName = "PublicMarketSnapshotCache";
    public static readonly TimeSpan DefaultEntryLifetime = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan MaximumEntryLifetime = TimeSpan.FromSeconds(5);

    public bool Enabled { get; set; } = true;

    public TimeSpan EntryLifetime { get; set; } = DefaultEntryLifetime;
}
