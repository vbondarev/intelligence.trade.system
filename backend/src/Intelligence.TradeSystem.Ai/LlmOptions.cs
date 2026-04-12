namespace Intelligence.TradeSystem.Ai;

/// <summary>
/// Конфигурация LLM provider для AI-слоя.
/// Предназначена для bind из configuration и последующей валидации
/// перед использованием в transport-specific client.
/// </summary>
public sealed class LlmOptions
{
    /// <summary>Имя configuration section для bind опций LLM provider.</summary>
    public const string SectionName = "Llm";

    /// <summary>
    /// Идентификатор текущего provider.
    /// Пока основным сценарием является <c>OpenRouter</c>, однако свойство оставлено строковым
    /// для provider-neutral конфигурации AI-слоя.
    /// </summary>
    public string Provider { get; set; } = "OpenRouter";

    /// <summary>
    /// Базовый URL LLM provider API.
    /// Должен быть абсолютным HTTPS URL.
    /// </summary>
    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";

    /// <summary>
    /// API-ключ LLM provider.
    /// Не должен быть пустым.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Имя модели, используемой для анализа.
    /// Например: <c>openai/gpt-4.1-mini</c> или иная модель, поддерживаемая provider.
    /// </summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// Температура генерации ответа.
    /// Для chat-oriented LLM provider должна находиться в диапазоне <c>[0; 2]</c>.
    /// </summary>
    public decimal Temperature { get; set; } = 0.2m;

    /// <summary>
    /// Верхняя граница числа токенов в ответе модели.
    /// Значение должно быть больше нуля.
    /// </summary>
    public int MaxTokens { get; set; } = 1200;

    /// <summary>
    /// Валидирует текущие настройки и возвращает тот же экземпляр для fluent-использования.
    /// </summary>
    /// <returns>Тот же экземпляр <see cref="LlmOptions"/> после успешной валидации.</returns>
    /// <exception cref="InvalidOperationException">Если конфигурация неполная или некорректная.</exception>
    public LlmOptions Validate()
    {
        EnsureConfigured(nameof(Provider), Provider);
        var baseUrl = EnsureConfigured(nameof(BaseUrl), BaseUrl);
        EnsureConfigured(nameof(ApiKey), ApiKey);
        EnsureConfigured(nameof(Model), Model);

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException($"{nameof(LlmOptions)}.{nameof(BaseUrl)} must be an absolute HTTPS URL.");
        }

        if (Temperature is < 0m or > 2m)
        {
            throw new InvalidOperationException($"{nameof(LlmOptions)}.{nameof(Temperature)} must be in range [0; 2].");
        }

        if (MaxTokens <= 0)
        {
            throw new InvalidOperationException($"{nameof(LlmOptions)}.{nameof(MaxTokens)} must be greater than zero.");
        }

        return this;
    }

    private static string EnsureConfigured(string propertyName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{nameof(LlmOptions)}.{propertyName} must be configured.");
        }

        return value;
    }
}

