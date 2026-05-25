namespace Intelligence.TradeSystem.Ai;

/// <summary>
/// Тонкий контракт клиента OpenRouter поверх normalized chat prompt payload.
/// Concrete HTTP-интеграция предоставляется классом <see cref="OpenRouterClient"/>.
/// </summary>
public interface IOpenRouterClient
{
    /// <summary>
    /// Отправляет подготовленный chat prompt в OpenRouter и возвращает текстовый ответ модели.
    /// </summary>
    /// <param name="prompt">Нормализованный prompt payload для chat-oriented LLM provider.</param>
    /// <param name="cancellationToken">Токен отмены асинхронной операции.</param>
    /// <returns>Текстовое содержимое ответа модели.</returns>
    /// <exception cref="ArgumentNullException">Если <paramref name="prompt"/> равен <c>null</c>.</exception>
    /// <exception cref="OperationCanceledException">Если операция была отменена через <paramref name="cancellationToken"/>.</exception>
    Task<string> CompleteChatAsync(PromptBuildResult prompt, CancellationToken cancellationToken = default);
}
