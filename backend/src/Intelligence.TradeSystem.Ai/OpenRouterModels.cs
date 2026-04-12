using System.Text.Json.Serialization;

namespace Intelligence.TradeSystem.Ai;

internal sealed class OpenRouterChatCompletionRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("messages")]
    public required IReadOnlyList<OpenRouterChatMessageDto> Messages { get; init; }

    [JsonPropertyName("temperature")]
    public required decimal Temperature { get; init; }

    [JsonPropertyName("max_tokens")]
    public required int MaxTokens { get; init; }
}

internal sealed class OpenRouterChatMessageDto
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("content")]
    public required string Content { get; init; }
}

internal sealed class OpenRouterChatCompletionResponse
{
    [JsonPropertyName("choices")]
    public List<OpenRouterChoiceDto> Choices { get; init; } = [];
}

internal sealed class OpenRouterChoiceDto
{
    [JsonPropertyName("message")]
    public OpenRouterChatMessageDto? Message { get; init; }
}

internal sealed class OpenRouterErrorEnvelope
{
    [JsonPropertyName("error")]
    public OpenRouterErrorDto? Error { get; init; }
}

internal sealed class OpenRouterErrorDto
{
    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

