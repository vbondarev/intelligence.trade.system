using Intelligence.TradeSystem.Api.Mappers;
using Intelligence.TradeSystem.Api.Models.MarketFacts;
using Intelligence.TradeSystem.Api.Models.Payloads;
using Intelligence.TradeSystem.Api.Tests.Helpers;

namespace Intelligence.TradeSystem.Api.Tests;

/// <summary>
/// Unit-тесты для <c>MarketFactsPayloadMapper.ToMarketFacts</c>.
/// Покрывают структуру payload, детерминированные вычисления и regression-сценарии.
/// </summary>
public sealed class MarketFactsPayloadMapperTests
{
    // ── Shared health instances ─────────────────────────────────────────────

    private static readonly LlmSnapshotHealthPayload _freshHealth = new()
    {
        IsFresh  = true,
        IsPartial = false,
        Warnings = [],
    };

    private static readonly LlmSnapshotHealthPayload _staleHealth = new()
    {
        IsFresh  = false,
        IsPartial = false,
        Warnings = ["SnapshotStale"],
    };

    private static readonly LlmSnapshotHealthPayload _partialHealth = new()
    {
        IsFresh  = true,
        IsPartial = true,
        Warnings = ["MissingSection"],
        MissingSections = ["tradeFlow"],
    };

    private static readonly LlmSnapshotHealthPayload _partialStaleHealth = new()
    {
        IsFresh  = false,
        IsPartial = true,
        Warnings = ["SnapshotStale", "MissingSection"],
        MissingSections = ["tradeFlow"],
    };

    // ===========================================================================
    // Basic structure
    // ===========================================================================

    [Fact]
    public void ToMarketFacts_maps_basic_payload_structure()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var payload = snapshot.ToMarketFacts(AnalysisMode.Intraday, _freshHealth);

        payload.SchemaVersion.Should().Be(MarketFactsPayload.CurrentSchemaVersion);
        payload.Source.Should().NotBeNull();
        payload.AnalysisContext.Should().NotBeNull();
        payload.DataQuality.Should().NotBeNull();
        payload.Price.Should().NotBeNull();
        payload.Derivatives.Should().NotBeNull();
        payload.OrderBook.Should().NotBeNull();
        payload.TradeFlow.Should().NotBeNull();
        payload.Timeframes.Should().NotBeNull();
        payload.Levels.Should().NotBeNull();
        payload.MarketInternalSentiment.Should().NotBeNull();
        payload.Tags.Should().NotBeNull();
    }

    [Fact]
    public void ToMarketFacts_schema_version_is_market_facts_v1()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var payload = snapshot.ToMarketFacts(AnalysisMode.Intraday, _freshHealth);

        payload.SchemaVersion.Should().Be("market-facts/v1");
    }

    // ===========================================================================
    // Source
    // ===========================================================================

    [Fact]
    public void ToMarketFacts_maps_source_fields()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var payload = snapshot.ToMarketFacts(AnalysisMode.Intraday, _freshHealth);

        payload.Source.Exchange.Should().Be(snapshot.Exchange);
        payload.Source.Symbol.Should().Be(snapshot.Symbol);
        payload.Source.Category.Should().Be(snapshot.Category);
        payload.Source.CapturedAtUtc.Should().Be(snapshot.CapturedAtUtc);
        payload.Source.PayloadSchemaVersion.Should().NotBeNullOrEmpty(
            because: "payloadSchemaVersion must be a non-empty source schema identifier");
    }

    // ===========================================================================
    // AnalysisContext
    // ===========================================================================

    [Fact]
    public void ToMarketFacts_maps_analysis_context()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var payload = snapshot.ToMarketFacts(AnalysisMode.Intraday, _freshHealth);

        payload.AnalysisContext.AnalysisMode.Should().Be("Intraday");
        payload.AnalysisContext.PrimaryTimeframes.Should().Contain("15m");
        payload.AnalysisContext.PrimaryTimeframes.Should().Contain("1h");
        payload.AnalysisContext.PrimaryTimeframes.Should().Contain("4h");
    }

    [Fact]
    public void ToMarketFacts_maps_analysis_context_swing_mode()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var payload = snapshot.ToMarketFacts(AnalysisMode.Swing, _freshHealth);

        payload.AnalysisContext.AnalysisMode.Should().Be("Swing");
        payload.AnalysisContext.PrimaryTimeframes.Should().Contain("1h");
        payload.AnalysisContext.PrimaryTimeframes.Should().Contain("4h");
        payload.AnalysisContext.PrimaryTimeframes.Should().Contain("1d");
    }

    // ===========================================================================
    // DataQuality status
    // ===========================================================================

    [Fact]
    public void ToMarketFacts_sets_data_quality_status_ok()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var payload = snapshot.ToMarketFacts(AnalysisMode.Intraday, _freshHealth);

        payload.DataQuality.Status.Should().Be("ok");
        payload.DataQuality.IsFresh.Should().BeTrue();
        payload.DataQuality.IsPartial.Should().BeFalse();
    }

    [Fact]
    public void ToMarketFacts_sets_data_quality_status_partial()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var payload = snapshot.ToMarketFacts(AnalysisMode.Intraday, _partialHealth);

        payload.DataQuality.Status.Should().Be("partial");
        payload.DataQuality.IsPartial.Should().BeTrue();
    }

    [Fact]
    public void ToMarketFacts_sets_data_quality_status_stale()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var payload = snapshot.ToMarketFacts(AnalysisMode.Intraday, _staleHealth);

        payload.DataQuality.Status.Should().Be("stale");
        payload.DataQuality.IsFresh.Should().BeFalse();
        payload.DataQuality.IsPartial.Should().BeFalse();
    }

    [Fact]
    public void ToMarketFacts_prefers_partial_over_stale()
    {
        // IsPartial = true AND IsFresh = false → status must be "partial", not "stale"
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var payload = snapshot.ToMarketFacts(AnalysisMode.Intraday, _partialStaleHealth);

        payload.DataQuality.Status.Should().Be("partial",
            because: "IsPartial takes precedence over IsFresh when both indicate degraded data");
    }

    [Fact]
    public void ToMarketFacts_maps_data_quality_warnings_and_sections()
    {
        var health = new LlmSnapshotHealthPayload
        {
            IsFresh      = false,
            IsPartial    = true,
            Warnings     = ["W1", "W2"],
            MissingSections = ["derivatives"],
            SectionAgesMs  = new Dictionary<string, long> { ["price"] = 1000L },
        };

        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var payload = snapshot.ToMarketFacts(AnalysisMode.Intraday, health);

        payload.DataQuality.Warnings.Should().Equal("W1", "W2");
        payload.DataQuality.MissingSections.Should().Equal("derivatives");
        payload.DataQuality.SectionAgesMs.Should().ContainKey("price").WhoseValue.Should().Be(1000L);
    }

    [Fact]
    public void ToMarketFacts_maps_indicator_diagnostics()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot() with
        {
            IndicatorDiagnostics =
            [
                new IndicatorDiagnosticSnapshot
                {
                    Timeframe  = "15m",
                    Indicator  = "rsi14",
                    Reason     = "InsufficientData",
                    IsFallback = false,
                    Message    = "Not enough candles",
                },
            ],
        };

        var payload = snapshot.ToMarketFacts(AnalysisMode.Intraday, _freshHealth);

        payload.DataQuality.IndicatorDiagnostics.Should().HaveCount(1);
        var diag = payload.DataQuality.IndicatorDiagnostics[0];
        diag.Timeframe.Should().Be("15m");
        diag.Indicator.Should().Be("rsi14");
        diag.Reason.Should().Be("InsufficientData");
        diag.IsFallback.Should().BeFalse();
        diag.Message.Should().Be("Not enough candles");
    }

    // ===========================================================================
    // Price
    // ===========================================================================

    [Fact]
    public void ToMarketFacts_maps_price_fields()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var p = snapshot.Price;
        var payload = snapshot.ToMarketFacts(AnalysisMode.Intraday, _freshHealth);

        payload.Price.LastPrice.Should().Be(p.LastPrice);
        payload.Price.MarkPrice.Should().Be(p.MarkPrice);
        payload.Price.IndexPrice.Should().Be(p.IndexPrice);
        payload.Price.SpreadAbs.Should().Be(p.SpreadAbs);
        payload.Price.SpreadPct.Should().Be(p.SpreadPct);
        payload.Price.Price24hChangePct.Should().Be(p.Price24hChangePct);
        payload.Price.High24h.Should().Be(p.High24h);
        payload.Price.Low24h.Should().Be(p.Low24h);
        payload.Price.Volume24h.Should().Be(p.Volume24h);
    }

    // ===========================================================================
    // Derivatives
    // ===========================================================================

    [Fact]
    public void ToMarketFacts_maps_derivatives_fields()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var d = snapshot.Derivatives;
        var payload = snapshot.ToMarketFacts(AnalysisMode.Intraday, _freshHealth);

        payload.Derivatives.FundingRate.Should().Be(d.FundingRate);
        payload.Derivatives.FundingRateAvg24h.Should().Be(d.FundingRateAvg24h);
        payload.Derivatives.NextFundingTimeUtc.Should().Be(d.NextFundingTimeUtc);
        payload.Derivatives.OpenInterest.Should().Be(d.OpenInterest);
        payload.Derivatives.OpenInterestValue.Should().Be(d.OpenInterestValue);
        payload.Derivatives.OpenInterestChange1hPct.Should().Be(d.OpenInterestChange1hPct);
        payload.Derivatives.OpenInterestChange4hPct.Should().Be(d.OpenInterestChange4hPct);
        payload.Derivatives.LongRatio.Should().Be(d.LongRatio);
        payload.Derivatives.ShortRatio.Should().Be(d.ShortRatio);
        payload.Derivatives.PremiumVsIndexPct.Should().Be(d.PremiumVsIndexPct);
    }

    // ===========================================================================
    // OrderBook
    // ===========================================================================

    [Fact]
    public void ToMarketFacts_maps_order_book_fields()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var ob = snapshot.OrderBook;
        var payload = snapshot.ToMarketFacts(AnalysisMode.Intraday, _freshHealth);

        payload.OrderBook.CapturedAtUtc.Should().Be(ob.CapturedAtUtc);
        payload.OrderBook.BestBidPrice.Should().Be(ob.BestBidPrice);
        payload.OrderBook.BestAskPrice.Should().Be(ob.BestAskPrice);
        payload.OrderBook.TotalBidVolumeTop5.Should().Be(ob.TotalBidVolumeTop5);
        payload.OrderBook.TotalAskVolumeTop5.Should().Be(ob.TotalAskVolumeTop5);
        payload.OrderBook.TotalBidVolumeTop10.Should().Be(ob.TotalBidVolumeTop10);
        payload.OrderBook.TotalAskVolumeTop10.Should().Be(ob.TotalAskVolumeTop10);
        payload.OrderBook.TotalBidVolumeTop20.Should().Be(ob.TotalBidVolumeTop20);
        payload.OrderBook.TotalAskVolumeTop20.Should().Be(ob.TotalAskVolumeTop20);
        payload.OrderBook.ImbalanceTop5.Should().Be(ob.ImbalanceTop5);
        payload.OrderBook.ImbalanceTop10.Should().Be(ob.ImbalanceTop10);
        payload.OrderBook.ImbalanceTop20.Should().Be(ob.ImbalanceTop20);
        payload.OrderBook.BidWalls.Should().HaveCount(ob.BidWalls.Count);
        payload.OrderBook.AskWalls.Should().HaveCount(ob.AskWalls.Count);
        payload.OrderBook.PressureLabel.Should().NotBeNullOrEmpty();
        payload.OrderBook.LiquiditySkewLabel.Should().NotBeNullOrEmpty();
        // Spread is computed from best bid/ask
        payload.OrderBook.SpreadAbs.Should().Be(ob.BestAskPrice - ob.BestBidPrice);
    }

    // ===========================================================================
    // TradeFlow direction
    // ===========================================================================

    [Fact]
    public void ToMarketFacts_sets_trade_flow_direction_buy_dominant()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot() with
        {
            TradeFlow = ApiSnapshotTestData.CreateSnapshot().TradeFlow with
            {
                BuyVolume  = 120m,
                SellVolume = 80m,
            },
        };

        var payload = snapshot.ToMarketFacts(AnalysisMode.Intraday, _freshHealth);

        payload.TradeFlow.Direction.Should().Be("buy_dominant");
    }

    [Fact]
    public void ToMarketFacts_sets_trade_flow_direction_sell_dominant()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot() with
        {
            TradeFlow = ApiSnapshotTestData.CreateSnapshot().TradeFlow with
            {
                BuyVolume  = 70m,
                SellVolume = 130m,
            },
        };

        var payload = snapshot.ToMarketFacts(AnalysisMode.Intraday, _freshHealth);

        payload.TradeFlow.Direction.Should().Be("sell_dominant");
    }

    [Fact]
    public void ToMarketFacts_sets_trade_flow_direction_neutral_when_equal()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot() with
        {
            TradeFlow = ApiSnapshotTestData.CreateSnapshot().TradeFlow with
            {
                BuyVolume  = 100m,
                SellVolume = 100m,
            },
        };

        var payload = snapshot.ToMarketFacts(AnalysisMode.Intraday, _freshHealth);

        payload.TradeFlow.Direction.Should().Be("neutral");
    }

    // ===========================================================================
    // TradeFlow label
    // ===========================================================================

    [Fact]
    public void ToMarketFacts_sets_trade_flow_label_aggressive_buying()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot() with
        {
            TradeFlow = ApiSnapshotTestData.CreateSnapshot().TradeFlow with
            {
                HasAggressiveBuyPressure  = true,
                HasAggressiveSellPressure = false,
            },
        };

        var payload = snapshot.ToMarketFacts(AnalysisMode.Intraday, _freshHealth);

        payload.TradeFlow.Label.Should().Be("aggressive_buying");
    }

    [Fact]
    public void ToMarketFacts_sets_trade_flow_label_aggressive_selling()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot() with
        {
            TradeFlow = ApiSnapshotTestData.CreateSnapshot().TradeFlow with
            {
                HasAggressiveBuyPressure  = false,
                HasAggressiveSellPressure = true,
            },
        };

        var payload = snapshot.ToMarketFacts(AnalysisMode.Intraday, _freshHealth);

        payload.TradeFlow.Label.Should().Be("aggressive_selling");
    }

    [Fact]
    public void ToMarketFacts_sets_trade_flow_label_mixed_aggressive_pressure()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot() with
        {
            TradeFlow = ApiSnapshotTestData.CreateSnapshot().TradeFlow with
            {
                HasAggressiveBuyPressure  = true,
                HasAggressiveSellPressure = true,
            },
        };

        var payload = snapshot.ToMarketFacts(AnalysisMode.Intraday, _freshHealth);

        payload.TradeFlow.Label.Should().Be("mixed_aggressive_pressure");
    }

    [Fact]
    public void ToMarketFacts_sets_trade_flow_label_neutral()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot() with
        {
            TradeFlow = ApiSnapshotTestData.CreateSnapshot().TradeFlow with
            {
                HasAggressiveBuyPressure  = false,
                HasAggressiveSellPressure = false,
            },
        };

        var payload = snapshot.ToMarketFacts(AnalysisMode.Intraday, _freshHealth);

        payload.TradeFlow.Label.Should().Be("neutral");
    }

    // ===========================================================================
    // XRPUSDT regression: aggressive selling must not become aggressive buying
    // ===========================================================================

    [Fact]
    public void ToMarketFacts_preserves_xrpusdt_aggressive_selling_regression()
    {
        // Arrange: XRPUSDT-like scenario — strong sell dominance, aggressive selling flag
        var snapshot = ApiSnapshotTestData.CreateSnapshot() with
        {
            Symbol = "XRPUSDT",
            TradeFlow = new TradeFlowSnapshot
            {
                WindowStartUtc = new DateTimeOffset(2026, 6, 24, 10, 0, 0, TimeSpan.Zero),
                WindowEndUtc   = new DateTimeOffset(2026, 6, 24, 10, 15, 0, TimeSpan.Zero),
                BuyVolume      = 3_120_000m,
                SellVolume     = 5_010_000m,   // sell > buy → sell_dominant
                DeltaVolume    = -1_890_000m,
                DeltaPct       = -37.7m,       // negative delta
                TotalTrades    = 4200,
                BuyTrades      = 1850,
                SellTrades     = 2350,
                AvgTradeSize   = 1935m,
                MaxTradeSize   = 48_000m,
                HasAggressiveBuyPressure  = false,  // no buy pressure
                HasAggressiveSellPressure = true,   // aggressive selling
            },
        };

        // Act
        var payload = snapshot.ToMarketFacts(AnalysisMode.Intraday, _freshHealth);

        // Assert: core regression expectations
        payload.TradeFlow.DeltaPct.Should().Be(-37.7m,
            because: "deltaPct must be preserved as-is from snapshot");
        payload.TradeFlow.Direction.Should().Be("sell_dominant",
            because: "sellVolume > buyVolume → sell_dominant");
        payload.TradeFlow.Label.Should().Be("aggressive_selling",
            because: "hasAggressiveSellPressure=true, hasAggressiveBuyPressure=false → aggressive_selling");
        payload.DataQuality.Status.Should().Be("ok",
            because: "isFresh=true, isPartial=false → ok");
        payload.DataQuality.IsPartial.Should().BeFalse();

        // Guard: must NOT have flipped to buy-side
        payload.TradeFlow.Label.Should().NotBe("aggressive_buying",
            because: "regression guard: aggressive selling must never flip to aggressive buying");
        payload.DataQuality.Status.Should().NotBe("partial",
            because: "regression guard: fresh non-partial snapshot must never produce status=partial");
    }

    // ===========================================================================
    // Timeframes
    // ===========================================================================

    [Fact]
    public void ToMarketFacts_maps_timeframes_dictionary()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var payload = snapshot.ToMarketFacts(AnalysisMode.Intraday, _freshHealth);

        payload.Timeframes.Should().ContainKey("15m");
        payload.Timeframes.Should().ContainKey("1h");
        payload.Timeframes.Should().ContainKey("4h");
        payload.Timeframes.Should().ContainKey("1d");
    }

    [Fact]
    public void ToMarketFacts_timeframe_contains_required_sections()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var payload = snapshot.ToMarketFacts(AnalysisMode.Intraday, _freshHealth);

        foreach (var key in new[] { "15m", "1h", "4h", "1d" })
        {
            var tf = payload.Timeframes[key];
            tf.Timeframe.Should().Be(key);
            tf.Trend.Should().NotBeNull();
            tf.Indicators.Should().NotBeNull();
            tf.Levels.Should().NotBeNull();
            tf.DerivedFlags.Should().NotBeNull();
            tf.BackendSummary.Should().NotBeNull();
            tf.BackendSummary.RiskFlags.Should().NotBeNull();
        }
    }

    [Fact]
    public void ToMarketFacts_timeframe_maps_indicator_values()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var payload = snapshot.ToMarketFacts(AnalysisMode.Intraday, _freshHealth);

        var tf = payload.Timeframes["1h"];
        tf.Indicators.Ema20.Should().Be(snapshot.H1.Ema20);
        tf.Indicators.Ema50.Should().Be(snapshot.H1.Ema50);
        tf.Indicators.Ema200.Should().Be(snapshot.H1.Ema200);
        tf.Indicators.Rsi14.Should().Be(snapshot.H1.Rsi14);
        tf.Indicators.Atr14.Should().Be(snapshot.H1.Atr14);
        tf.Indicators.VolumeRatio.Should().Be(snapshot.H1.VolumeRatio);
    }

    [Fact]
    public void ToMarketFacts_timeframe_maps_derived_flags()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var payload = snapshot.ToMarketFacts(AnalysisMode.Intraday, _freshHealth);

        var tf = payload.Timeframes["4h"];
        tf.DerivedFlags.IsAboveEma20.Should().Be(snapshot.H4.IsAboveEma20);
        tf.DerivedFlags.IsAboveEma50.Should().Be(snapshot.H4.IsAboveEma50);
        tf.DerivedFlags.IsAboveEma200.Should().Be(snapshot.H4.IsAboveEma200);
        tf.DerivedFlags.EmaBullishAlignment.Should().Be(snapshot.H4.EmaBullishAlignment);
        tf.DerivedFlags.EmaBearishAlignment.Should().Be(snapshot.H4.EmaBearishAlignment);
        tf.DerivedFlags.RsiOverbought.Should().Be(snapshot.H4.RsiOverbought);
        tf.DerivedFlags.RsiOversold.Should().Be(snapshot.H4.RsiOversold);
    }

    private static readonly string[] _validEntryQualities = ["Good", "Fair", "Poor"];

    [Fact]
    public void ToMarketFacts_timeframe_backend_summary_entry_quality_is_valid_value()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var payload = snapshot.ToMarketFacts(AnalysisMode.Intraday, _freshHealth);

        foreach (var key in new[] { "15m", "1h", "4h", "1d" })
        {
            payload.Timeframes[key].BackendSummary.EntryQuality
                .Should().BeOneOf(_validEntryQualities,
                    because: $"timeframe {key} must have a valid EntryQuality value");
        }
    }

    // ===========================================================================
    // Aggregated Levels
    // ===========================================================================

    [Fact]
    public void ToMarketFacts_aggregates_support_levels()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var payload = snapshot.ToMarketFacts(AnalysisMode.Intraday, _freshHealth);

        // All 4 timeframes × support1 + support2 = up to 8 supports
        payload.Levels.Supports.Should().NotBeEmpty();

        var m15Support1 = payload.Levels.Supports.FirstOrDefault(
            l => l.Timeframe == "15m" && l.Rank == 1);
        m15Support1.Should().NotBeNull();
        m15Support1.Kind.Should().Be("support");
        m15Support1.Price.Should().Be(snapshot.M15.Support1);
        m15Support1.Strength.Should().Be(snapshot.M15.Support1Strength);
        m15Support1.Source.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ToMarketFacts_aggregates_resistance_levels()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var payload = snapshot.ToMarketFacts(AnalysisMode.Intraday, _freshHealth);

        payload.Levels.Resistances.Should().NotBeEmpty();

        var h1Resistance2 = payload.Levels.Resistances.FirstOrDefault(
            l => l.Timeframe == "1h" && l.Rank == 2);
        h1Resistance2.Should().NotBeNull();
        h1Resistance2.Kind.Should().Be("resistance");
        h1Resistance2.Price.Should().Be(snapshot.H1.Resistance2);
        h1Resistance2.Strength.Should().Be(snapshot.H1.Resistance2Strength);
    }

    [Fact]
    public void ToMarketFacts_skips_aggregated_level_when_price_missing()
    {
        // Arrange: snapshot with Support2 = null on all TFs
        var noSupport2Tf = ApiSnapshotTestData.CreateSnapshot().M15 with
        {
            Support2          = null,
            Support2Strength  = null,
            Support2ClusterVolume = null,
        };
        var snapshot = ApiSnapshotTestData.CreateSnapshot() with
        {
            M15 = noSupport2Tf,
            H1  = ApiSnapshotTestData.CreateSnapshot().H1 with { Support2 = null, Support2Strength = null },
            H4  = ApiSnapshotTestData.CreateSnapshot().H4 with { Support2 = null, Support2Strength = null },
            D1  = ApiSnapshotTestData.CreateSnapshot().D1 with { Support2 = null, Support2Strength = null },
        };

        var payload = snapshot.ToMarketFacts(AnalysisMode.Intraday, _freshHealth);

        payload.Levels.Supports.Should().NotContain(
            l => l.Rank == 2,
            because: "levels with null price must be omitted from aggregated list");
    }

    [Fact]
    public void ToMarketFacts_aggregated_level_has_correct_rank_and_kind()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var payload = snapshot.ToMarketFacts(AnalysisMode.Intraday, _freshHealth);

        foreach (var s in payload.Levels.Supports)
        {
            s.Kind.Should().Be("support");
            s.Rank.Should().BeOneOf(1, 2);
        }

        foreach (var r in payload.Levels.Resistances)
        {
            r.Kind.Should().Be("resistance");
            r.Rank.Should().BeOneOf(1, 2);
        }
    }

    // ===========================================================================
    // MarketInternalSentiment
    // ===========================================================================

    [Fact]
    public void ToMarketFacts_maps_market_internal_sentiment()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();
        var s = snapshot.Sentiment;
        var payload = snapshot.ToMarketFacts(AnalysisMode.Intraday, _freshHealth);

        payload.MarketInternalSentiment.LongShortBiasScore.Should().Be(s.LongShortBiasScore);
        payload.MarketInternalSentiment.FundingBiasScore.Should().Be(s.FundingBiasScore);
        payload.MarketInternalSentiment.OrderBookPressureScore.Should().Be(s.OrderBookPressureScore);
        payload.MarketInternalSentiment.TradeFlowPressureScore.Should().Be(s.TradeFlowPressureScore);
        payload.MarketInternalSentiment.MarketRegime.Should().Be(s.MarketRegime);
    }

    // ===========================================================================
    // Tags
    // ===========================================================================

    [Fact]
    public void ToMarketFacts_maps_tags()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot() with
        {
            Tags = ["trend", "momentum", "funding-spike"],
        };

        var payload = snapshot.ToMarketFacts(AnalysisMode.Intraday, _freshHealth);

        payload.Tags.Should().Contain("trend");
        payload.Tags.Should().Contain("momentum");
        payload.Tags.Should().Contain("funding-spike");
    }

    [Fact]
    public void ToMarketFacts_returns_empty_tags_when_snapshot_tags_empty()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot() with
        {
            Tags = [],
        };

        var payload = snapshot.ToMarketFacts(AnalysisMode.Intraday, _freshHealth);

        payload.Tags.Should().BeEmpty();
    }

    [Fact]
    public void ToMarketFacts_does_not_add_extra_tags()
    {
        var original = new List<string> { "volatility" };
        var snapshot = ApiSnapshotTestData.CreateSnapshot() with
        {
            Tags = original,
        };

        var payload = snapshot.ToMarketFacts(AnalysisMode.Intraday, _freshHealth);

        payload.Tags.Should().HaveCount(1,
            because: "mapper must not inject additional tags beyond those in the snapshot");
        payload.Tags.Should().Equal("volatility");
    }
}
