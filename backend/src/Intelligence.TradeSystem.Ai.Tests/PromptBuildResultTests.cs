namespace Intelligence.TradeSystem.Ai.Tests;

public sealed class PromptBuildResultTests
{
    [Fact]
    public void Throws_When_Messages_Is_Null()
    {
        var action = () => new PromptBuildResult(null!);

        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("messages");
    }

    [Fact]
    public void Throws_When_Messages_Is_Empty()
    {
        var action = () => new PromptBuildResult([]);

        action.Should().Throw<ArgumentException>()
            .WithParameterName("messages");
    }

    [Fact]
    public void Throws_When_Messages_Contains_Null_Element()
    {
        PromptMessage[] messages = [new(PromptRole.System, "system"), null!];

        var action = () => new PromptBuildResult(messages);

        action.Should().Throw<ArgumentException>()
            .WithParameterName("messages");
    }

    [Fact]
    public void Preserves_Message_Order()
    {
        PromptMessage[] messages =
        [
            new(PromptRole.System, "system"),
            new(PromptRole.User, "question"),
        ];

        var result = new PromptBuildResult(messages);

        result.Messages.Should().ContainInOrder(messages);
    }
}
