using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Intelligence.TradeSystem.Domain.Assessments;

namespace Intelligence.TradeSystem.Domain.Recommendations;

/// <summary>
/// Типизированное неизменяемое описание изменяемых параметров RecommendationPolicy.
/// Safety-инварианты намеренно отсутствуют в этом типе.
/// </summary>
public sealed record PolicyDefinition
{
    public PolicyDefinition(
        RuleVersion version,
        TimeSpan validityPeriod,
        decimal closeLossThreshold,
        decimal reduceLossThreshold,
        decimal protectProfitThreshold,
        decimal takePartialProfitThreshold,
        RecommendationConfidenceProfile confidenceProfiles,
        RecommendationPriorityProfile priorityProfiles,
        AddAllowedPolicyLimits addAllowedLimits)
    {
        if (validityPeriod <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(validityPeriod), validityPeriod, "Validity period must be positive.");
        if (closeLossThreshold >= reduceLossThreshold || reduceLossThreshold > 0m || closeLossThreshold > 0m)
            throw new ArgumentOutOfRangeException(
                nameof(closeLossThreshold),
                "Loss thresholds must satisfy close < reduce <= 0.");
        ArgumentOutOfRangeException.ThrowIfNegative(protectProfitThreshold);
        if (protectProfitThreshold == 0m)
            throw new ArgumentOutOfRangeException(nameof(protectProfitThreshold), "Profit threshold must be positive.");
        ArgumentOutOfRangeException.ThrowIfNegative(takePartialProfitThreshold);
        if (takePartialProfitThreshold == 0m)
            throw new ArgumentOutOfRangeException(
                nameof(takePartialProfitThreshold),
                "Profit threshold must be positive.");

        ArgumentNullException.ThrowIfNull(confidenceProfiles);
        ArgumentNullException.ThrowIfNull(priorityProfiles);
        ArgumentNullException.ThrowIfNull(addAllowedLimits);

        Version = version;
        ValidityPeriod = validityPeriod;
        CloseLossThreshold = closeLossThreshold;
        ReduceLossThreshold = reduceLossThreshold;
        ProtectProfitThreshold = protectProfitThreshold;
        TakePartialProfitThreshold = takePartialProfitThreshold;
        ConfidenceProfiles = confidenceProfiles;
        PriorityProfiles = priorityProfiles;
        AddAllowedLimits = addAllowedLimits;

        CanonicalRepresentation = BuildCanonicalRepresentation();
        Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalRepresentation)));
        Identity = PolicyConfigurationIdentity.From(Version.Value, Hash);
    }

    public RuleVersion Version { get; }
    public string Hash { get; }
    public PolicyConfigurationIdentity Identity { get; }
    public string CanonicalRepresentation { get; }
    public TimeSpan ValidityPeriod { get; }
    public decimal CloseLossThreshold { get; }
    public decimal ReduceLossThreshold { get; }
    public decimal ProtectProfitThreshold { get; }
    public decimal TakePartialProfitThreshold { get; }
    public RecommendationConfidenceProfile ConfidenceProfiles { get; }
    public RecommendationPriorityProfile PriorityProfiles { get; }
    public AddAllowedPolicyLimits AddAllowedLimits { get; }

    public static PolicyDefinition Default => new(
        new RuleVersion("recommendation-v1"),
        TimeSpan.FromMinutes(5),
        closeLossThreshold: -10m,
        reduceLossThreshold: -5m,
        protectProfitThreshold: 2m,
        takePartialProfitThreshold: 5m,
        RecommendationConfidenceProfile.Default,
        RecommendationPriorityProfile.Default,
        AddAllowedPolicyLimits.Default);

    private string BuildCanonicalRepresentation() => string.Join(
        "|",
        "recommendation-policy",
        Version.Value,
        ValidityPeriod.Ticks.ToString(CultureInfo.InvariantCulture),
        Format(CloseLossThreshold),
        Format(ReduceLossThreshold),
        Format(ProtectProfitThreshold),
        Format(TakePartialProfitThreshold),
        Format(ConfidenceProfiles.Hold),
        Format(ConfidenceProfiles.Watch),
        Format(ConfidenceProfiles.ProtectProfit),
        Format(ConfidenceProfiles.Reduce),
        Format(ConfidenceProfiles.Close),
        Format(ConfidenceProfiles.MoveStop),
        Format(ConfidenceProfiles.TakePartialProfit),
        PriorityProfiles.Hold,
        PriorityProfiles.Watch,
        PriorityProfiles.ProtectProfit,
        PriorityProfiles.Reduce,
        PriorityProfiles.Close,
        PriorityProfiles.MoveStop,
        PriorityProfiles.TakePartialProfit,
        Format(AddAllowedLimits.MaximumAdditionalPositionPercentOfEquity),
        Format(AddAllowedLimits.MaximumAdditionalAvailableCapitalPercent),
        Format(AddAllowedLimits.MinimumLiquidationDistancePercent));

    private static string Format(decimal value) =>
        value.ToString("G29", CultureInfo.InvariantCulture);
}
