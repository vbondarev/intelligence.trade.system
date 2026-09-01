using Intelligence.TradeSystem.Api.Configuration;
using Intelligence.TradeSystem.Api.Models.Payloads;
using Microsoft.Extensions.Options;

namespace Intelligence.TradeSystem.Api.Services;

/// <summary>
/// Реализация <see cref="ISnapshotHealthEvaluator"/> на основе конфигурируемых порогов свежести.
/// </summary>
internal sealed class SnapshotHealthEvaluator : ISnapshotHealthEvaluator
{
    private readonly SnapshotFreshnessOptions _options;

    public SnapshotHealthEvaluator(IOptions<SnapshotFreshnessOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    /// <inheritdoc/>
    public LlmSnapshotHealthPayload Evaluate(
        MarketSnapshot snapshot,
        AnalysisMode mode)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var thresholds = _options.ForMode(mode);
        var reference = snapshot.CapturedAtUtc;

        var warnings = new List<string>();
        var sectionAges = new Dictionary<string, long>();
        var isFresh = true;

        var requiredSections = GetRequiredSections(mode);

        // price — используем CapturedAtUtc снапшота как fallback timestamp
        AddSection("price", reference, reference, thresholds.PriceMaxAge, requiredSections, sectionAges, warnings, ref isFresh);

        // derivatives
        AddSection("derivatives", reference, reference, thresholds.DerivativesMaxAge, requiredSections, sectionAges, warnings, ref isFresh);

        // orderBook — имеет собственный CapturedAtUtc
        AddSection("orderBook", snapshot.OrderBook.CapturedAtUtc, reference, thresholds.OrderBookMaxAge, requiredSections, sectionAges, warnings, ref isFresh);

        // tradeFlow — используем WindowEndUtc как реальный timestamp данных секции
        AddSection("tradeFlow", snapshot.TradeFlow.WindowEndUtc, reference, thresholds.TradeFlowMaxAge, requiredSections, sectionAges, warnings, ref isFresh);

        // timeframe секции
        AddSection("m15", reference, reference, thresholds.M15MaxAge, requiredSections, sectionAges, warnings, ref isFresh);
        AddSection("h1", reference, reference, thresholds.H1MaxAge, requiredSections, sectionAges, warnings, ref isFresh);
        AddSection("h4", reference, reference, thresholds.H4MaxAge, requiredSections, sectionAges, warnings, ref isFresh);
        AddSection("d1", reference, reference, thresholds.D1MaxAge, requiredSections, sectionAges, warnings, ref isFresh);

        // Spread validation warnings (добавляются маппером — но health-evaluator может добавить заранее)
        CheckOrderBookSpread(snapshot.OrderBook, warnings);

        // Мягкие предупреждения интерпретации — не влияют на isFresh/isPartial
        var softWarnings = SnapshotHealthWarningsBuilder.Build(snapshot, new SnapshotHealthWarningsContext
        {
            Mode = mode,
            SectionAgesMs = sectionAges,
            Thresholds = thresholds,
            StalenessProximityFactor = _options.StalenessProximityFactor,
        });
        warnings.AddRange(softWarnings);

        return new LlmSnapshotHealthPayload
        {
            IsFresh = isFresh,
            IsPartial = false,  // Этап 1: все секции required в domain-модели
            Warnings = warnings,
            MissingSections = [],     // Этап 1: partial snapshot не поддерживается
            SectionAgesMs = sectionAges,
        };
    }

    private static void AddSection(
        string name,
        DateTimeOffset sectionTimestamp,
        DateTimeOffset referenceTimestamp,
        TimeSpan maxAge,
        HashSet<string> requiredSections,
        Dictionary<string, long> sectionAges,
        List<string> warnings,
        ref bool isFresh)
    {
        var age = referenceTimestamp - sectionTimestamp;
        var ageMs = (long)Math.Max(0, age.TotalMilliseconds);
        sectionAges[name] = ageMs;

        if (age > maxAge && requiredSections.Contains(name))
        {
            isFresh = false;
            warnings.Add($"{name} is stale (age: {ageMs}ms, max: {(long)maxAge.TotalMilliseconds}ms)");
        }
    }

    private static void CheckOrderBookSpread(OrderBookSnapshot orderBook, List<string> warnings)
    {
        if (orderBook.BestAskPrice <= 0 || orderBook.BestBidPrice <= 0)
        {
            warnings.Add("orderBook best bid/ask invalid for spread calculation");
        }
        else if (orderBook.BestAskPrice < orderBook.BestBidPrice)
        {
            warnings.Add("orderBook best ask is lower than best bid");
        }
    }

    private static HashSet<string> GetRequiredSections(AnalysisMode mode) => mode switch
    {
        AnalysisMode.Intraday => new HashSet<string>(StringComparer.Ordinal) { "price", "orderBook", "tradeFlow", "derivatives", "m15", "h1", "h4" },
        AnalysisMode.Swing => new HashSet<string>(StringComparer.Ordinal) { "price", "derivatives", "h1", "h4", "d1" },
        AnalysisMode.Portfolio => new HashSet<string>(StringComparer.Ordinal) { "price", "derivatives", "h4", "d1" },
        _ => new HashSet<string>(StringComparer.Ordinal) { "price", "orderBook", "tradeFlow", "derivatives", "m15", "h1", "h4" },
    };
}
