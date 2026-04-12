namespace Intelligence.TradeSystem.Ai.Tests;

public sealed class PromptBuildRequestTests
{
    [Fact]
    public void Throws_When_Snapshot_Is_Null()
    {
        var action = () => new PromptBuildRequest(null!, "analyze");

        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("snapshot");
    }

    [Fact]
    public void Throws_When_UserQuery_Is_Null()
    {
        const string parameterName = "userQuery";
        var action = () => new PromptBuildRequest(PromptTestData.CreateSnapshot(), null!);

        action.Should().Throw<ArgumentException>()
            .WithParameterName(parameterName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Throws_When_UserQuery_Is_Whitespace(string userQuery)
    {
        const string parameterName = "userQuery";
        var action = () => new PromptBuildRequest(PromptTestData.CreateSnapshot(), userQuery);

        action.Should().Throw<ArgumentException>()
            .WithParameterName(parameterName);
    }

    [Fact]
    public void Stores_Snapshot_UserQuery_And_Optional_AnalyticsOutput()
    {
        var snapshot = PromptTestData.CreateSnapshot();
        var analyticsOutput = PromptTestData.CreateAnalyticsOutput();

        var request = new PromptBuildRequest(snapshot, "analyze btc", analyticsOutput);

        request.Snapshot.Should().BeSameAs(snapshot);
        request.UserQuery.Should().Be("analyze btc");
        request.AnalyticsOutput.Should().BeSameAs(analyticsOutput);
    }

    [Fact]
    public void Allows_Missing_AnalyticsOutput()
    {
        var request = new PromptBuildRequest(PromptTestData.CreateSnapshot(), "analyze btc");

        request.AnalyticsOutput.Should().BeNull();
    }
}


