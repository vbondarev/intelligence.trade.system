using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Ai;

/// <summary>
/// Provider-neutral контракт AI-сервиса, который получает уже собранный
/// <see cref="MarketAnalysisSnapshot"/> и пользовательский запрос,
/// а затем возвращает итоговый текстовый ответ LLM.
/// </summary>
public interface ILlmAnalyticsService
{
    /// <summary>
    /// Выполняет AI-анализ на основе готового рыночного снимка и пользовательского вопроса.
    /// </summary>
    /// <param name="snapshot">Полностью собранный <see cref="MarketAnalysisSnapshot"/>.</param>
    /// <param name="userQuery">Непустой пользовательский запрос к аналитике.</param>
    /// <param name="cancellationToken">Токен отмены асинхронной операции.</param>
    /// <returns>
    /// Текстовый ответ LLM, пригодный для возврата в API/UI слой.
    /// Реализация должна возвращать осмысленную непустую строку.
    /// </returns>
    /// <exception cref="ArgumentNullException">Если <paramref name="snapshot"/> равен <c>null</c>.</exception>
    /// <exception cref="ArgumentException">Если <paramref name="userQuery"/> пустой или состоит только из пробелов.</exception>
    /// <exception cref="OperationCanceledException">Если операция была отменена через <paramref name="cancellationToken"/>.</exception>
    Task<string> AnalyzeAsync(
        MarketAnalysisSnapshot snapshot,
        string userQuery,
        CancellationToken cancellationToken = default);
}
