using Microsoft.Extensions.DependencyInjection;

namespace Intelligence.TradeSystem.Analytics.Tests;

public sealed class StartupExtensionsTests
{
    [Fact]
    public void AddAnalytics_Registers_Formatter_Classifier_And_OutputComposer()
    {
        var services = new ServiceCollection();

        services.AddAnalytics();

        using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
        using var scope = serviceProvider.CreateScope();

        var formatter = scope.ServiceProvider.GetRequiredService<IAnalyticsFormatter>();
        var classifier = scope.ServiceProvider.GetRequiredService<IMarketRegimeClassifier>();
        var composer = scope.ServiceProvider.GetRequiredService<IAnalyticsOutputComposer>();

        formatter.Should().BeOfType<SnapshotTextFormatter>();
        classifier.Should().BeOfType<MarketRegimeClassifier>();
        composer.Should().BeOfType<AnalyticsOutputComposer>();
    }

    [Fact]
    public void AddAnalytics_Uses_One_Scoped_Instance_Per_Registration_And_New_Instances_Per_Scope()
    {
        var services = new ServiceCollection();

        services.AddAnalytics();

        using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
        using var firstScope = serviceProvider.CreateScope();
        using var secondScope = serviceProvider.CreateScope();

        var firstFormatterA = firstScope.ServiceProvider.GetRequiredService<IAnalyticsFormatter>();
        var firstFormatterB = firstScope.ServiceProvider.GetRequiredService<IAnalyticsFormatter>();
        var firstClassifierA = firstScope.ServiceProvider.GetRequiredService<IMarketRegimeClassifier>();
        var firstClassifierB = firstScope.ServiceProvider.GetRequiredService<IMarketRegimeClassifier>();
        var firstComposerA = firstScope.ServiceProvider.GetRequiredService<IAnalyticsOutputComposer>();
        var firstComposerB = firstScope.ServiceProvider.GetRequiredService<IAnalyticsOutputComposer>();

        var secondFormatter = secondScope.ServiceProvider.GetRequiredService<IAnalyticsFormatter>();
        var secondClassifier = secondScope.ServiceProvider.GetRequiredService<IMarketRegimeClassifier>();
        var secondComposer = secondScope.ServiceProvider.GetRequiredService<IAnalyticsOutputComposer>();

        firstFormatterA.Should().BeSameAs(firstFormatterB);
        firstClassifierA.Should().BeSameAs(firstClassifierB);
        firstComposerA.Should().BeSameAs(firstComposerB);

        firstFormatterA.Should().NotBeSameAs(secondFormatter);
        firstClassifierA.Should().NotBeSameAs(secondClassifier);
        firstComposerA.Should().NotBeSameAs(secondComposer);
    }

}

