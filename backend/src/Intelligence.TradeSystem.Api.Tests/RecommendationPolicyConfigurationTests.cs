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
    public void Missing_connection_string_does_not_register_recommendation_service()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(CreateConfiguration());

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });

        provider.GetService<RecommendationService>().Should().BeNull();
    }

    [Fact]
    public void Configured_persistence_registers_recommendation_service_with_all_dependencies()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<RecommendationPolicy>();
        services.AddSingleton<RecommendationStabilityPolicy>();

        services.AddInfrastructure(
            CreateConfiguration(includePersistence: true));

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

        services.AddInfrastructure(CreateConfiguration(includePersistence: true));

        services.Should().ContainSingle(
            descriptor => descriptor.ServiceType == typeof(PositionEvaluationService));
    }

    private static IConfiguration CreateConfiguration(bool includePersistence = false)
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

        if (includePersistence)
        {
            values.Add(new("ConnectionStrings:TradeSystem", "Host=localhost;Database=tradesystem"));
            values.Add(new("CredentialProtection:ActiveKeyId", "test"));
            values.Add(new(
                "CredentialProtection:Keys:test",
                Convert.ToBase64String(new byte[32])));
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
