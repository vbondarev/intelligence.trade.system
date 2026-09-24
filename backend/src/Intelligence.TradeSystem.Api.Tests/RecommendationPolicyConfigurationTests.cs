using FluentAssertions;
using Intelligence.TradeSystem.Application;
using Intelligence.TradeSystem.Infrastructure;
using Intelligence.TradeSystem.Application.Evaluations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Intelligence.TradeSystem.Application.Assessments;
using Intelligence.TradeSystem.Application.Recommendations;
using Intelligence.TradeSystem.Domain.Recommendations;
using Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;

namespace Intelligence.TradeSystem.Api.Tests;

public sealed class RecommendationPolicyConfigurationTests
{
    [Fact]
    public void Missing_policy_path_fails_fast()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        Action act = () => services.AddInfrastructure(configuration);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*RecommendationPolicy:Path*");
    }

    [Fact]
    public void Missing_policy_file_fails_fast()
    {
        var services = new ServiceCollection();
        var missingPath = Path.Combine(
            Path.GetTempPath(),
            $"missing-recommendation-policy-{Guid.NewGuid():N}.json");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("RecommendationPolicy:Path", missingPath)
            ])
            .Build();

        Action act = () => services.AddInfrastructure(configuration);

        act.Should().Throw<FileNotFoundException>().Which.FileName.Should().Be(missingPath);
    }

    [Fact]
    public void Missing_connection_string_fails_fast()
    {
        var services = new ServiceCollection();
        var configuration = CreateConfiguration();
        configuration["ConnectionStrings:TradeSystem"] = null;

        var act = () => services.AddInfrastructure(configuration);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConnectionStrings:TradeSystem*");
    }

    [Fact]
    public void Blank_connection_string_fails_fast()
    {
        var services = new ServiceCollection();
        var configuration = CreateConfiguration();
        configuration["ConnectionStrings:TradeSystem"] = " ";

        var act = () => services.AddInfrastructure(configuration);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConnectionStrings:TradeSystem*");
    }

    [Fact]
    public void Missing_credential_protection_active_key_fails_fast()
    {
        var values = CreateConfiguration();
        values["CredentialProtection:ActiveKeyId"] = null;

        var act = () => new ServiceCollection().AddInfrastructure(values);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*CredentialProtection:ActiveKeyId*");
    }

    [Fact]
    public void Missing_credential_protection_keys_fails_fast()
    {
        var values = new ConfigurationBuilder()
            .AddInMemoryCollection(
                CreateConfiguration()
                    .AsEnumerable()
                    .Where(pair =>
                        !pair.Key.StartsWith(
                            "CredentialProtection:Keys:",
                            StringComparison.OrdinalIgnoreCase)))
            .Build();

        var act = () => new ServiceCollection().AddInfrastructure(values);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*CredentialProtection:Keys*");
    }

    [Fact]
    public void Invalid_credential_protection_key_fails_fast()
    {
        var values = CreateConfiguration();
        values["CredentialProtection:Keys:test"] = "not-base64";

        var act = () => new ServiceCollection().AddInfrastructure(values);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not valid Base64*");
    }

    [Fact]
    public void Configured_persistence_registers_recommendation_service_with_all_dependencies()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<RecommendationPolicy>();
        services.AddSingleton<RecommendationStabilityPolicy>();

        services.AddInfrastructure(
            CreateConfiguration());

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
        using var scope = provider.CreateScope();

        var serviceProvider = scope.ServiceProvider;
        serviceProvider.GetRequiredService<RecommendationService>().Should().NotBeNull();
        serviceProvider.GetRequiredService<IRecommendationRepository>()
            .Should().BeOfType<RecommendationRepository>();
        serviceProvider.GetRequiredService<IRecommendationStabilityStateRepository>()
            .Should().BeOfType<RecommendationStabilityStateRepository>();
        serviceProvider.GetRequiredService<IRecommendationPublicationTransaction>()
            .Should().NotBeNull();
        serviceProvider.GetRequiredService<IPositionAssessmentRepository>()
            .Should().NotBeNull();
    }

    [Fact]
    public void Application_composition_registers_position_evaluation_service_before_cached_market_replacement()
    {
        var services = new ServiceCollection();
        services.AddApplication();

        services.AddInfrastructure(CreateConfiguration());

        services.Should().ContainSingle(
            descriptor => descriptor.ServiceType == typeof(PositionEvaluationService));
    }

    private static IConfiguration CreateConfiguration()
    {
        var values = new List<KeyValuePair<string, string?>>
        {
            new("RecommendationPolicy:Path", Path.Combine(
                AppContext.BaseDirectory,
                "Configuration",
                "recommendation-policy.json")),
            new("PositionEvaluationPolicy:AssessmentRules:Version", "assessment-v1"),
            new("PositionEvaluationPolicy:AssessmentRules:RsiOverbought", "70"),
            new("PositionEvaluationPolicy:AssessmentRules:RsiOversold", "30"),
            new("PositionEvaluationPolicy:AssessmentRules:NearbyLevelPercent", "1"),
            new("PositionEvaluationPolicy:AssessmentRules:LiquidationDangerPercent", "5"),
            new("PositionEvaluationPolicy:AssessmentRules:LowVolumeRatio", "0.5"),
            new("PositionEvaluationPolicy:AssessmentRules:ValidityPeriod", "00:05:00"),
            new("PositionEvaluationPolicy:PortfolioRisk:MinimumFreeCapitalPercent", "20"),
            new("PositionEvaluationPolicy:PortfolioRisk:MaximumGrossExposureToEquityPercent", "200"),
            new("PositionEvaluationPolicy:PortfolioRisk:MaximumPositionConcentrationPercent", "50"),
        };

        values.Add(new("ConnectionStrings:TradeSystem", "Host=localhost;Database=tradesystem"));
        values.Add(new("CredentialProtection:ActiveKeyId", "test"));
        values.Add(new(
            "CredentialProtection:Keys:test",
            Convert.ToBase64String(new byte[32])));

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
