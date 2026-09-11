using System.Text.Json;
using Intelligence.TradeSystem.Application.Assessments;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Decisions;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Portfolio;
using Intelligence.TradeSystem.Domain.Snapshots;
using Intelligence.TradeSystem.MarketIntelligence.Snapshots;

namespace Intelligence.TradeSystem.Application.Tests.Assessments;

public sealed class PositionAssessmentServiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly PolicyConfigurationIdentity PolicyIdentity = new("policy-v1", "sha256:fixed");
    private static readonly PositionAssessmentRules Rules = PositionAssessmentRules.Default;

    [Fact]
    public void Assess_Is_Deterministic_For_Identical_Inputs()
    {
        var input = CreateInput(PositionSide.Long, MarketTrend.Bullish);
        var service = new PositionAssessmentService();

        var first = service.Assess(input);
        var second = service.Assess(input);

        first.Id.Should().NotBe(second.Id);
        first.PortfolioRiskDecision.Should().Be(RiskIncreaseDecision.Allowed);
        first.Result.DataQuality.SafetyState.Should().Be(AssessmentSafetyState.Allowed);
        second.Result.Should().Be(first.Result);
        JsonSerializer.Deserialize<PositionAssessmentResult>(
            JsonSerializer.Serialize(first.Result))
            .Should()
            .Be(first.Result);
        second.InputVersions.Should().Be(first.InputVersions);
        second.RuleVersion.Should().Be(first.RuleVersion);
        second.CreatedAt.Should().Be(first.CreatedAt);
        second.ValidUntil.Should().Be(first.ValidUntil);
        second.PortfolioRiskDecision.Should().Be(first.PortfolioRiskDecision);
        second.ReasonCodes.Should().Equal(first.ReasonCodes);
    }

    [Fact]
    public void Bullish_Market_Is_Aligned_For_Long_And_Adverse_For_Short()
    {
        var longAssessment = new PositionAssessmentService().Assess(
            CreateInput(PositionSide.Long, MarketTrend.Bullish));
        var shortAssessment = new PositionAssessmentService().Assess(
            CreateInput(PositionSide.Short, MarketTrend.Bullish));

        longAssessment.Result.Trend.PositionAlignment.Should().Be(PositionTrendAlignment.Aligned);
        shortAssessment.Result.Trend.PositionAlignment.Should().Be(PositionTrendAlignment.Adverse);
        longAssessment.ReasonCodes.Should().Contain(ReasonCode.TrendAligned);
        shortAssessment.ReasonCodes.Should().Contain(ReasonCode.TrendAdverse);
    }

    [Fact]
    public void Bearish_Market_Is_Adverse_For_Long_And_Aligned_For_Short()
    {
        var longAssessment = new PositionAssessmentService().Assess(
            CreateInput(PositionSide.Long, MarketTrend.Bearish));
        var shortAssessment = new PositionAssessmentService().Assess(
            CreateInput(PositionSide.Short, MarketTrend.Bearish));

        longAssessment.Result.Trend.PositionAlignment.Should().Be(PositionTrendAlignment.Adverse);
        shortAssessment.Result.Trend.PositionAlignment.Should().Be(PositionTrendAlignment.Aligned);
    }

    [Fact]
    public void Sideways_Market_Is_Recorded_As_Flat_Or_Unknown()
    {
        var assessment = new PositionAssessmentService().Assess(
            CreateInput(PositionSide.Long, MarketTrend.Sideways));

        assessment.Result.Trend.PositionAlignment.Should().Be(PositionTrendAlignment.FlatOrUnknown);
        assessment.ReasonCodes.Should().Contain(ReasonCode.TrendFlatOrUnknown);
    }

    [Theory]
    [InlineData(80, AssessmentMomentumState.Overbought, ReasonCode.MomentumOverbought)]
    [InlineData(20, AssessmentMomentumState.Oversold, ReasonCode.MomentumOversold)]
    [InlineData(50, AssessmentMomentumState.Normal, ReasonCode.MomentumNormal)]
    public void Momentum_Uses_Rsi_State_And_Reason_Code(
        decimal rsi,
        AssessmentMomentumState expectedState,
        ReasonCode expectedReason)
    {
        var assessment = new PositionAssessmentService().Assess(
            CreateInput(PositionSide.Long, MarketTrend.Bullish, rsi: rsi));

        assessment.Result.Momentum.State.Should().Be(expectedState);
        assessment.ReasonCodes.Should().Contain(expectedReason);
    }

    [Fact]
    public void Long_Uses_Directional_Stop_Breakeven_And_Liquidation_Distances()
    {
        var assessment = new PositionAssessmentService().Assess(
            CreateInput(PositionSide.Long, MarketTrend.Bullish));

        assessment.Result.Stop.State.Should().Be(AssessmentStopState.Protective);
        assessment.Result.Breakeven.IsProfitable.Should().BeTrue();
        assessment.Result.Liquidation.State.Should().Be(AssessmentLiquidationState.Far);
        assessment.Result.Liquidation.DistanceFromCurrentPercent.Should().BeApproximately(27.27m, 0.01m);
    }

    [Fact]
    public void Short_Uses_Mirrored_Stop_Breakeven_And_Liquidation_Distances()
    {
        var assessment = new PositionAssessmentService().Assess(
            CreateInput(PositionSide.Short, MarketTrend.Bearish));

        assessment.Result.Stop.State.Should().Be(AssessmentStopState.Protective);
        assessment.Result.Breakeven.IsProfitable.Should().BeTrue();
        assessment.Result.Liquidation.State.Should().Be(AssessmentLiquidationState.Far);
        assessment.Result.Liquidation.DistanceFromCurrentPercent.Should().BeApproximately(33.33m, 0.01m);
        assessment.Result.Pnl.IsFavorable.Should().BeTrue();
    }

    [Fact]
    public void Low_Volume_Is_Recorded_And_Fallback_Diagnostics_Make_Data_Partial()
    {
        var lowVolume = new PositionAssessmentService().Assess(
            CreateInput(PositionSide.Long, MarketTrend.Bullish, volumeRatio: 0.2m));
        var partial = new PositionAssessmentService().Assess(
            CreateInput(
                PositionSide.Long,
                MarketTrend.Bullish,
                marketSnapshotDiagnostics:
                [
                    new IndicatorDiagnosticSnapshot
                    {
                        Timeframe = "4h",
                        Indicator = "atr14",
                        Reason = "PartialWindow",
                        IsFallback = true,
                        Message = "test fallback",
                    },
                ]));

        lowVolume.ReasonCodes.Should().Contain(ReasonCode.LowVolume);
        partial.Result.DataQuality.Overall.Should().Be(AssessmentDataQuality.Partial);
        partial.PortfolioRiskDecision.Should().Be(RiskIncreaseDecision.Blocked);
    }

    [Fact]
    public void Liquidation_Near_And_Missing_Are_Explicit()
    {
        var near = new PositionAssessmentService().Assess(
            CreateInput(PositionSide.Long, MarketTrend.Bullish, customLiquidationPrice: 105m));
        var missing = new PositionAssessmentService().Assess(
            CreateInput(PositionSide.Long, MarketTrend.Bullish, includeLiquidation: false));

        near.Result.Liquidation.State.Should().Be(AssessmentLiquidationState.Near);
        near.ReasonCodes.Should().Contain(ReasonCode.LiquidationNearby);
        missing.Result.Liquidation.State.Should().Be(AssessmentLiquidationState.Unavailable);
        missing.ReasonCodes.Should().Contain(ReasonCode.LiquidationUnavailable);
    }

    [Fact]
    public void Missing_Stop_Is_Not_Represented_As_Zero()
    {
        var assessment = new PositionAssessmentService().Assess(
            CreateInput(PositionSide.Short, MarketTrend.Bearish, includeStop: false));

        assessment.Result.Stop.StopPrice.Should().BeNull();
        assessment.Result.Stop.State.Should().Be(AssessmentStopState.Unavailable);
        assessment.ReasonCodes.Should().Contain(ReasonCode.StopMissing);
    }

    [Theory]
    [InlineData(AssessmentDataQuality.Stale, ReasonCode.MarketDataStale)]
    [InlineData(AssessmentDataQuality.Partial, ReasonCode.MarketDataPartial)]
    [InlineData(AssessmentDataQuality.Uncertain, ReasonCode.MarketDataUncertain)]
    public void Non_Fresh_Market_Data_Always_Blocks_Risk_Increase(
        AssessmentDataQuality quality,
        ReasonCode expectedReason)
    {
        var input = CreateInput(PositionSide.Long, MarketTrend.Bullish, marketDataQuality: quality);

        var assessment = new PositionAssessmentService().Assess(input);

        assessment.PortfolioRiskDecision.Should().Be(RiskIncreaseDecision.Blocked);
        assessment.Result.PortfolioRisk.PolicyDecision.Should().Be(RiskIncreaseDecision.Allowed);
        assessment.Result.DataQuality.SafetyState.Should().Be(AssessmentSafetyState.Blocked);
        assessment.ReasonCodes.Should().Contain(expectedReason);
        assessment.ReasonCodes.Should().NotContain(ReasonCode.RiskWithinLimits);
    }

    [Fact]
    public void Portfolio_Risk_Limits_Remain_Independent_From_Data_Safety_Guard()
    {
        var input = CreateInput(
            PositionSide.Long,
            MarketTrend.Bullish,
            marketDataQuality: AssessmentDataQuality.Stale,
            portfolioRiskPolicySettings: new PortfolioRiskPolicySettings(90m, 1m, 1m));

        var assessment = new PositionAssessmentService().Assess(input);

        assessment.Result.PortfolioRisk.PolicyDecision.Should().Be(RiskIncreaseDecision.Blocked);
        assessment.PortfolioRiskDecision.Should().Be(RiskIncreaseDecision.Blocked);
        assessment.ReasonCodes.Should().Contain(ReasonCode.MarketDataStale);
        assessment.ReasonCodes.Should().Contain(ReasonCode.InsufficientFreeCapital);
        assessment.ReasonCodes.Should().Contain(ReasonCode.GrossExposureLimitExceeded);
        assessment.ReasonCodes.Should().Contain(ReasonCode.ConcentrationLimitExceeded);
    }

    [Fact]
    public void Mismatched_Position_Identity_Is_Rejected()
    {
        var input = CreateInput(PositionSide.Long, MarketTrend.Bullish);
        var versions = new PositionAssessmentInputVersions(
            PositionId.New(),
            input.Position.ExchangePositionKey.ExchangeAccountId,
            input.Position.ExchangePositionKey.InstrumentId,
            input.Position.LastObservedAt,
            input.PortfolioState.CalculatedAt,
            input.MarketSnapshot.CapturedAtUtc,
            PolicyIdentity);
        var invalid = RecreateInput(input, inputVersions: versions);

        FluentActions.Invoking(() => new PositionAssessmentService().Assess(invalid))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Mismatched_Account_Identity_Is_Rejected()
    {
        var input = CreateInput(PositionSide.Long, MarketTrend.Bullish);
        var versions = new PositionAssessmentInputVersions(
            input.Position.Id,
            ExchangeAccountId.New(),
            input.Position.ExchangePositionKey.InstrumentId,
            input.Position.LastObservedAt,
            input.PortfolioState.CalculatedAt,
            input.MarketSnapshot.CapturedAtUtc,
            PolicyIdentity);

        FluentActions.Invoking(() => new PositionAssessmentService().Assess(
                RecreateInput(input, inputVersions: versions)))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AsOf_Before_An_Input_Snapshot_Is_Rejected()
    {
        var input = CreateInput(PositionSide.Long, MarketTrend.Bullish, asOf: T0);

        FluentActions.Invoking(() => new PositionAssessmentService().Assess(input))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Mismatched_Symbol_Is_Rejected()
    {
        var invalidMarket = CreateMarketSnapshot(
            T0.AddMinutes(2),
            MarketTrend.Bullish,
            50m) with { Symbol = "ETHUSDT" };
        var input = RecreateInput(
            CreateInput(PositionSide.Long, MarketTrend.Bullish),
            marketSnapshot: invalidMarket);

        FluentActions.Invoking(() => new PositionAssessmentService().Assess(input))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Invalid_Policy_Identity_Is_Rejected_Before_Assessment()
    {
        new PolicyConfigurationIdentity("v1", "hash")
            .Should().Be(new PolicyConfigurationIdentity("v1", "hash"));
        FluentActions.Invoking(() => new PolicyConfigurationIdentity(" ", "hash"))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new PolicyConfigurationIdentity("v1", " "))
            .Should().Throw<ArgumentException>();
    }

    private static PositionAssessmentInput CreateInput(
        PositionSide side,
        MarketTrend trend,
        decimal rsi = 50m,
        AssessmentDataQuality marketDataQuality = AssessmentDataQuality.FreshCompleteReliable,
        PortfolioRiskPolicySettings? portfolioRiskPolicySettings = null,
        bool includeStop = true,
        bool includeLiquidation = true,
        decimal? customLiquidationPrice = null,
        decimal volumeRatio = 1m,
        IReadOnlyList<IndicatorDiagnosticSnapshot>? marketSnapshotDiagnostics = null,
        DateTimeOffset? asOf = null)
    {
        var accountId = ExchangeAccountId.New();
        var position = Position.Create(
            ExchangePositionKey.Create(
                accountId,
                InstrumentId.From("BTCUSDT"),
                side,
                0),
            MarketCategory.Linear,
            1m,
            T0,
            T0,
            averageEntryPrice: 100m,
            positionValue: 100m,
            leverage: 2m,
            markPrice: side == PositionSide.Long ? 110m : 90m,
            breakEvenPrice: side == PositionSide.Long ? 105m : 95m,
            liquidationPrice: includeLiquidation
                ? customLiquidationPrice ?? (side == PositionSide.Long ? 80m : 120m)
                : null,
            unrealizedPnl: 10m,
            stopLoss: includeStop ? side == PositionSide.Long ? 95m : 105m : null);
        var portfolio = PortfolioState.Create(
            accountId,
            [position],
            new PortfolioCapitalState(1_000m, 800m, T0, 1_000m),
            T0.AddMinutes(1),
            TimeSpan.FromHours(1));
        var market = CreateMarketSnapshot(
            T0.AddMinutes(2),
            trend,
            rsi,
            side == PositionSide.Long ? 110m : 90m,
            volumeRatio,
            marketSnapshotDiagnostics);

        return new PositionAssessmentInput(
            position,
            market,
            portfolio,
            portfolioRiskPolicySettings ?? new PortfolioRiskPolicySettings(0m, 100m, 100m),
            new PositionAssessmentInputVersions(
                position.Id,
                accountId,
                InstrumentId.From("BTCUSDT"),
                position.LastObservedAt,
                portfolio.CalculatedAt,
                market.CapturedAtUtc,
                PolicyIdentity),
            marketDataQuality,
            AssessmentDataQuality.FreshCompleteReliable,
            asOf ?? T0.AddMinutes(3),
            Rules);
    }

    private static PositionAssessmentInput RecreateInput(
        PositionAssessmentInput input,
        MarketSnapshot? marketSnapshot = null,
        PositionAssessmentInputVersions? inputVersions = null) =>
        new(
            input.Position,
            marketSnapshot ?? input.MarketSnapshot,
            input.PortfolioState,
            input.PortfolioRiskPolicySettings,
            inputVersions ?? input.InputVersions,
            input.MarketDataQuality,
            input.PortfolioDataQuality,
            input.AsOf,
            input.Rules);

    private static MarketSnapshot CreateMarketSnapshot(
        DateTimeOffset capturedAt,
        MarketTrend trend,
        decimal rsi,
        decimal currentPrice = 110m,
        decimal volumeRatio = 1m,
        IReadOnlyList<IndicatorDiagnosticSnapshot>? diagnostics = null)
    {
        var timeframe = new TimeframeAnalysisSnapshot
        {
            Timeframe = "4h",
            LastCandleOpenTimeUtc = capturedAt.AddMinutes(-1),
            LastCandle = new CandleSnapshot
            {
                OpenTimeUtc = capturedAt.AddMinutes(-1),
                Open = currentPrice,
                High = currentPrice + 2m,
                Low = currentPrice - 2m,
                Close = currentPrice,
                Volume = 100m,
                Turnover = 100m * currentPrice,
            },
            Rsi14 = rsi,
            Rsi14IsReliable = true,
            Atr14 = 2m,
            AtrIsReliable = true,
            VolumeRatio = volumeRatio,
            VolumeRatioIsReliable = true,
            Trend = trend,
            TrendStrengthScore = 0.8m,
            Support1 = currentPrice - 5m,
            Support1Strength = 0.7m,
            DistanceToSupport1Pct = 4.5m,
            Resistance1 = currentPrice + 5m,
            Resistance1Strength = 0.7m,
            DistanceToResistance1Pct = 4.5m,
        };

        return new MarketSnapshot
        {
            Exchange = "Bybit",
            Symbol = "BTCUSDT",
            Category = "Linear",
            CapturedAtUtc = capturedAt,
            Price = new PriceSnapshot
            {
                LastPrice = currentPrice,
                MarkPrice = currentPrice,
            },
            Derivatives = new(),
            OrderBook = new() { CapturedAtUtc = capturedAt },
            TradeFlow = new() { WindowEndUtc = capturedAt },
            M15 = timeframe with { Timeframe = "15m" },
            H1 = timeframe with { Timeframe = "1h" },
            H4 = timeframe,
            D1 = timeframe with { Timeframe = "1d" },
            Sentiment = new(),
            IndicatorDiagnostics = diagnostics ?? [],
        };
    }
}
