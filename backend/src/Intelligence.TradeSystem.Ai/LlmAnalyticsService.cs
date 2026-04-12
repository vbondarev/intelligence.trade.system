using Intelligence.TradeSystem.Analytics;
using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Ai;

/// <summary>
/// Оркестрирует AI-анализ поверх уже подготовленного <see cref="MarketAnalysisSnapshot"/>:
/// собирает analytics context, строит normalized prompt и делегирует вызов в OpenRouter client.
/// Сам не пересчитывает рыночную аналитику и не содержит transport-specific HTTP-логики.
/// </summary>
public sealed class LlmAnalyticsService : ILlmAnalyticsService
{
    private readonly IAnalyticsOutputComposer _analyticsOutputComposer;
    private readonly IPromptBuilder _promptBuilder;
    private readonly IOpenRouterClient _openRouterClient;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="LlmAnalyticsService"/>.
    /// </summary>
    /// <param name="analyticsOutputComposer">Сервис подготовки согласованного analytics-context.</param>
    /// <param name="promptBuilder">Сервис построения normalized prompt payload.</param>
    /// <param name="openRouterClient">Клиент LLM provider для отправки prompt и получения ответа.</param>
    /// <exception cref="ArgumentNullException">Если любая из зависимостей равна <c>null</c>.</exception>
    public LlmAnalyticsService(
        IAnalyticsOutputComposer analyticsOutputComposer,
        IPromptBuilder promptBuilder,
        IOpenRouterClient openRouterClient)
    {
        _analyticsOutputComposer = analyticsOutputComposer ?? throw new ArgumentNullException(nameof(analyticsOutputComposer));
        _promptBuilder = promptBuilder ?? throw new ArgumentNullException(nameof(promptBuilder));
        _openRouterClient = openRouterClient ?? throw new ArgumentNullException(nameof(openRouterClient));
    }

    /// <inheritdoc />
    public async Task<string> AnalyzeAsync(
        MarketAnalysisSnapshot snapshot,
        string userQuery,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(userQuery);

        var analyticsOutput = _analyticsOutputComposer.Compose(snapshot);
        var prompt = _promptBuilder.Build(new PromptBuildRequest(snapshot, userQuery, analyticsOutput));
        var response = await _openRouterClient.CompleteChatAsync(prompt, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(response))
        {
            throw new InvalidOperationException("LLM provider returned an empty response.");
        }

        return response;
    }
}

