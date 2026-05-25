using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Intelligence.TradeSystem.Ai;

/// <summary>
/// HTTP-клиент OpenRouter API для отправки chat/completions запросов на основе
/// уже подготовленного <see cref="PromptBuildResult"/>.
/// </summary>
public sealed class OpenRouterClient : IOpenRouterClient
{
    private static readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;
    private readonly Uri _chatCompletionsUri;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly decimal _temperature;
    private readonly int _maxTokens;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="OpenRouterClient"/>.
    /// </summary>
    /// <param name="httpClient">Переиспользуемый HTTP-клиент для вызовов OpenRouter API.</param>
    /// <param name="options">Валидированные настройки LLM provider.</param>
    /// <exception cref="ArgumentNullException">Если <paramref name="httpClient"/> или <paramref name="options"/> равны <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">Если настройки невалидны или настроены не на <c>OpenRouter</c>.</exception>
    public OpenRouterClient(HttpClient httpClient, LlmOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        options = (options ?? throw new ArgumentNullException(nameof(options))).Validate();

        if (!string.Equals(options.Provider, "OpenRouter", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{nameof(OpenRouterClient)} supports only the OpenRouter provider.");
        }

        _chatCompletionsUri = new Uri(options.BaseUrl.TrimEnd('/') + "/chat/completions", UriKind.Absolute);
        _apiKey = options.ApiKey;
        _model = options.Model;
        _temperature = options.Temperature;
        _maxTokens = options.MaxTokens;
    }

    /// <inheritdoc />
    public async Task<string> CompleteChatAsync(PromptBuildResult prompt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        using HttpRequestMessage request = new(HttpMethod.Post, _chatCompletionsUri)
        {
            Content = JsonContent.Create(
                CreateRequestBody(prompt),
                options: _jsonSerializerOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorMessage = await TryReadErrorMessageAsync(response, cancellationToken).ConfigureAwait(false);
            var message = $"OpenRouter chat completion request failed with status code {(int)response.StatusCode} ({response.StatusCode})";

            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                message += $": {errorMessage}";
            }

            throw new HttpRequestException(message, null, response.StatusCode);
        }

        OpenRouterChatCompletionResponse? completionResponse;
        try
        {
            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            completionResponse = await JsonSerializer.DeserializeAsync<OpenRouterChatCompletionResponse>(
                responseStream,
                _jsonSerializerOptions,
                cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("OpenRouter returned malformed JSON.", exception);
        }

        var content = completionResponse?.Choices.FirstOrDefault()?.Message?.Content;

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("OpenRouter response does not contain a non-empty assistant message.");
        }

        return content;
    }

    private OpenRouterChatCompletionRequest CreateRequestBody(PromptBuildResult prompt) =>
        new()
        {
            Model = _model,
            Temperature = _temperature,
            MaxTokens = _maxTokens,
            Messages = prompt.Messages.Select(MapMessage).ToArray(),
        };

    private static OpenRouterChatMessageDto MapMessage(PromptMessage message) =>
        new()
        {
            Role = message.Role switch
            {
                PromptRole.System => "system",
                PromptRole.User => "user",
                PromptRole.Assistant => "assistant",
                _ => throw new InvalidOperationException($"Unsupported prompt role: {message.Role}."),
            },
            Content = message.Content,
        };

    private static async Task<string?> TryReadErrorMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var rawContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(rawContent))
        {
            return null;
        }

        try
        {
            var errorEnvelope = JsonSerializer.Deserialize<OpenRouterErrorEnvelope>(rawContent, _jsonSerializerOptions);
            return string.IsNullOrWhiteSpace(errorEnvelope?.Error?.Message)
                ? rawContent
                : errorEnvelope.Error.Message;
        }
        catch (JsonException)
        {
            return rawContent;
        }
    }
}
