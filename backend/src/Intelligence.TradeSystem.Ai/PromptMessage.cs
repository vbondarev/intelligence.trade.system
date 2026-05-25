namespace Intelligence.TradeSystem.Ai;

/// <summary>
/// Одно сообщение в ordered prompt payload, подготавливаемом для chat-oriented LLM provider.
/// </summary>
public sealed record PromptMessage
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="PromptMessage"/>.
    /// </summary>
    /// <param name="role">Роль сообщения в prompt.</param>
    /// <param name="content">Непустое текстовое содержимое сообщения.</param>
    /// <exception cref="ArgumentException">Если <paramref name="content"/> пустой или состоит только из пробелов.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Если <paramref name="role"/> не соответствует допустимому значению <see cref="PromptRole"/>.</exception>
    public PromptMessage(PromptRole role, string content)
    {
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role), role, "Prompt role must be a defined PromptRole value.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        Role = role;
        Content = content;
    }

    /// <summary>Роль сообщения в prompt payload.</summary>
    public PromptRole Role { get; }

    /// <summary>Текстовое содержимое сообщения.</summary>
    public string Content { get; }
}
