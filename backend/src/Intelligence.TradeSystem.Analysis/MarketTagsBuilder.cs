using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Analysis;

/// <summary>
/// Централизованный builder тегов снапшота V1.
///
/// Whitelist тегов V1:
///   Режим:      trending · neutral
///   Funding:    positive-funding · negative-funding
///   Давление:   bid-pressure · ask-pressure
///   Агрессия:   aggressive-buying · aggressive-selling
///
/// Правила разрешения конфликтов (взаимоисключающие группы):
///   trending ⊕ neutral          — определяется sentiment.MarketRegime
///   positive ⊕ negative-funding — определяется знаком fundingRate
///   bid ⊕ ask-pressure          — определяется знаком ImbalanceTop5
///   aggressive-buying ⊕ aggressive-selling — buying имеет приоритет
///
/// Порядок тегов в выводе детерминирован: regime → funding → pressure → aggression.
/// Максимум <see cref="MaxTags"/> тегов на снапшот.
/// </summary>
internal static class MarketTagsBuilder
{
    // ─── V1 whitelist — строковые константы тегов ─────────────────────────────

    /// <summary>Рыночный режим активного тренда.</summary>
    public const string TagTrending = "trending";

    /// <summary>Нейтральный рыночный режим.</summary>
    public const string TagNeutral = "neutral";

    /// <summary>Ставка финансирования положительная (лонги переплачивают).</summary>
    public const string TagPositiveFunding = "positive-funding";

    /// <summary>Ставка финансирования отрицательная (шорты переплачивают).</summary>
    public const string TagNegativeFunding = "negative-funding";

    /// <summary>Стакан заявок с доминированием bid-стороны.</summary>
    public const string TagBidPressure = "bid-pressure";

    /// <summary>Стакан заявок с доминированием ask-стороны.</summary>
    public const string TagAskPressure = "ask-pressure";

    /// <summary>Агрессивное давление покупателей в потоке сделок.</summary>
    public const string TagAggressiveBuying = "aggressive-buying";

    /// <summary>Агрессивное давление продавцов в потоке сделок.</summary>
    public const string TagAggressiveSelling = "aggressive-selling";

    // ─── Параметры ───────────────────────────────────────────────────────────

    /// <summary>Максимальное количество тегов в одном снапшоте.</summary>
    public const int MaxTags = 4;

    /// <summary>
    /// Порог абсолютного дисбаланса стакана на глубине Top-5,
    /// при превышении которого выставляется тег давления.
    /// </summary>
    internal const decimal OrderBookPressureThreshold = 0.3m;

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Строит детерминированный список тегов из указанных снапшотов.
    /// Результат содержит не более <see cref="MaxTags"/> тегов в стабильном порядке:
    /// regime → funding → pressure → aggression.
    /// </summary>
    public static List<string> Build(
        DerivativesSnapshot derivatives,
        OrderBookSnapshot orderBook,
        TradeFlowSnapshot tradeFlow,
        SentimentSnapshot sentiment)
    {
        ArgumentNullException.ThrowIfNull(derivatives);
        ArgumentNullException.ThrowIfNull(orderBook);
        ArgumentNullException.ThrowIfNull(tradeFlow);
        ArgumentNullException.ThrowIfNull(sentiment);

        var tags = new List<string>(MaxTags);

        // 1. Regime (взаимоисключающие: trending / neutral)
        var regime = GetRegimeTag(sentiment.MarketRegime);
        if (regime is not null) tags.Add(regime);

        // 2. Funding (взаимоисключающие: positive / negative)
        var funding = GetFundingTag(derivatives.FundingRate);
        if (funding is not null) tags.Add(funding);

        // 3. Pressure (взаимоисключающие: bid / ask)
        var pressure = GetPressureTag(orderBook.ImbalanceTop5);
        if (pressure is not null) tags.Add(pressure);

        // 4. Aggression (взаимоисключающие: buying приоритетнее selling)
        var aggression = GetAggressionTag(tradeFlow.HasAggressiveBuyPressure, tradeFlow.HasAggressiveSellPressure);
        if (aggression is not null) tags.Add(aggression);

        return tags;
    }

    // ─── Rule implementations ─────────────────────────────────────────────────

    /// <summary>
    /// Rule 4.1: только Trending → "trending" и Neutral → "neutral".
    /// Другие режимы (MeanReversion, Volatile) — вне V1 whitelist.
    /// </summary>
    internal static string? GetRegimeTag(string regime) => regime switch
    {
        "Trending" => TagTrending,
        "Neutral" => TagNeutral,
        _ => null,
    };

    /// <summary>
    /// Rule 4.2: fundingRate &gt; 0 → "positive-funding"; &lt; 0 → "negative-funding"; == 0 → нет тега.
    /// </summary>
    internal static string? GetFundingTag(decimal fundingRate) =>
        fundingRate > 0m ? TagPositiveFunding :
        fundingRate < 0m ? TagNegativeFunding : null;

    /// <summary>
    /// Rule 4.3: ImbalanceTop5 &gt; threshold → "bid-pressure"; &lt; -threshold → "ask-pressure".
    /// </summary>
    internal static string? GetPressureTag(decimal imbalanceTop5) =>
        imbalanceTop5 > OrderBookPressureThreshold ? TagBidPressure :
        imbalanceTop5 < -OrderBookPressureThreshold ? TagAskPressure : null;

    /// <summary>
    /// Rule 4.4: buying имеет приоритет над selling при одновременном срабатывании.
    /// </summary>
    internal static string? GetAggressionTag(bool hasBuyPressure, bool hasSellPressure) =>
        hasBuyPressure ? TagAggressiveBuying :
        hasSellPressure ? TagAggressiveSelling : null;
}
