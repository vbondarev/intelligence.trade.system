using FluentAssertions;
using Intelligence.TradeSystem.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
}
