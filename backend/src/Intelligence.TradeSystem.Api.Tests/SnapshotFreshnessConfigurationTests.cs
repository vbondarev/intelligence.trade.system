using Intelligence.TradeSystem.Api.Configuration;
using Intelligence.TradeSystem.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Intelligence.TradeSystem.Api.Tests;

public sealed class SnapshotFreshnessConfigurationTests
{
    [Fact]
    public async Task Valid_configuration_starts_and_binds_values()
    {
        using var host = BuildHost(CreateValidConfiguration());

        await host.StartAsync();
        try
        {
            var options = host.Services.GetRequiredService<IOptions<SnapshotFreshnessOptions>>().Value;

            options.Intraday.PriceMaxAge.Should().Be(TimeSpan.FromSeconds(2));
            options.Intraday.TradeFlowMaxAge.Should().Be(TimeSpan.FromSeconds(5));
            options.Swing.DerivativesMaxAge.Should().Be(TimeSpan.FromMinutes(2));
            options.Portfolio.D1MaxAge.Should().Be(TimeSpan.FromMinutes(5));
            options.Portfolio.PortfolioMaxAge.Should().Be(TimeSpan.FromSeconds(30));
            options.Intraday.PortfolioMaxAge.Should().BeNull();
            options.Intraday.AggregatedContextMaxAge.Should().BeNull();
            options.StalenessProximityFactor.Should().Be(0.8m);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task Missing_snapshot_freshness_section_fails_on_start()
    {
        using var host = BuildHost(new Dictionary<string, string?>());

        var act = () => host.StartAsync();

        await act.Should().ThrowAsync<OptionsValidationException>();
    }

    [Theory]
    [InlineData("Intraday")]
    [InlineData("Swing")]
    [InlineData("Portfolio")]
    public async Task Missing_mode_section_fails_on_start(string mode)
    {
        var configuration = CreateValidConfiguration()
            .Where(pair => !pair.Key.StartsWith($"SnapshotFreshness:{mode}:", StringComparison.Ordinal))
            .ToDictionary();
        using var host = BuildHost(configuration);

        var act = () => host.StartAsync();

        (await act.Should().ThrowAsync<OptionsValidationException>())
            .Which.Failures.Should().Contain(failure => failure.Contains($"SnapshotFreshness:{mode}"));
    }

    [Fact]
    public async Task Zero_required_threshold_fails_on_start()
    {
        var configuration = CreateValidConfiguration();
        configuration["SnapshotFreshness:Swing:OrderBookMaxAge"] = "00:00:00";
        using var host = BuildHost(configuration);

        var act = () => host.StartAsync();

        (await act.Should().ThrowAsync<OptionsValidationException>())
            .Which.Failures.Should().Contain(failure =>
                failure.StartsWith("SnapshotFreshness:Swing:OrderBookMaxAge", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Invalid_staleness_proximity_factor_fails_on_start()
    {
        var configuration = CreateValidConfiguration();
        configuration["SnapshotFreshness:StalenessProximityFactor"] = "1";
        using var host = BuildHost(configuration);

        var act = () => host.StartAsync();

        (await act.Should().ThrowAsync<OptionsValidationException>())
            .Which.Failures.Should().Contain(failure =>
                failure.StartsWith("SnapshotFreshness:StalenessProximityFactor", StringComparison.Ordinal));
    }

    [Fact]
    public void Api_host_fails_during_creation_with_invalid_snapshot_freshness()
    {
        using var factory = new ApiWebApplicationFactory()
            .WithWebHostBuilder(builder =>
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(
                    [
                        new KeyValuePair<string, string?>("SnapshotFreshness:StalenessProximityFactor", "1"),
                    ])));

        var act = () => factory.CreateClient();

        act.Should().Throw<OptionsValidationException>()
            .Which.Failures.Should().Contain(failure =>
                failure.StartsWith("SnapshotFreshness:StalenessProximityFactor", StringComparison.Ordinal));
    }

    private static IHost BuildHost(IDictionary<string, string?> configuration)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(configuration);
        builder.Services.AddSnapshotHealthEvaluation(builder.Configuration);

        return builder.Build();
    }

    private static Dictionary<string, string?> CreateValidConfiguration()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        AddMode(values, "Intraday", "00:00:02", "00:00:30", "00:00:02", "00:00:05", "00:01:00");
        AddMode(values, "Swing", "00:00:10", "00:02:00", "00:00:15", "00:00:30", "00:05:00");
        AddMode(values, "Portfolio", "00:00:05", "00:01:00", "00:00:05", "00:00:10", "00:05:00");
        values["SnapshotFreshness:Portfolio:PortfolioMaxAge"] = "00:00:30";

        return values;
    }

    private static void AddMode(
        Dictionary<string, string?> values,
        string mode,
        string price,
        string derivatives,
        string orderBook,
        string tradeFlow,
        string timeframes)
    {
        var prefix = $"SnapshotFreshness:{mode}";
        values[$"{prefix}:PriceMaxAge"] = price;
        values[$"{prefix}:DerivativesMaxAge"] = derivatives;
        values[$"{prefix}:OrderBookMaxAge"] = orderBook;
        values[$"{prefix}:TradeFlowMaxAge"] = tradeFlow;
        values[$"{prefix}:M15MaxAge"] = timeframes;
        values[$"{prefix}:H1MaxAge"] = timeframes;
        values[$"{prefix}:H4MaxAge"] = timeframes;
        values[$"{prefix}:D1MaxAge"] = timeframes;
    }
}
