namespace Intelligence.TradeSystem.Ai;

/// <summary>
/// Роль сообщения в chat-oriented prompt payload для LLM provider.
/// </summary>
public enum PromptRole
{
    /// <summary>Системная инструкция, задающая правила интерпретации данных и стиль ответа.</summary>
    System = 1,

    /// <summary>Пользовательский запрос с прикладной задачей к анализу.</summary>
    User = 2,

    /// <summary>Сообщение ассистента, если в будущем понадобится few-shot или continuation-контекст.</summary>
    Assistant = 3
}
