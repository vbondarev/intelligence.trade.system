namespace Intelligence.TradeSystem.Analysis;

/// <summary>
/// Публичные строковые константы тегов снапшота V1 и V2.
/// Являются единственным источником истины для значений тегов.
/// <para>
/// Используются как <see cref="MarketTagsBuilder"/> (Analysis-слой),
/// так и <c>LlmTagEnricher</c> (API-слой), чтобы избежать дублирования строк.
/// </para>
/// </summary>
public static class MarketTagConstants
{
    // ─── V1 ───────────────────────────────────────────────────────────────────
    public const string Trending = "trending";
    public const string Neutral = "neutral";
    public const string PositiveFunding = "positive-funding";
    public const string NegativeFunding = "negative-funding";
    public const string BidPressure = "bid-pressure";
    public const string AskPressure = "ask-pressure";
    public const string AggressiveBuying = "aggressive-buying";
    public const string AggressiveSelling = "aggressive-selling";

    // ─── V2 — режим ───────────────────────────────────────────────────────────
    public const string VolatileRegime = "volatile-regime";
    public const string BullishRegime = "bullish-regime";
    public const string BearishRegime = "bearish-regime";
    public const string UnknownMarketRegime = "unknown-market-regime";

    // ─── V2 — health (выставляется API-layer enricher) ────────────────────────
    public const string StaleSnapshot = "stale-snapshot";
    public const string StaleOrderBook = "stale-orderbook";

    // ─── V2 — tradeFlow quality ───────────────────────────────────────────────
    public const string StaleTradeFlow = "stale-tradeflow";
    public const string ShortTradeFlowWindow = "short-tradeflow-window";
    public const string LowTradeFlowVolume = "low-tradeflow-volume";
    public const string OrderBookTradeFlowConflict = "orderbook-tradeflow-conflict";
    public const string WeakTradeFlowConfirmation = "weak-tradeflow-confirmation";

    // ─── V2 — orderBook ───────────────────────────────────────────────────────
    public const string StrongOrderBookImbalance = "strong-orderbook-imbalance";
    public const string UpperLiquidityHeavy = "upper-liquidity-heavy";
    public const string LowerLiquidityHeavy = "lower-liquidity-heavy";

    // ─── V2 — OI / деривативы ─────────────────────────────────────────────────
    public const string OiDeclining = "oi-declining";
    public const string OiRising = "oi-rising";
    public const string LongCrowded = "long-crowded";
    public const string ShortCrowded = "short-crowded";
    public const string PossibleShortCovering = "possible-short-covering";
    public const string PossibleLongUnwinding = "possible-long-unwinding";
    public const string NeutralFunding = "neutral-funding";

    // ─── V2 — цена / 24ч ─────────────────────────────────────────────────────
    public const string Near24hHigh = "near-24h-high";
    public const string Near24hLow = "near-24h-low";

    // ─── V2 — таймфреймы ─────────────────────────────────────────────────────
    public const string LowVolume = "low-volume";
    public const string RsiOverbought = "rsi-overbought";
    public const string RsiOversold = "rsi-oversold";
    public const string WeakTrend = "weak-trend";
    public const string RangeBound = "range-bound";
    public const string NeutralTimeframes = "neutral-timeframes";
    public const string NearResistance = "near-resistance";
    public const string NearSupport = "near-support";
    public const string OverextendedMomentum = "overextended-momentum";
    public const string DirectionalTrendWithNeutralRegime = "directional-trend-with-neutral-regime";

    // ─── V2 — качество входа (выставляется LlmTagEnricher) ───────────────────
    public const string NoCleanEntry = "no-clean-entry";
    public const string ActionableEntry = "actionable-entry";
    public const string WeakEntryConfirmation = "weak-entry-confirmation";
    public const string TrendConfirmedEntryFiltered = "trend-confirmed-entry-filtered";

    // ─── Максимум тегов ───────────────────────────────────────────────────────
    public const int MaxTags = 20;
}
