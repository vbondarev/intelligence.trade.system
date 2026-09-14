namespace Intelligence.TradeSystem.Domain.Recommendations;

/// <summary>
/// Настройки детерминированной стабилизации опубликованных рекомендаций.
/// </summary>
public sealed record RecommendationStabilityProfile
{
    private static readonly TimeSpan MaximumDuration = TimeSpan.FromDays(3650);

    public RecommendationStabilityProfile(
        TimeSpan minimumReplacementInterval,
        TimeSpan improvementConfirmationPeriod,
        int improvementConfirmationObservations,
        TimeSpan addAllowedConfirmationPeriod,
        int addAllowedConfirmationObservations)
    {
        ValidateDuration(minimumReplacementInterval, nameof(minimumReplacementInterval));
        ValidateDuration(improvementConfirmationPeriod, nameof(improvementConfirmationPeriod));
        ValidateObservations(improvementConfirmationObservations, nameof(improvementConfirmationObservations));
        ValidateDuration(addAllowedConfirmationPeriod, nameof(addAllowedConfirmationPeriod));
        ValidateObservations(addAllowedConfirmationObservations, nameof(addAllowedConfirmationObservations));

        MinimumReplacementInterval = minimumReplacementInterval;
        ImprovementConfirmationPeriod = improvementConfirmationPeriod;
        ImprovementConfirmationObservations = improvementConfirmationObservations;
        AddAllowedConfirmationPeriod = addAllowedConfirmationPeriod;
        AddAllowedConfirmationObservations = addAllowedConfirmationObservations;
    }

    public TimeSpan MinimumReplacementInterval { get; }
    public TimeSpan ImprovementConfirmationPeriod { get; }
    public int ImprovementConfirmationObservations { get; }
    public TimeSpan AddAllowedConfirmationPeriod { get; }
    public int AddAllowedConfirmationObservations { get; }

    public static RecommendationStabilityProfile Default => new(
        minimumReplacementInterval: TimeSpan.FromSeconds(30),
        improvementConfirmationPeriod: TimeSpan.FromMinutes(2),
        improvementConfirmationObservations: 2,
        addAllowedConfirmationPeriod: TimeSpan.FromMinutes(3),
        addAllowedConfirmationObservations: 3);

    private static void ValidateDuration(TimeSpan value, string parameterName)
    {
        if (value <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Stability duration must be positive.");
        if (value > MaximumDuration)
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Stability duration is unreasonably large.");
    }

    private static void ValidateObservations(int value, string parameterName)
    {
        if (value < 1)
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Stability observation count must be at least one.");
    }
}
