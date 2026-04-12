using System.Text.Json;

namespace Intelligence.TradeSystem.Ai;

/// <summary>
/// Строит детерминированный chat-oriented prompt поверх уже подготовленного
/// <see cref="PromptBuildRequest"/>.
/// Использует <c>MarketAnalysisSnapshot</c> как основной JSON payload,
/// а compact analytics context — только как вспомогательное summary-представление.
/// </summary>
public sealed class PromptBuilder : IPromptBuilder
{
    private const string LineBreak = "\n";

    private static readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        WriteIndented = true,
    };

    private static readonly string _systemInstruction = string.Join(LineBreak,
    [
        "Ты — AI-аналитик криптовалютного рынка.",
        "Используй только данные, переданные в prompt.",
        "Основной источник фактов — structured JSON объекта MarketAnalysisSnapshot.",
        "Analytics context используй только как вспомогательное краткое summary.",
        "Не придумывай отсутствующие данные, уровни, сигналы или позиции.",
        "Если данных недостаточно для уверенного вывода, прямо скажи об этом.",
        "Отвечай по существу на пользовательский запрос и явно отделяй наблюдения, риски и вывод."
    ]);

    /// <inheritdoc />
    public PromptBuildResult Build(PromptBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new PromptBuildResult(
        [
            new(PromptRole.System, _systemInstruction),
            new(PromptRole.User, BuildUserMessageContent(request)),
        ]);
    }

    private static string BuildUserMessageContent(PromptBuildRequest request)
    {
        var userQuery = NormalizeLineEndings(request.UserQuery);
        List<string> sections =
        [
            "user_query:",
            userQuery,
        ];

        if (request.AnalyticsOutput is { } analyticsOutput)
        {
            sections.Add(string.Empty);
            sections.Add("analytics_output_market_regime:");
            sections.Add(NormalizeLineEndings(analyticsOutput.MarketRegime));
            sections.Add(string.Empty);
            sections.Add("analytics_output_formatted_context:");
            sections.Add(NormalizeLineEndings(analyticsOutput.FormattedContext));
        }

        sections.Add(string.Empty);
        sections.Add("market_analysis_snapshot_json:");
        sections.Add("```json");
        sections.Add(NormalizeLineEndings(JsonSerializer.Serialize(request.Snapshot, _jsonSerializerOptions)));
        sections.Add("```");

        return string.Join(LineBreak, sections);
    }

    private static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", LineBreak, StringComparison.Ordinal)
            .Replace("\r", LineBreak, StringComparison.Ordinal);
}


