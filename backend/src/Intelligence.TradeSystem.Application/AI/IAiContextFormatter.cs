namespace Intelligence.TradeSystem.Application.AI;

/// <summary>
/// Формирует компактный текстовый аналитический контекст на основе готового рыночного снимка
/// и снимка портфеля.
/// </summary>
public interface IAiContextFormatter
{
    /// <summary>
    /// Преобразует контекст (рыночный снимок + снимок портфеля) в компактное
    /// детерминированное текстовое представление.
    /// </summary>
    string Format(AiAnalysisContext context);
}
