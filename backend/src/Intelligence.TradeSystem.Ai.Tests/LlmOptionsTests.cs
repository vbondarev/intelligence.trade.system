namespace Intelligence.TradeSystem.Ai.Tests;

public sealed class LlmOptionsTests
{
    [Fact]
    public void Exposes_Stable_Defaults_For_OpenRouter_Based_Setup()
    {
        var options = new LlmOptions();

        options.Provider.Should().Be("OpenRouter");
        options.BaseUrl.Should().Be("https://openrouter.ai/api/v1");
        options.ApiKey.Should().BeEmpty();
        options.Model.Should().BeEmpty();
        options.Temperature.Should().Be(0.2m);
        options.MaxTokens.Should().Be(1200);
    }

    [Fact]
    public void Validate_Returns_Same_Instance_For_Valid_Options()
    {
        var options = CreateValidOptions();

        var validated = options.Validate();

        validated.Should().BeSameAs(options);
    }

    [Fact]
    public void Exposes_Stable_SectionName()
    {
        LlmOptions.SectionName.Should().Be("Llm");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Validate_Throws_When_Provider_Is_Null_Or_Whitespace(string? provider)
    {
        var options = CreateValidOptions();
        options.Provider = provider!;

        var action = options.Validate;

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*LlmOptions.Provider*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Validate_Throws_When_BaseUrl_Is_Null_Or_Whitespace(string? baseUrl)
    {
        var options = CreateValidOptions();
        options.BaseUrl = baseUrl!;

        var action = options.Validate;

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*LlmOptions.BaseUrl*");
    }

    [Theory]
    [InlineData("/api/v1")]
    [InlineData("http://openrouter.ai/api/v1")]
    [InlineData("ftp://openrouter.ai/api/v1")]
    [InlineData("not-a-url")]
    public void Validate_Throws_When_BaseUrl_Is_Not_Absolute_Https(string baseUrl)
    {
        var options = CreateValidOptions();
        options.BaseUrl = baseUrl;

        var action = options.Validate;

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*absolute HTTPS URL*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Validate_Throws_When_ApiKey_Is_Null_Or_Whitespace(string? apiKey)
    {
        var options = CreateValidOptions();
        options.ApiKey = apiKey!;

        var action = options.Validate;

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*LlmOptions.ApiKey*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Validate_Throws_When_Model_Is_Null_Or_Whitespace(string? model)
    {
        var options = CreateValidOptions();
        options.Model = model!;

        var action = options.Validate;

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*LlmOptions.Model*");
    }

    [Theory]
    [InlineData(-0.0001)]
    [InlineData(2.0001)]
    public void Validate_Throws_When_Temperature_Is_Out_Of_Range(decimal temperature)
    {
        var options = CreateValidOptions();
        options.Temperature = temperature;

        var action = options.Validate;

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*LlmOptions.Temperature*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_Throws_When_MaxTokens_Is_Not_Positive(int maxTokens)
    {
        var options = CreateValidOptions();
        options.MaxTokens = maxTokens;

        var action = options.Validate;

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*LlmOptions.MaxTokens*");
    }

    private static LlmOptions CreateValidOptions() =>
        new()
        {
            Provider = "OpenRouter",
            BaseUrl = "https://openrouter.ai/api/v1",
            ApiKey = "test-api-key",
            Model = "openai/gpt-4.1-mini",
            Temperature = 0.2m,
            MaxTokens = 1200,
        };
}

