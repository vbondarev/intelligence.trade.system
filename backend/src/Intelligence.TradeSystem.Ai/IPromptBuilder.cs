namespace Intelligence.TradeSystem.Ai;

/// <summary>
/// Строит ordered prompt payload для LLM provider на основе готового рыночного снимка
/// и пользовательского запроса.
/// </summary>
public interface IPromptBuilder
{
    /// <summary>
    /// Формирует prompt payload для отправки в chat-oriented LLM provider.
    /// </summary>
    /// <param name="request">Запрос на построение prompt с рыночным снимком и пользовательским вопросом.</param>
    /// <returns>
    /// Нормализованный <see cref="PromptBuildResult"/>, пригодный для последующего маппинга
    /// в transport-specific request body.
    /// </returns>
    /// <exception cref="ArgumentNullException">Если <paramref name="request"/> равен <c>null</c>.</exception>
    PromptBuildResult Build(PromptBuildRequest request);
}

