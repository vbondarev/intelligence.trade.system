namespace Intelligence.TradeSystem.Ai.Tests;

public sealed class PromptBuilderTests
{
    private readonly PromptBuilder _builder = new();

    [Fact]
    public void Throws_When_Request_Is_Null()
    {
        var action = () => _builder.Build(null!);

        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("request");
    }

    [Fact]
    public void Builds_System_And_User_Messages_In_Order()
    {
        var result = _builder.Build(PromptTestData.CreateRequest());

        result.Messages.Should().HaveCount(2);
        result.Messages[0].Role.Should().Be(PromptRole.System);
        result.Messages[1].Role.Should().Be(PromptRole.User);
        result.Messages[0].Content.Should().Contain("MarketAnalysisSnapshot");
        result.Messages[0].Content.Should().Contain("Analytics context");
        result.Messages[0].Content.Should().Contain("Не придумывай");
        result.Messages[1].Content.Should().StartWith("user_query:\nanalyze btc");
        result.Messages[1].Content.Should().Contain("\n\nmarket_analysis_snapshot_json:\n```json\n{");
    }

    [Fact]
    public void Includes_Analytics_Output_When_Present()
    {
        var request = PromptTestData.CreateRequest(analyticsOutput: PromptTestData.CreateAnalyticsOutput());

        var result = _builder.Build(request);

        result.Messages[1].Content.Should().Contain("analytics_output_market_regime:\nTrending");
        result.Messages[1].Content.Should().Contain("analytics_output_formatted_context:\nsnapshot:\n  regime: Trending\n  momentum: positive");
    }

    [Fact]
    public void Omits_Analytics_Output_When_Missing()
    {
        var result = _builder.Build(PromptTestData.CreateRequest());

        result.Messages[1].Content.Should().NotContain("analytics_output_market_regime:");
        result.Messages[1].Content.Should().NotContain("analytics_output_formatted_context:");
    }

    [Fact]
    public void Serializes_Snapshot_As_Indented_Json_Payload()
    {
        var result = _builder.Build(PromptTestData.CreateRequest());

        result.Messages[1].Content.Should().Contain("market_analysis_snapshot_json:\n```json\n{");
        result.Messages[1].Content.Should().Contain("\n  \"Exchange\": \"Bybit\"");
        result.Messages[1].Content.Should().Contain("\"CapturedAtUtc\": \"2026-04-12T14:00:00+00:00\"");
        result.Messages[1].Content.Should().Contain("\"Tags\": [");
        result.Messages[1].Content.Should().Contain("\"trend\"");
        result.Messages[1].Content.Should().Contain("\"momentum\"");
    }

    [Fact]
    public void Returns_Deterministic_Result_For_Same_Request()
    {
        var request = PromptTestData.CreateRequest(analyticsOutput: PromptTestData.CreateAnalyticsOutput());

        var firstResult = _builder.Build(request);
        var secondResult = _builder.Build(request);

        firstResult.Messages.Should().BeEquivalentTo(secondResult.Messages, options => options.WithStrictOrdering());
    }

    [Fact]
    public void Uses_Canonical_Line_Endings_For_Stable_Prompt_Content()
    {
        var result = _builder.Build(PromptTestData.CreateRequest(analyticsOutput: PromptTestData.CreateAnalyticsOutput()));

        result.Messages.Select(static message => message.Content)
            .Should().OnlyContain(static content => !content.Contains('\r'));
    }
}

