using Intelligence.TradeSystem.Analysis;
using Intelligence.TradeSystem.Api.Models.Payloads;

namespace Intelligence.TradeSystem.Api.Mappers;

/// <summary>
/// Обогащает список тегов снапшота данными, доступными только в API-слое:
/// health.IsFresh, warnings секций и EntryQuality из <see cref="LlmTimeframeSummaryBuilder"/>.
///
/// Теги добавляются поверх уже вычисленных тегов (от <c>MarketTagsBuilder</c>),
/// получая наивысший приоритет (prepended перед base-тегами).
/// Итоговый список дедуплицируется и ограничивается <see cref="MarketTagConstants.MaxTags"/>.
/// </summary>
internal static class LlmTagEnricher
{
    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Обогащает базовые теги снапшота данными из health и summary таймфреймов.
    /// </summary>
    public static List<string> Enrich(
        IReadOnlyList<string> baseTags,
        LlmSnapshotHealthPayload health,
        LlmTimeframeSummaryResult? m15Summary,
        LlmTimeframeSummaryResult? h1Summary,
        LlmTimeframeSummaryResult? h4Summary,
        LlmTimeframeSummaryResult? d1Summary,
        AnalysisMode mode)
    {
        ArgumentNullException.ThrowIfNull(baseTags);
        ArgumentNullException.ThrowIfNull(health);

        // Теги высокого приоритета — идут перед base-тегами
        var priority = new List<string>(4);

        if (!health.IsFresh)
            priority.Add(MarketTagConstants.StaleSnapshot);

        if (HasStaleWarning(health.Warnings, "orderBook"))
            priority.Add(MarketTagConstants.StaleOrderBook);

        if (HasStaleWarning(health.Warnings, "tradeFlow"))
            priority.Add(MarketTagConstants.StaleTradeFlow);

        // Объединяем: priority → base → entry-quality теги
        var all = new List<string>(priority.Count + baseTags.Count + 4);
        all.AddRange(priority);
        all.AddRange(baseTags);

        // Теги на основе EntryQuality и RiskFlags первичных ТФ
        var primarySummaries = GetPrimaryTfSummaries(m15Summary, h1Summary, h4Summary, d1Summary, mode);
        if (primarySummaries.Count > 0)
            AddEntryQualityTags(primarySummaries, all);

        return Deduplicate(all, MarketTagConstants.MaxTags);
    }

    // ─── Internal helpers (testable) ─────────────────────────────────────────

    /// <summary>
    /// Проверяет, содержит ли список предупреждений stale-сообщение для указанной секции.
    /// Формат health warning: <c>"{sectionName} is stale (age: ...ms, max: ...ms)"</c>.
    /// </summary>
    internal static bool HasStaleWarning(IReadOnlyList<string> warnings, string sectionName) =>
        warnings.Any(w => w.StartsWith($"{sectionName} is stale", StringComparison.OrdinalIgnoreCase));

    // ─── Private helpers ──────────────────────────────────────────────────────

    private static void AddEntryQualityTags(List<LlmTimeframeSummaryResult> primarySummaries, List<string> all)
    {
        var allPoor = primarySummaries.All(s => s.EntryQuality == EntryQuality.Poor);
        var anyGood = primarySummaries.Any(s => s.EntryQuality == EntryQuality.Good);

        if (allPoor)
            TryAdd(all, MarketTagConstants.NoCleanEntry);
        else if (anyGood)
            TryAdd(all, MarketTagConstants.ActionableEntry);

        // weak-entry-confirmation: есть directional сигнал и хотя бы один ТФ с EntryQuality != Good
        var anyDirectional =
            all.Contains(MarketTagConstants.AggressiveBuying, StringComparer.Ordinal) ||
            all.Contains(MarketTagConstants.AggressiveSelling, StringComparer.Ordinal) ||
            all.Contains(MarketTagConstants.BidPressure, StringComparer.Ordinal) ||
            all.Contains(MarketTagConstants.AskPressure, StringComparer.Ordinal);

        if (anyDirectional && primarySummaries.Any(s => s.EntryQuality != EntryQuality.Good))
            TryAdd(all, MarketTagConstants.WeakEntryConfirmation);

        // trend-confirmed-entry-filtered: хотя бы один primary ТФ имеет этот riskFlag
        if (primarySummaries.Any(s => s.RiskFlags.Contains("TrendConfirmedButEntryFiltered", StringComparer.Ordinal)))
            TryAdd(all, MarketTagConstants.TrendConfirmedEntryFiltered);
    }

    private static List<LlmTimeframeSummaryResult> GetPrimaryTfSummaries(
        LlmTimeframeSummaryResult? m15,
        LlmTimeframeSummaryResult? h1,
        LlmTimeframeSummaryResult? h4,
        LlmTimeframeSummaryResult? d1,
        AnalysisMode mode)
    {
        var primaryLabels = AnalysisModeDefaults.GetPrimaryTimeframes(mode);
        var result = new List<LlmTimeframeSummaryResult>(3);

        if (primaryLabels.Contains("15m", StringComparer.Ordinal) && m15 is not null) result.Add(m15);
        if (primaryLabels.Contains("1h", StringComparer.Ordinal) && h1 is not null) result.Add(h1);
        if (primaryLabels.Contains("4h", StringComparer.Ordinal) && h4 is not null) result.Add(h4);
        if (primaryLabels.Contains("1d", StringComparer.Ordinal) && d1 is not null) result.Add(d1);

        return result;
    }

    private static void TryAdd(List<string> list, string tag)
    {
        if (!list.Contains(tag, StringComparer.Ordinal))
            list.Add(tag);
    }

    private static List<string> Deduplicate(List<string> all, int maxCount)
    {
        var result = new List<string>(Math.Min(all.Count, maxCount));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tag in all)
        {
            if (result.Count >= maxCount) break;
            if (seen.Add(tag)) result.Add(tag);
        }
        return result;
    }
}
