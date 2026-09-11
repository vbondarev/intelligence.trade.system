using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Portfolio;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.MarketIntelligence.Snapshots;

namespace Intelligence.TradeSystem.Application.Assessments;

/// <summary>
/// Единый явный вход детерминированной оценки одной позиции.
/// </summary>
public sealed record PositionAssessmentInput
{
    /// <summary>
    /// Создаёт вход оценки с зафиксированными snapshots, качеством данных и временем оценки.
    /// </summary>
    public PositionAssessmentInput(
        Position position,
        MarketSnapshot marketSnapshot,
        PortfolioState portfolioState,
        PortfolioRiskPolicySettings portfolioRiskPolicySettings,
        PositionAssessmentInputVersions inputVersions,
        AssessmentDataQuality marketDataQuality,
        AssessmentDataQuality portfolioDataQuality,
        DateTimeOffset asOf,
        PositionAssessmentRules rules)
    {
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(marketSnapshot);
        ArgumentNullException.ThrowIfNull(portfolioState);
        ArgumentNullException.ThrowIfNull(portfolioRiskPolicySettings);
        ArgumentNullException.ThrowIfNull(rules);
        if (!Enum.IsDefined(marketDataQuality))
            throw new ArgumentOutOfRangeException(
                nameof(marketDataQuality), marketDataQuality, "Market data quality is not defined.");
        if (!Enum.IsDefined(portfolioDataQuality))
            throw new ArgumentOutOfRangeException(
                nameof(portfolioDataQuality), portfolioDataQuality, "Portfolio data quality is not defined.");
        inputVersions.Validate();

        var effectiveIdentity = PositionAssessmentConfigurationIdentity.Compose(
            inputVersions.BasePolicyConfigurationIdentity,
            portfolioRiskPolicySettings,
            rules,
            marketDataQuality,
            portfolioDataQuality);

        Position = position;
        MarketSnapshot = marketSnapshot;
        PortfolioState = portfolioState;
        PortfolioRiskPolicySettings = portfolioRiskPolicySettings;
        InputVersions = new PositionAssessmentInputVersions(
            inputVersions.PositionId,
            inputVersions.ExchangeAccountId,
            inputVersions.InstrumentId,
            inputVersions.PositionObservedAt,
            inputVersions.PortfolioCalculatedAt,
            inputVersions.MarketCapturedAt,
            inputVersions.BasePolicyConfigurationIdentity,
            effectiveIdentity);
        MarketDataQuality = marketDataQuality;
        PortfolioDataQuality = portfolioDataQuality;
        AsOf = asOf;
        Rules = rules;
    }

    /// <summary>Оцениваемая позиция.</summary>
    public Position Position { get; }

    /// <summary>Публичный рыночный snapshot для инструмента позиции.</summary>
    public MarketSnapshot MarketSnapshot { get; }

    /// <summary>Актуальный snapshot портфеля аккаунта.</summary>
    public PortfolioState PortfolioState { get; }

    /// <summary>Явные настройки существующей портфельной risk policy.</summary>
    public PortfolioRiskPolicySettings PortfolioRiskPolicySettings { get; }

    /// <summary>Воспроизводимая identity всех входных snapshots и policy configuration.</summary>
    public PositionAssessmentInputVersions InputVersions { get; }

    /// <summary>Качество рыночного источника.</summary>
    public AssessmentDataQuality MarketDataQuality { get; }

    /// <summary>Качество портфельного источника.</summary>
    public AssessmentDataQuality PortfolioDataQuality { get; }

    /// <summary>Фиксированный момент оценки.</summary>
    public DateTimeOffset AsOf { get; }

    /// <summary>Явно переданные пороги и срок актуальности assessment algorithm.</summary>
    public PositionAssessmentRules Rules { get; }
}
