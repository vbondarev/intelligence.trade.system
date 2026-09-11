using System.Text.Json;
using System.Text.Json.Serialization;
using Intelligence.TradeSystem.Application.Recommendations;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Recommendations;

namespace Intelligence.TradeSystem.Infrastructure.RecommendationPolicy;

/// <summary>
/// Загружает policy JSON один раз при построении composition root. Hash из файла не принимается:
/// identity вычисляет сам typed PolicyDefinition.
/// </summary>
public sealed class JsonRecommendationPolicyDefinitionProvider
    : IRecommendationPolicyDefinitionProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly PolicyDefinition definition;

    public JsonRecommendationPolicyDefinitionProvider(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = System.IO.Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Recommendation policy file was not found.", fullPath);

        var json = File.ReadAllText(fullPath);
        var document = JsonSerializer.Deserialize<PolicyDefinitionDocument>(json, JsonOptions)
            ?? throw new InvalidOperationException("Recommendation policy JSON is empty.");
        definition = document.ToDomain();
    }

    public ValueTask<PolicyDefinition> GetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(definition);
    }

    private sealed class PolicyDefinitionDocument
    {
        public string? Version { get; init; }
        public TimeSpan? ValidityPeriod { get; init; }
        public decimal? CloseLossThreshold { get; init; }
        public decimal? ReduceLossThreshold { get; init; }
        public decimal? ProtectProfitThreshold { get; init; }
        public decimal? TakePartialProfitThreshold { get; init; }
        public ConfidenceProfilesDocument? ConfidenceProfiles { get; init; }
        public PriorityProfilesDocument? PriorityProfiles { get; init; }
        public AddAllowedLimitsDocument? AddAllowedLimits { get; init; }

        public PolicyDefinition ToDomain()
        {
            if (Version is null)
                throw new InvalidOperationException("Recommendation policy Version is required.");
            if (!ValidityPeriod.HasValue)
                throw new InvalidOperationException("Recommendation policy ValidityPeriod is required.");
            if (!CloseLossThreshold.HasValue ||
                !ReduceLossThreshold.HasValue ||
                !ProtectProfitThreshold.HasValue ||
                !TakePartialProfitThreshold.HasValue)
                throw new InvalidOperationException("All recommendation policy thresholds are required.");
            if (ConfidenceProfiles is null)
                throw new InvalidOperationException("Recommendation policy ConfidenceProfiles are required.");
            if (PriorityProfiles is null)
                throw new InvalidOperationException("Recommendation policy PriorityProfiles are required.");
            if (AddAllowedLimits is null)
                throw new InvalidOperationException("Recommendation policy AddAllowedLimits are required.");

            return new(
                new RuleVersion(Version),
                ValidityPeriod.Value,
                CloseLossThreshold.Value,
                ReduceLossThreshold.Value,
                ProtectProfitThreshold.Value,
                TakePartialProfitThreshold.Value,
                ConfidenceProfiles.ToDomain(),
                PriorityProfiles.ToDomain(),
                AddAllowedLimits.ToDomain());
        }
    }

    private sealed class ConfidenceProfilesDocument
    {
        public decimal? Hold { get; init; }
        public decimal? Watch { get; init; }
        public decimal? ProtectProfit { get; init; }
        public decimal? Reduce { get; init; }
        public decimal? Close { get; init; }
        public decimal? MoveStop { get; init; }
        public decimal? TakePartialProfit { get; init; }

        public RecommendationConfidenceProfile ToDomain() =>
            new(
                Hold ?? throw Missing(nameof(Hold)),
                Watch ?? throw Missing(nameof(Watch)),
                ProtectProfit ?? throw Missing(nameof(ProtectProfit)),
                Reduce ?? throw Missing(nameof(Reduce)),
                Close ?? throw Missing(nameof(Close)),
                MoveStop ?? throw Missing(nameof(MoveStop)),
                TakePartialProfit ?? throw Missing(nameof(TakePartialProfit)));
    }

    private sealed class PriorityProfilesDocument
    {
        public RecommendationPriority? Hold { get; init; }
        public RecommendationPriority? Watch { get; init; }
        public RecommendationPriority? ProtectProfit { get; init; }
        public RecommendationPriority? Reduce { get; init; }
        public RecommendationPriority? Close { get; init; }
        public RecommendationPriority? MoveStop { get; init; }
        public RecommendationPriority? TakePartialProfit { get; init; }

        public RecommendationPriorityProfile ToDomain() =>
            new(
                Hold ?? throw Missing(nameof(Hold)),
                Watch ?? throw Missing(nameof(Watch)),
                ProtectProfit ?? throw Missing(nameof(ProtectProfit)),
                Reduce ?? throw Missing(nameof(Reduce)),
                Close ?? throw Missing(nameof(Close)),
                MoveStop ?? throw Missing(nameof(MoveStop)),
                TakePartialProfit ?? throw Missing(nameof(TakePartialProfit)));
    }

    private sealed class AddAllowedLimitsDocument
    {
        public decimal? MaximumAdditionalPositionPercentOfEquity { get; init; }
        public decimal? MaximumAdditionalAvailableCapitalPercent { get; init; }
        public decimal? MinimumLiquidationDistancePercent { get; init; }

        public AddAllowedPolicyLimits ToDomain() =>
            new(
                MaximumAdditionalPositionPercentOfEquity ??
                throw Missing(nameof(MaximumAdditionalPositionPercentOfEquity)),
                MaximumAdditionalAvailableCapitalPercent ??
                throw Missing(nameof(MaximumAdditionalAvailableCapitalPercent)),
                MinimumLiquidationDistancePercent ??
                throw Missing(nameof(MinimumLiquidationDistancePercent)));
    }

    private static InvalidOperationException Missing(string propertyName) =>
        new($"Recommendation policy property '{propertyName}' is required.");
}
