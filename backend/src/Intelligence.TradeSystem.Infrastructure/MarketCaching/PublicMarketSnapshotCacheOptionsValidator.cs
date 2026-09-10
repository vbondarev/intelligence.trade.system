using Microsoft.Extensions.Options;

namespace Intelligence.TradeSystem.Infrastructure.MarketCaching;

internal sealed class PublicMarketSnapshotCacheOptionsValidator : IValidateOptions<PublicMarketSnapshotCacheOptions>
{
    public ValidateOptionsResult Validate(string? name, PublicMarketSnapshotCacheOptions options)
    {
        if (options.EntryLifetime <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(PublicMarketSnapshotCacheOptions.EntryLifetime)} must be greater than zero.");
        }

        if (options.EntryLifetime > PublicMarketSnapshotCacheOptions.MaximumEntryLifetime)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(PublicMarketSnapshotCacheOptions.EntryLifetime)} must not exceed " +
                $"{PublicMarketSnapshotCacheOptions.MaximumEntryLifetime}.");
        }

        return ValidateOptionsResult.Success;
    }
}
