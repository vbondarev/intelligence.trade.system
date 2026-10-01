using Intelligence.TradeSystem.Api.Configuration;
using Microsoft.Extensions.Options;

namespace Intelligence.TradeSystem.Api.Tests;

public sealed class SnapshotFreshnessOptionsValidatorTests
{
    private static readonly string[] Modes =
    [
        nameof(SnapshotFreshnessOptions.Intraday),
        nameof(SnapshotFreshnessOptions.Swing),
        nameof(SnapshotFreshnessOptions.Portfolio),
    ];

    private static readonly string[] RequiredThresholds =
    [
        nameof(SectionFreshnessOptions.PriceMaxAge),
        nameof(SectionFreshnessOptions.DerivativesMaxAge),
        nameof(SectionFreshnessOptions.OrderBookMaxAge),
        nameof(SectionFreshnessOptions.TradeFlowMaxAge),
        nameof(SectionFreshnessOptions.M15MaxAge),
        nameof(SectionFreshnessOptions.H1MaxAge),
        nameof(SectionFreshnessOptions.H4MaxAge),
        nameof(SectionFreshnessOptions.D1MaxAge),
    ];

    public static TheoryData<string, string> RequiredThresholdCases()
    {
        var data = new TheoryData<string, string>();
        foreach (var mode in Modes)
        {
            foreach (var threshold in RequiredThresholds)
            {
                data.Add(mode, threshold);
            }
        }

        return data;
    }

    [Fact]
    public void Valid_options_without_optional_thresholds_succeed()
    {
        var result = Validate(CreateValidOptions());

        result.Should().Be(ValidateOptionsResult.Success);
    }

    [Fact]
    public void Valid_options_with_positive_optional_thresholds_succeed()
    {
        var section = CreateValidSection() with
        {
            PortfolioMaxAge = TimeSpan.FromSeconds(30),
            AggregatedContextMaxAge = TimeSpan.FromSeconds(30),
        };
        var options = CreateValidOptions() with
        {
            Intraday = section,
            Swing = section,
            Portfolio = section,
        };

        Validate(options).Should().Be(ValidateOptionsResult.Success);
    }

    [Theory]
    [InlineData(nameof(SnapshotFreshnessOptions.Intraday))]
    [InlineData(nameof(SnapshotFreshnessOptions.Swing))]
    [InlineData(nameof(SnapshotFreshnessOptions.Portfolio))]
    public void Missing_mode_section_fails(string mode)
    {
        var options = WithSection(CreateValidOptions(), mode, null!);

        var result = Validate(options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainSingle()
            .Which.Should().Contain($"SnapshotFreshness:{mode}");
    }

    [Theory]
    [MemberData(nameof(RequiredThresholdCases))]
    public void Zero_required_threshold_fails(string mode, string threshold)
    {
        AssertSingleThresholdFailure(mode, threshold, TimeSpan.Zero);
    }

    [Theory]
    [MemberData(nameof(RequiredThresholdCases))]
    public void Negative_required_threshold_fails(string mode, string threshold)
    {
        AssertSingleThresholdFailure(mode, threshold, TimeSpan.FromSeconds(-1));
    }

    [Fact]
    public void All_invalid_required_thresholds_are_reported_together()
    {
        var invalidSection = new SectionFreshnessOptions
        {
            PriceMaxAge = TimeSpan.Zero,
            DerivativesMaxAge = TimeSpan.Zero,
            OrderBookMaxAge = TimeSpan.Zero,
            TradeFlowMaxAge = TimeSpan.Zero,
            M15MaxAge = TimeSpan.Zero,
            H1MaxAge = TimeSpan.Zero,
            H4MaxAge = TimeSpan.Zero,
            D1MaxAge = TimeSpan.Zero,
        };
        var options = CreateValidOptions() with
        {
            Intraday = invalidSection,
            Swing = invalidSection,
            Portfolio = invalidSection,
        };

        var result = Validate(options);

        var expectedPaths = Modes
            .SelectMany(mode => RequiredThresholds.Select(threshold => $"SnapshotFreshness:{mode}:{threshold} "))
            .ToArray();
        result.Failures.Should().HaveCount(expectedPaths.Length);
        foreach (var path in expectedPaths)
        {
            result.Failures.Should().Contain(failure => failure.StartsWith(path, StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData(nameof(SectionFreshnessOptions.PortfolioMaxAge), 0)]
    [InlineData(nameof(SectionFreshnessOptions.PortfolioMaxAge), -1)]
    [InlineData(nameof(SectionFreshnessOptions.AggregatedContextMaxAge), 0)]
    [InlineData(nameof(SectionFreshnessOptions.AggregatedContextMaxAge), -1)]
    public void Non_positive_optional_threshold_fails(string threshold, int seconds)
    {
        foreach (var mode in Modes)
        {
            var section = WithOptionalThreshold(CreateValidSection(), threshold, TimeSpan.FromSeconds(seconds));
            var options = WithSection(CreateValidOptions(), mode, section);

            var result = Validate(options);

            result.Failures.Should().ContainSingle()
                .Which.Should().StartWith($"SnapshotFreshness:{mode}:{threshold}");
        }
    }

    [Theory]
    [InlineData(nameof(SectionFreshnessOptions.PortfolioMaxAge))]
    [InlineData(nameof(SectionFreshnessOptions.AggregatedContextMaxAge))]
    public void Missing_or_positive_optional_threshold_succeeds(string threshold)
    {
        foreach (var value in new TimeSpan?[] { null, TimeSpan.FromSeconds(1) })
        {
            var section = WithOptionalThreshold(CreateValidSection(), threshold, value);
            var options = CreateValidOptions() with { Portfolio = section };

            Validate(options).Should().Be(ValidateOptionsResult.Success);
        }
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1.1)]
    public void Staleness_proximity_factor_outside_open_unit_interval_fails(double factor)
    {
        var options = CreateValidOptions() with { StalenessProximityFactor = (decimal)factor };

        var result = Validate(options);

        result.Failures.Should().ContainSingle()
            .Which.Should().StartWith("SnapshotFreshness:StalenessProximityFactor");
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(0.8)]
    public void Staleness_proximity_factor_inside_open_unit_interval_succeeds(double factor)
    {
        var options = CreateValidOptions() with { StalenessProximityFactor = (decimal)factor };

        Validate(options).Should().Be(ValidateOptionsResult.Success);
    }

    [Fact]
    public void Default_staleness_proximity_factor_is_valid()
    {
        var options = CreateValidOptions();

        options.StalenessProximityFactor.Should().Be(0.8m);
        Validate(options).Should().Be(ValidateOptionsResult.Success);
    }

    private static void AssertSingleThresholdFailure(string mode, string threshold, TimeSpan value)
    {
        var section = WithRequiredThreshold(CreateValidSection(), threshold, value);
        var options = WithSection(CreateValidOptions(), mode, section);

        var result = Validate(options);

        result.Failures.Should().ContainSingle()
            .Which.Should().StartWith($"SnapshotFreshness:{mode}:{threshold} ");
    }

    private static ValidateOptionsResult Validate(SnapshotFreshnessOptions options) =>
        new SnapshotFreshnessOptionsValidator().Validate(Options.DefaultName, options);

    private static SnapshotFreshnessOptions CreateValidOptions() => new()
    {
        Intraday = CreateValidSection(),
        Swing = CreateValidSection(),
        Portfolio = CreateValidSection(),
    };

    private static SectionFreshnessOptions CreateValidSection() => new()
    {
        PriceMaxAge = TimeSpan.FromSeconds(1),
        DerivativesMaxAge = TimeSpan.FromSeconds(1),
        OrderBookMaxAge = TimeSpan.FromSeconds(1),
        TradeFlowMaxAge = TimeSpan.FromSeconds(1),
        M15MaxAge = TimeSpan.FromSeconds(1),
        H1MaxAge = TimeSpan.FromSeconds(1),
        H4MaxAge = TimeSpan.FromSeconds(1),
        D1MaxAge = TimeSpan.FromSeconds(1),
    };

    private static SnapshotFreshnessOptions WithSection(
        SnapshotFreshnessOptions options,
        string mode,
        SectionFreshnessOptions section) => mode switch
    {
        nameof(SnapshotFreshnessOptions.Intraday) => options with { Intraday = section },
        nameof(SnapshotFreshnessOptions.Swing) => options with { Swing = section },
        nameof(SnapshotFreshnessOptions.Portfolio) => options with { Portfolio = section },
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    private static SectionFreshnessOptions WithRequiredThreshold(
        SectionFreshnessOptions section,
        string threshold,
        TimeSpan value) => threshold switch
    {
        nameof(SectionFreshnessOptions.PriceMaxAge) => section with { PriceMaxAge = value },
        nameof(SectionFreshnessOptions.DerivativesMaxAge) => section with { DerivativesMaxAge = value },
        nameof(SectionFreshnessOptions.OrderBookMaxAge) => section with { OrderBookMaxAge = value },
        nameof(SectionFreshnessOptions.TradeFlowMaxAge) => section with { TradeFlowMaxAge = value },
        nameof(SectionFreshnessOptions.M15MaxAge) => section with { M15MaxAge = value },
        nameof(SectionFreshnessOptions.H1MaxAge) => section with { H1MaxAge = value },
        nameof(SectionFreshnessOptions.H4MaxAge) => section with { H4MaxAge = value },
        nameof(SectionFreshnessOptions.D1MaxAge) => section with { D1MaxAge = value },
        _ => throw new ArgumentOutOfRangeException(nameof(threshold), threshold, null),
    };

    private static SectionFreshnessOptions WithOptionalThreshold(
        SectionFreshnessOptions section,
        string threshold,
        TimeSpan? value) => threshold switch
    {
        nameof(SectionFreshnessOptions.PortfolioMaxAge) => section with { PortfolioMaxAge = value },
        nameof(SectionFreshnessOptions.AggregatedContextMaxAge) => section with { AggregatedContextMaxAge = value },
        _ => throw new ArgumentOutOfRangeException(nameof(threshold), threshold, null),
    };
}
