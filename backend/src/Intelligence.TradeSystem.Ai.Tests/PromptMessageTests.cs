namespace Intelligence.TradeSystem.Ai.Tests;

public sealed class PromptMessageTests
{
    [Fact]
    public void Throws_When_Role_Is_Not_Defined()
    {
        const string parameterName = "role";
        var action = () => new PromptMessage((PromptRole)999, "summary");

        action.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName(parameterName);
    }

    [Fact]
    public void Throws_When_Content_Is_Null()
    {
        const string parameterName = "content";
        var action = () => new PromptMessage(PromptRole.User, null!);

        action.Should().Throw<ArgumentException>()
            .WithParameterName(parameterName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Throws_When_Content_Is_Whitespace(string content)
    {
        const string parameterName = "content";
        var action = () => new PromptMessage(PromptRole.System, content);

        action.Should().Throw<ArgumentException>()
            .WithParameterName(parameterName);
    }

    [Fact]
    public void Stores_Role_And_Content()
    {
        var message = new PromptMessage(PromptRole.Assistant, "summary");

        message.Role.Should().Be(PromptRole.Assistant);
        message.Content.Should().Be("summary");
    }
}


