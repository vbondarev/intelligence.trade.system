using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Ai.Tests;

public sealed class ILlmAnalyticsServiceContractTests
{
    [Fact]
    public void Declares_Single_AnalyzeAsync_Method_With_Expected_Signature()
    {
        var methods = typeof(ILlmAnalyticsService).GetMethods();

        methods.Should().ContainSingle();

        var method = methods[0];
        method.Name.Should().Be("AnalyzeAsync");
        method.ReturnType.Should().Be<Task<string>>();

        var parameters = method.GetParameters();
        parameters.Should().HaveCount(3);
        parameters[0].ParameterType.Should().Be<MarketAnalysisSnapshot>();
        parameters[0].Name.Should().Be("snapshot");
        parameters[1].ParameterType.Should().Be<string>();
        parameters[1].Name.Should().Be("userQuery");
        parameters[2].ParameterType.Should().Be<CancellationToken>();
        parameters[2].Name.Should().Be("cancellationToken");
    }

    [Fact]
    public void Declares_Optional_CancellationToken_For_Asynchronous_Consumers()
    {
        var method = typeof(ILlmAnalyticsService).GetMethod("AnalyzeAsync");

        method.Should().NotBeNull();

        var cancellationTokenParameter = method
            .GetParameters()
            .Single(static parameter => parameter.Name == "cancellationToken");

        cancellationTokenParameter.IsOptional.Should().BeTrue();
    }
}



