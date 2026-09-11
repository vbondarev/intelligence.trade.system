using System.Diagnostics.CodeAnalysis;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Decisions;
using Intelligence.TradeSystem.Domain.Portfolio;
using Intelligence.TradeSystem.MarketIntelligence.Snapshots;

namespace Intelligence.TradeSystem.Application.Assessments;

/// <summary>
/// Оркестрирует детерминированную оценку позиции без IO и без формирования рекомендации.
/// </summary>
public sealed class PositionAssessmentService
{
    /// <summary>
    /// Создаёт immutable assessment из одного согласованного воспроизводимого входа.
    /// </summary>
    [SuppressMessage(
        "Performance",
        "CA1822",
        Justification = "Сервис остаётся instance-scoped для единообразного application DI-контракта.")]
    public PositionAssessment Assess(PositionAssessmentInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ValidateInput(input);

        var marketQuality = ResolveMarketQuality(input);
        var portfolioQuality = ResolvePortfolioQuality(input);
        var overallQuality = MaxQuality(marketQuality, portfolioQuality);

        var portfolioRiskResult = PortfolioRiskPolicy.EvaluateRiskIncrease(
            input.PortfolioState,
            input.PortfolioRiskPolicySettings);
        portfolioRiskResult = ApplyExplicitPortfolioQuality(portfolioRiskResult, portfolioQuality);

        var timeframe = input.MarketSnapshot.H4;
        var currentPrice = PositionAssessmentFeatureCalculator.GetCurrentPrice(input.MarketSnapshot);
        var result = PositionAssessmentFeatureCalculator.BuildResult(
            input,
            timeframe,
            currentPrice,
            marketQuality,
            portfolioQuality,
            overallQuality,
            portfolioRiskResult);

        var reasonCodes = PositionAssessmentFeatureCalculator.BuildReasonCodes(input, result);
        return PositionAssessment.Create(
            input.InputVersions,
            input.Rules.Version,
            portfolioRiskResult,
            result,
            reasonCodes,
            input.AsOf,
            input.AsOf.Add(input.Rules.ValidityPeriod));
    }

    private static void ValidateInput(PositionAssessmentInput input)
    {
        input.InputVersions.Validate();

        var position = input.Position;
        var versions = input.InputVersions;
        var market = input.MarketSnapshot;
        var portfolio = input.PortfolioState;

        if (position.Id != versions.PositionId)
            throw new ArgumentException("Assessment PositionId does not match the position.", nameof(input));
        if (position.ExchangePositionKey.ExchangeAccountId != versions.ExchangeAccountId ||
            portfolio.ExchangeAccountId != versions.ExchangeAccountId)
            throw new ArgumentException("Assessment ExchangeAccountId does not match the position and portfolio.", nameof(input));
        if (position.ExchangePositionKey.InstrumentId != versions.InstrumentId)
            throw new ArgumentException("Assessment InstrumentId does not match the position.", nameof(input));
        if (!string.Equals(
                market.Symbol?.Trim(),
                versions.InstrumentId.Value,
                StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Market snapshot symbol does not match the position instrument.", nameof(input));
        if (!string.Equals(
                market.Category?.Trim(),
                position.MarketCategory.ToString(),
                StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Market snapshot category does not match the position market category.", nameof(input));

        var portfolioPositions = portfolio.Positions
            .Where(snapshot => snapshot.PositionId == position.Id)
            .ToArray();
        if (portfolioPositions.Length != 1)
            throw new ArgumentException("Portfolio snapshot does not contain the assessed position.", nameof(input));
        var portfolioPosition = portfolioPositions[0];
        if (portfolioPosition.ExchangePositionKey != position.ExchangePositionKey ||
            portfolioPosition.MarketCategory != position.MarketCategory)
            throw new ArgumentException("Portfolio position identity does not match the assessed position.", nameof(input));

        if (versions.PositionObservedAt != position.LastObservedAt ||
            versions.PortfolioCalculatedAt != portfolio.CalculatedAt ||
            versions.MarketCapturedAt != market.CapturedAtUtc)
            throw new ArgumentException("Assessment input versions do not match the supplied snapshots.", nameof(input));

        if (input.AsOf < versions.PositionObservedAt ||
            input.AsOf < versions.PortfolioCalculatedAt ||
            input.AsOf < versions.MarketCapturedAt)
            throw new ArgumentException("Assessment asOf cannot precede an input observation.", nameof(input));
        if (market.OrderBook.CapturedAtUtc > market.CapturedAtUtc ||
            market.TradeFlow.WindowEndUtc > market.CapturedAtUtc ||
            market.M15.LastCandleOpenTimeUtc > market.CapturedAtUtc ||
            market.H1.LastCandleOpenTimeUtc > market.CapturedAtUtc ||
            market.H4.LastCandleOpenTimeUtc > market.CapturedAtUtc ||
            market.D1.LastCandleOpenTimeUtc > market.CapturedAtUtc)
            throw new ArgumentException("Market snapshot contains data captured after its root timestamp.", nameof(input));
    }

    private static AssessmentDataQuality ResolveMarketQuality(PositionAssessmentInput input)
    {
        var quality = input.MarketDataQuality;
        if (input.MarketSnapshot.IndicatorDiagnostics.Any(diagnostic => !diagnostic.IsFallback) ||
            input.MarketSnapshot.H4.Rsi14 is null ||
            input.MarketSnapshot.H4.Atr14 is null ||
            input.MarketSnapshot.H4.Trend == MarketTrend.Unknown)
            quality = MaxQuality(quality, AssessmentDataQuality.Uncertain);
        else if (input.MarketSnapshot.IndicatorDiagnostics.Any(diagnostic => diagnostic.IsFallback))
            quality = MaxQuality(quality, AssessmentDataQuality.Partial);

        return quality;
    }

    private static AssessmentDataQuality ResolvePortfolioQuality(PositionAssessmentInput input)
    {
        var quality = input.PortfolioDataQuality;
        if (!input.PortfolioState.IsComplete)
            quality = MaxQuality(quality, AssessmentDataQuality.Partial);
        if (!input.PortfolioState.IsFresh)
            quality = MaxQuality(quality, AssessmentDataQuality.Stale);

        return quality;
    }

    private static RiskIncreasePolicyResult ApplyExplicitPortfolioQuality(
        RiskIncreasePolicyResult result,
        AssessmentDataQuality quality)
    {
        var qualityReason = quality switch
        {
            AssessmentDataQuality.FreshCompleteReliable => (ReasonCode?)null,
            AssessmentDataQuality.Stale => ReasonCode.PortfolioDataStale,
            AssessmentDataQuality.Partial => ReasonCode.PortfolioDataIncomplete,
            AssessmentDataQuality.Uncertain => ReasonCode.PortfolioDataUncertain,
            _ => throw new ArgumentOutOfRangeException(nameof(quality), quality, "Data quality is not defined."),
        };
        if (qualityReason is null)
            return result;

        return RiskIncreasePolicyResult.Blocked(
            result.ReasonCodes
                .Where(reason => reason != ReasonCode.RiskWithinLimits)
                .Append(qualityReason.Value));
    }

    private static AssessmentDataQuality MaxQuality(
        AssessmentDataQuality first,
        AssessmentDataQuality second) =>
        (AssessmentDataQuality)Math.Max((int)first, (int)second);
}
