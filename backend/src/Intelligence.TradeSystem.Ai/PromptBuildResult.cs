namespace Intelligence.TradeSystem.Ai;

/// <summary>
/// Результат построения ordered prompt payload для chat-oriented LLM provider.
/// </summary>
public sealed record PromptBuildResult
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="PromptBuildResult"/>.
    /// </summary>
    /// <param name="messages">Набор сообщений prompt payload в том порядке, в котором они должны быть отправлены в LLM provider.</param>
    /// <exception cref="ArgumentNullException">Если <paramref name="messages"/> равен <c>null</c>.</exception>
    /// <exception cref="ArgumentException">
    /// Если <paramref name="messages"/> пустой или содержит <c>null</c>-элементы.
    /// </exception>
    public PromptBuildResult(IReadOnlyList<PromptMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        if (messages.Count == 0)
        {
            throw new ArgumentException("Prompt must contain at least one message.", nameof(messages));
        }

        if (messages.Any(static message => message is null))
        {
            throw new ArgumentException("Prompt messages cannot contain null elements.", nameof(messages));
        }

        Messages = messages;
    }

    /// <summary>
    /// Ordered messages, готовые к маппингу в transport-specific request body.
    /// </summary>
    public IReadOnlyList<PromptMessage> Messages { get; }
}

