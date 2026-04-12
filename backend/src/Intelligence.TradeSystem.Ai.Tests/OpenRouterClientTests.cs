using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Intelligence.TradeSystem.Ai.Tests;

public sealed class OpenRouterClientTests
{
    [Fact]
    public void Constructor_Throws_When_HttpClient_Is_Null()
    {
        var action = () => new OpenRouterClient(null!, CreateValidOptions());

        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("httpClient");
    }

    [Fact]
    public void Constructor_Throws_When_Options_Is_Null()
    {
        var action = () => new OpenRouterClient(new HttpClient(), null!);

        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("options");
    }

    [Fact]
    public void Constructor_Throws_When_Provider_Is_Not_OpenRouter()
    {
        var options = CreateValidOptions();
        options.Provider = "AnotherProvider";

        var action = () => new OpenRouterClient(new HttpClient(), options);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*supports only the OpenRouter provider*");
    }

    [Fact]
    public async Task CompleteChatAsync_Throws_When_Prompt_Is_Null()
    {
        var client = CreateClient(_ => CreateJsonResponse(HttpStatusCode.OK, """
            {
              "choices": [
                {
                  "message": {
                    "role": "assistant",
                    "content": "ok"
                  }
                }
              ]
            }
            """));

        var action = () => client.CompleteChatAsync(null!);

        await action.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("prompt");
    }

    [Fact]
    public async Task CompleteChatAsync_Sends_Expected_Request_And_Returns_Assistant_Message()
    {
        HttpMethod? capturedMethod = null;
        Uri? capturedUri = null;
        AuthenticationHeaderValue? capturedAuthorization = null;
        string? capturedBody = null;

        var client = CreateClient(async request =>
        {
            capturedMethod = request.Method;
            capturedUri = request.RequestUri;
            capturedAuthorization = request.Headers.Authorization;
            capturedBody = await request.Content!.ReadAsStringAsync().ConfigureAwait(false);

            return await CreateJsonResponse(HttpStatusCode.OK, """
                {
                  "choices": [
                    {
                      "message": {
                        "role": "assistant",
                        "content": "market summary"
                      }
                    }
                  ]
                }
                """);
        });

        var prompt = new PromptBuildResult(
        [
            new(PromptRole.System, "system guidance"),
            new(PromptRole.User, "user question"),
            new(PromptRole.Assistant, "assistant context"),
        ]);

        var result = await client.CompleteChatAsync(prompt);

        result.Should().Be("market summary");
        capturedMethod.Should().Be(HttpMethod.Post);
        capturedUri.Should().Be(new Uri("https://openrouter.ai/api/v1/chat/completions"));
        capturedAuthorization.Should().NotBeNull();
        capturedAuthorization!.Scheme.Should().Be("Bearer");
        capturedAuthorization.Parameter.Should().Be("test-api-key");
        capturedBody.Should().NotBeNull();

        using var bodyDocument = JsonDocument.Parse(capturedBody!);
        var root = bodyDocument.RootElement;
        root.GetProperty("model").GetString().Should().Be("openai/gpt-4.1-mini");
        root.GetProperty("temperature").GetDecimal().Should().Be(0.2m);
        root.GetProperty("max_tokens").GetInt32().Should().Be(1200);

        var messages = root.GetProperty("messages").EnumerateArray().ToArray();
        messages.Should().HaveCount(3);
        messages[0].GetProperty("role").GetString().Should().Be("system");
        messages[0].GetProperty("content").GetString().Should().Be("system guidance");
        messages[1].GetProperty("role").GetString().Should().Be("user");
        messages[1].GetProperty("content").GetString().Should().Be("user question");
        messages[2].GetProperty("role").GetString().Should().Be("assistant");
        messages[2].GetProperty("content").GetString().Should().Be("assistant context");
    }

    [Fact]
    public async Task CompleteChatAsync_Throws_HttpRequestException_When_Status_Code_Is_Not_Success()
    {
        var client = CreateClient(_ => CreateJsonResponse(HttpStatusCode.Unauthorized, """
            {
              "error": {
                "message": "Invalid API key"
              }
            }
            """));

        var action = () => client.CompleteChatAsync(CreatePrompt());

        await action.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*401*Invalid API key*");
    }

    [Fact]
    public async Task CompleteChatAsync_Uses_Raw_Error_Body_When_Error_Payload_Is_Not_Json()
    {
        var client = CreateClient(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("provider exploded", Encoding.UTF8, "text/plain"),
        }));

        var action = () => client.CompleteChatAsync(CreatePrompt());

        await action.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*provider exploded*");
    }

    [Fact]
    public async Task CompleteChatAsync_Throws_When_Response_Json_Is_Malformed()
    {
        var client = CreateClient(_ => CreateJsonResponse(HttpStatusCode.OK, "{ not-json }"));

        var action = () => client.CompleteChatAsync(CreatePrompt());

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*malformed JSON*");
    }

    [Fact]
    public async Task CompleteChatAsync_Throws_When_Response_Has_No_Choices()
    {
        var client = CreateClient(_ => CreateJsonResponse(HttpStatusCode.OK, """
            {
              "choices": []
            }
            """));

        var action = () => client.CompleteChatAsync(CreatePrompt());

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*non-empty assistant message*");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public async Task CompleteChatAsync_Throws_When_First_Choice_Content_Is_Whitespace(string content)
    {
        var escapedContent = JsonSerializer.Serialize(content);
        var client = CreateClient(_ => CreateJsonResponse(HttpStatusCode.OK, $$"""
            {
              "choices": [
                {
                  "message": {
                    "role": "assistant",
                    "content": {{escapedContent}}
                  }
                }
              ]
            }
            """));

        var action = () => client.CompleteChatAsync(CreatePrompt());

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*non-empty assistant message*");
    }

    [Fact]
    public async Task CompleteChatAsync_Throws_When_Prompt_Contains_Unsupported_Role()
    {
        var client = CreateClient(_ => CreateJsonResponse(HttpStatusCode.OK, """
            {
              "choices": [
                {
                  "message": {
                    "role": "assistant",
                    "content": "ok"
                  }
                }
              ]
            }
            """));
        var prompt = new PromptBuildResult([new((PromptRole)999, "unexpected")]);

        var action = () => client.CompleteChatAsync(prompt);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Unsupported prompt role*");
    }

    [Fact]
    public async Task CompleteChatAsync_Propagates_OperationCanceledException()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();
        var cancellationToken = cancellationTokenSource.Token;
        var client = CreateClient((_, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return CreateJsonResponse(HttpStatusCode.OK, """
                {
                  "choices": [
                    {
                      "message": {
                        "role": "assistant",
                        "content": "ok"
                      }
                    }
                  ]
                }
                """);
        });

        var action = () => client.CompleteChatAsync(CreatePrompt(), cancellationToken);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    private static OpenRouterClient CreateClient(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler, LlmOptions? options = null) =>
        CreateClient((request, _) => handler(request), options);

    private static OpenRouterClient CreateClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler, LlmOptions? options = null)
    {
        var httpClient = new HttpClient(new TestHttpMessageHandler(handler));
        return new OpenRouterClient(httpClient, options ?? CreateValidOptions());
    }

    private static PromptBuildResult CreatePrompt() =>
        new([
            new(PromptRole.System, "system guidance"),
            new(PromptRole.User, "what is the outlook?"),
        ]);

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

    private static Task<HttpResponseMessage> CreateJsonResponse(HttpStatusCode statusCode, string json) =>
        Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });
}


