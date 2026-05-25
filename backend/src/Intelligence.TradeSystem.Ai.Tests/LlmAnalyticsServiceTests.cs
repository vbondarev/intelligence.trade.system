using Intelligence.TradeSystem.Analytics;
using Moq;

namespace Intelligence.TradeSystem.Ai.Tests;

public sealed class LlmAnalyticsServiceTests
{
    [Fact]
    public void Constructor_Throws_When_AnalyticsOutputComposer_Is_Null()
    {
        var action = () => new LlmAnalyticsService(
            null!,
            new Mock<IPromptBuilder>().Object,
            new Mock<IOpenRouterClient>().Object);

        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("analyticsOutputComposer");
    }

    [Fact]
    public void Constructor_Throws_When_PromptBuilder_Is_Null()
    {
        var action = () => new LlmAnalyticsService(
            new Mock<IAnalyticsOutputComposer>().Object,
            null!,
            new Mock<IOpenRouterClient>().Object);

        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("promptBuilder");
    }

    [Fact]
    public void Constructor_Throws_When_OpenRouterClient_Is_Null()
    {
        var action = () => new LlmAnalyticsService(
            new Mock<IAnalyticsOutputComposer>().Object,
            new Mock<IPromptBuilder>().Object,
            null!);

        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("openRouterClient");
    }

    [Fact]
    public async Task AnalyzeAsync_Throws_When_Snapshot_Is_Null()
    {
        var service = CreateService();

        var action = () => service.AnalyzeAsync(null!, "analyze btc");

        await action.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("snapshot");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public async Task AnalyzeAsync_Throws_When_UserQuery_Is_Null_Or_Whitespace(string? userQuery)
    {
        var service = CreateService();

        var action = () => service.AnalyzeAsync(PromptTestData.CreateSnapshot(), userQuery!);

        await action.Should().ThrowAsync<ArgumentException>()
            .WithParameterName(nameof(userQuery));
    }

    [Fact]
    public async Task AnalyzeAsync_Composes_Builds_And_Sends_Prompt_In_Order()
    {
        var snapshot = PromptTestData.CreateSnapshot();
        var analyticsOutput = PromptTestData.CreateAnalyticsOutput();
        var prompt = new PromptBuildResult(
        [
            new(PromptRole.System, "system"),
            new(PromptRole.User, "user"),
        ]);
        var cancellationToken = new CancellationTokenSource().Token;
        var sequence = new MockSequence();
        PromptBuildRequest? capturedRequest = null;

        var analyticsOutputComposer = new Mock<IAnalyticsOutputComposer>(MockBehavior.Strict);
        analyticsOutputComposer
            .InSequence(sequence)
            .Setup(x => x.Compose(snapshot))
            .Returns(analyticsOutput);

        var promptBuilder = new Mock<IPromptBuilder>(MockBehavior.Strict);
        promptBuilder
            .InSequence(sequence)
            .Setup(x => x.Build(It.IsAny<PromptBuildRequest>()))
            .Callback<PromptBuildRequest>(request => capturedRequest = request)
            .Returns(prompt);

        var openRouterClient = new Mock<IOpenRouterClient>(MockBehavior.Strict);
        openRouterClient
            .InSequence(sequence)
            .Setup(x => x.CompleteChatAsync(prompt, cancellationToken))
            .ReturnsAsync("market summary");

        var service = new LlmAnalyticsService(
            analyticsOutputComposer.Object,
            promptBuilder.Object,
            openRouterClient.Object);

        var result = await service.AnalyzeAsync(snapshot, "analyze btc", cancellationToken);

        result.Should().Be("market summary");
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Snapshot.Should().BeSameAs(snapshot);
        capturedRequest.UserQuery.Should().Be("analyze btc");
        capturedRequest.AnalyticsOutput.Should().BeSameAs(analyticsOutput);
        analyticsOutputComposer.Verify(x => x.Compose(snapshot), Times.Once);
        promptBuilder.Verify(x => x.Build(It.IsAny<PromptBuildRequest>()), Times.Once);
        openRouterClient.Verify(x => x.CompleteChatAsync(prompt, cancellationToken), Times.Once);
    }

    [Fact]
    public async Task AnalyzeAsync_Passes_CancellationToken_To_OpenRouterClient()
    {
        var snapshot = PromptTestData.CreateSnapshot();
        var analyticsOutput = PromptTestData.CreateAnalyticsOutput();
        var prompt = new PromptBuildResult([new(PromptRole.User, "payload")]);
        var cancellationToken = new CancellationTokenSource().Token;

        var analyticsOutputComposer = new Mock<IAnalyticsOutputComposer>();
        analyticsOutputComposer
            .Setup(x => x.Compose(snapshot))
            .Returns(analyticsOutput);

        var promptBuilder = new Mock<IPromptBuilder>();
        promptBuilder
            .Setup(x => x.Build(It.IsAny<PromptBuildRequest>()))
            .Returns(prompt);

        var openRouterClient = new Mock<IOpenRouterClient>();
        openRouterClient
            .Setup(x => x.CompleteChatAsync(prompt, cancellationToken))
            .ReturnsAsync("ok");

        var service = new LlmAnalyticsService(
            analyticsOutputComposer.Object,
            promptBuilder.Object,
            openRouterClient.Object);

        await service.AnalyzeAsync(snapshot, "analyze btc", cancellationToken);

        openRouterClient.Verify(x => x.CompleteChatAsync(prompt, cancellationToken), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public async Task AnalyzeAsync_Throws_When_OpenRouterClient_Returns_Null_Or_Whitespace(string? response)
    {
        var snapshot = PromptTestData.CreateSnapshot();
        var analyticsOutput = PromptTestData.CreateAnalyticsOutput();
        var prompt = new PromptBuildResult([new(PromptRole.User, "payload")]);

        var analyticsOutputComposer = new Mock<IAnalyticsOutputComposer>();
        analyticsOutputComposer
            .Setup(x => x.Compose(snapshot))
            .Returns(analyticsOutput);

        var promptBuilder = new Mock<IPromptBuilder>();
        promptBuilder
            .Setup(x => x.Build(It.IsAny<PromptBuildRequest>()))
            .Returns(prompt);

        var openRouterClient = new Mock<IOpenRouterClient>();
        openRouterClient
            .Setup(x => x.CompleteChatAsync(prompt, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response!);

        var service = new LlmAnalyticsService(
            analyticsOutputComposer.Object,
            promptBuilder.Object,
            openRouterClient.Object);

        var action = () => service.AnalyzeAsync(snapshot, "analyze btc");

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*empty response*");
    }

    [Fact]
    public async Task AnalyzeAsync_Propagates_OperationCanceledException_From_OpenRouterClient()
    {
        var snapshot = PromptTestData.CreateSnapshot();
        var analyticsOutput = PromptTestData.CreateAnalyticsOutput();
        var prompt = new PromptBuildResult([new(PromptRole.User, "payload")]);
        var cancellationToken = new CancellationTokenSource().Token;

        var analyticsOutputComposer = new Mock<IAnalyticsOutputComposer>();
        analyticsOutputComposer
            .Setup(x => x.Compose(snapshot))
            .Returns(analyticsOutput);

        var promptBuilder = new Mock<IPromptBuilder>();
        promptBuilder
            .Setup(x => x.Build(It.IsAny<PromptBuildRequest>()))
            .Returns(prompt);

        var openRouterClient = new Mock<IOpenRouterClient>();
        openRouterClient
            .Setup(x => x.CompleteChatAsync(prompt, cancellationToken))
            .ThrowsAsync(new OperationCanceledException(cancellationToken));

        var service = new LlmAnalyticsService(
            analyticsOutputComposer.Object,
            promptBuilder.Object,
            openRouterClient.Object);

        var action = () => service.AnalyzeAsync(snapshot, "analyze btc", cancellationToken);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    private static LlmAnalyticsService CreateService() =>
        new(
            new Mock<IAnalyticsOutputComposer>().Object,
            new Mock<IPromptBuilder>().Object,
            new Mock<IOpenRouterClient>().Object);
}
