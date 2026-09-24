using Intelligence.TradeSystem.Application.Evaluations;
using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Portfolio;
using Intelligence.TradeSystem.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Intelligence.TradeSystem.Api.Tests;

public sealed class PositionEvaluationPolicyConfigurationTests
{
    private static readonly string[] AssessmentRuleNames =
    [
        "Version",
        "RsiOverbought",
        "RsiOversold",
        "NearbyLevelPercent",
        "LiquidationDangerPercent",
        "LowVolumeRatio",
        "ValidityPeriod",
    ];

    private static readonly string[] PortfolioRiskNames =
    [
        "MinimumFreeCapitalPercent",
        "MaximumGrossExposureToEquityPercent",
        "MaximumPositionConcentrationPercent",
    ];

    [Fact]
    public void Missing_position_evaluation_section_fails_fast()
    {
        var values = CreateValues();
        RemoveSection(values, "PositionEvaluationPolicy");

        AssertConfigurationFails(values, "PositionEvaluationPolicy");
    }

    [Fact]
    public void Missing_assessment_rules_section_fails_fast()
    {
        var values = CreateValues();
        RemoveSection(values, "PositionEvaluationPolicy:AssessmentRules");

        AssertConfigurationFails(values, "AssessmentRules");
    }

    [Fact]
    public void Missing_portfolio_risk_section_fails_fast()
    {
        var values = CreateValues();
        RemoveSection(values, "PositionEvaluationPolicy:PortfolioRisk");

        AssertConfigurationFails(values, "PortfolioRisk");
    }

    [Theory]
    [MemberData(nameof(AssessmentRuleKeys))]
    public void Missing_assessment_rule_value_fails_fast(string key)
    {
        var values = CreateValues();
        values.Remove($"PositionEvaluationPolicy:AssessmentRules:{key}");

        AssertConfigurationFails(values, "assessment");
    }

    [Theory]
    [MemberData(nameof(PortfolioRiskKeys))]
    public void Missing_portfolio_risk_value_fails_fast(string key)
    {
        var values = CreateValues();
        values.Remove($"PositionEvaluationPolicy:PortfolioRisk:{key}");

        AssertConfigurationFails(values, "portfolio");
    }

    [Theory]
    [InlineData("PositionEvaluationPolicy:AssessmentRules:RsiOversold", "70")]
    [InlineData("PositionEvaluationPolicy:AssessmentRules:RsiOverbought", "101")]
    [InlineData("PositionEvaluationPolicy:AssessmentRules:ValidityPeriod", "00:00:00")]
    [InlineData("PositionEvaluationPolicy:AssessmentRules:NearbyLevelPercent", "-1")]
    [InlineData("PositionEvaluationPolicy:AssessmentRules:LiquidationDangerPercent", "-1")]
    [InlineData("PositionEvaluationPolicy:AssessmentRules:LowVolumeRatio", "-1")]
    [InlineData("PositionEvaluationPolicy:PortfolioRisk:MinimumFreeCapitalPercent", "-1")]
    [InlineData("PositionEvaluationPolicy:PortfolioRisk:MinimumFreeCapitalPercent", "101")]
    [InlineData("PositionEvaluationPolicy:PortfolioRisk:MaximumGrossExposureToEquityPercent", "-1")]
    [InlineData("PositionEvaluationPolicy:PortfolioRisk:MaximumPositionConcentrationPercent", "-1")]
    [InlineData("PositionEvaluationPolicy:PortfolioRisk:MaximumPositionConcentrationPercent", "101")]
    public void Invalid_position_evaluation_values_fail_fast(string key, string value)
    {
        var values = CreateValues();
        values[key] = value;

        AssertConfigurationFails(values, null);
    }

    [Fact]
    public void Production_api_configuration_registers_approved_immutable_settings()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(CreateValues());
        services.AddInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();
        var settings = provider.GetRequiredService<PositionEvaluationPolicySettings>();

        Assert.Equal(20m, settings.PortfolioRisk.MinimumFreeCapitalPercent);
        Assert.Equal(200m, settings.PortfolioRisk.MaximumGrossExposureToEquityPercent);
        Assert.Equal(50m, settings.PortfolioRisk.MaximumPositionConcentrationPercent);
        Assert.Equal("assessment-v1", settings.AssessmentRules.Version.Value);
        Assert.Equal(TimeSpan.FromMinutes(5), settings.AssessmentRules.ValidityPeriod);
    }

    [Fact]
    public void Effective_configuration_identity_is_stable_and_sensitive_to_risk_settings()
    {
        var baseIdentity = new PolicyConfigurationIdentity("policy-v1", "sha256:policy");
        var rules = PositionAssessmentRules.Default;
        var first = PositionAssessmentConfigurationIdentity.Compose(
            baseIdentity,
            new PortfolioRiskPolicySettings(20m, 200m, 50m),
            rules,
            AssessmentDataQuality.FreshCompleteReliable,
            AssessmentDataQuality.FreshCompleteReliable);
        var same = PositionAssessmentConfigurationIdentity.Compose(
            baseIdentity,
            new PortfolioRiskPolicySettings(20m, 200m, 50m),
            rules,
            AssessmentDataQuality.FreshCompleteReliable,
            AssessmentDataQuality.FreshCompleteReliable);
        var changed = PositionAssessmentConfigurationIdentity.Compose(
            baseIdentity,
            new PortfolioRiskPolicySettings(20m, 200m, 51m),
            rules,
            AssessmentDataQuality.FreshCompleteReliable,
            AssessmentDataQuality.FreshCompleteReliable);

        Assert.Equal(first, same);
        Assert.NotEqual(first, changed);
    }

    public static IEnumerable<object[]> AssessmentRuleKeys() =>
        AssessmentRuleNames.Select(key => new object[] { key });

    public static IEnumerable<object[]> PortfolioRiskKeys() =>
        PortfolioRiskNames.Select(key => new object[] { key });

    private static void AssertConfigurationFails(
        IDictionary<string, string?> values,
        string? expectedMessage)
    {
        var exception = Assert.ThrowsAny<Exception>(
            () => new ServiceCollection().AddInfrastructure(BuildConfiguration(values)));
        if (expectedMessage is not null)
        {
            Assert.Contains(expectedMessage, exception.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static IConfiguration BuildConfiguration(
        IDictionary<string, string?> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

    private static Dictionary<string, string?> CreateValues() =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["RecommendationPolicy:Path"] = Path.Combine(
                AppContext.BaseDirectory,
                "Configuration",
                "recommendation-policy.json"),
            ["PositionEvaluationPolicy:AssessmentRules:Version"] = "assessment-v1",
            ["PositionEvaluationPolicy:AssessmentRules:RsiOverbought"] = "70",
            ["PositionEvaluationPolicy:AssessmentRules:RsiOversold"] = "30",
            ["PositionEvaluationPolicy:AssessmentRules:NearbyLevelPercent"] = "1",
            ["PositionEvaluationPolicy:AssessmentRules:LiquidationDangerPercent"] = "5",
            ["PositionEvaluationPolicy:AssessmentRules:LowVolumeRatio"] = "0.5",
            ["PositionEvaluationPolicy:AssessmentRules:ValidityPeriod"] = "00:05:00",
            ["PositionEvaluationPolicy:PortfolioRisk:MinimumFreeCapitalPercent"] = "20",
            ["PositionEvaluationPolicy:PortfolioRisk:MaximumGrossExposureToEquityPercent"] = "200",
            ["PositionEvaluationPolicy:PortfolioRisk:MaximumPositionConcentrationPercent"] = "50",
            ["ConnectionStrings:TradeSystem"] = "Host=localhost;Database=tradesystem",
            ["CredentialProtection:ActiveKeyId"] = "test",
            ["CredentialProtection:Keys:test"] = Convert.ToBase64String(new byte[32]),
        };

    private static void RemoveSection(
        IDictionary<string, string?> values,
        string section)
    {
        foreach (var key in values.Keys
                     .Where(key => key.StartsWith(section + ":", StringComparison.OrdinalIgnoreCase))
                     .ToArray())
        {
            values.Remove(key);
        }
    }
}
