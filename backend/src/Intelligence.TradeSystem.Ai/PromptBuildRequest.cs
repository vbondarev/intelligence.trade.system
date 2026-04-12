using Intelligence.TradeSystem.Analytics;
using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Ai;

/// <summary>
/// Входные данные для построения prompt payload на основе уже собранного рыночного снимка.
/// </summary>
public sealed record PromptBuildRequest
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="PromptBuildRequest"/>.
    /// </summary>
    /// <param name="snapshot">Полностью собранный <see cref="MarketAnalysisSnapshot"/>.</param>
    /// <param name="userQuery">Непустой пользовательский запрос к аналитике.</param>
    /// <param name="analyticsOutput">
    /// Дополнительный analytics-layer context. Может отсутствовать, если вызывающая сторона
    /// решает строить prompt только на основе <paramref name="snapshot"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">Если <paramref name="snapshot"/> равен <c>null</c>.</exception>
    /// <exception cref="ArgumentException">Если <paramref name="userQuery"/> пустой или состоит только из пробелов.</exception>
    public PromptBuildRequest(
        MarketAnalysisSnapshot snapshot,
        string userQuery,
        AnalyticsOutput? analyticsOutput = null)
    {
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        ArgumentException.ThrowIfNullOrWhiteSpace(userQuery);

        UserQuery = userQuery;
        AnalyticsOutput = analyticsOutput;
    }

    /// <summary>Полностью собранный рыночный снимок, являющийся основным payload для LLM.</summary>
    public MarketAnalysisSnapshot Snapshot { get; }

    /// <summary>Пользовательский запрос к аналитике.</summary>
    public string UserQuery { get; }

    /// <summary>
    /// Дополнительный компактный analytics-context, подготовленный слоем <c>Analytics</c>.
    /// </summary>
    public AnalyticsOutput? AnalyticsOutput { get; }
}

