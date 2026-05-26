using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Analysis;

/// <summary>
/// Применяет cap-корректировки к raw <c>tradeFlowPressureScore</c>:
/// freshness, window duration, absolute volume и конфликт с orderBook.
/// <para>
/// Все caps применяются по принципу строгого минимума.
/// Знак исходного score сохраняется: cap применяется к абсолютному значению.
/// </para>
/// </summary>
internal static class TradeFlowPressureScoreAdjuster
{
    // -- Freshness caps -------------------------------------------------------

    /// <summary>
    /// Значение по умолчанию для maxTradeFlowAgeMs.
    /// Совпадает с <c>SnapshotFreshnessOptions.Default.Intraday.TradeFlowMaxAge</c> (5 с) —
    /// наиболее строгим порогом из всех режимов.
    /// При вызове из Application-слоя это значение используется как консервативный fallback;
    /// точный mode-specific порог передаётся из <c>SectionFreshnessOptions</c> в API-слое.
    /// </summary>
    internal const long DefaultMaxTradeFlowAgeMs = 5_000L; // 5 s — Intraday threshold

    /// <summary>Cap для age > maxAge (stale).</summary>
    internal const decimal StaleCap = 0.50m;

    /// <summary>Cap для age > maxAge × 2 (very stale).</summary>
    internal const decimal VeryStaleCap = 0.25m;

    // -- Window duration caps -------------------------------------------------

    /// <summary>Нижняя граница большого окна; выше этой cap не применяется к window.</summary>
    internal const double WindowLargeCapThresholdSeconds = 60.0;

    /// <summary>Нижняя граница среднего окна.</summary>
    internal const double WindowMediumCapThresholdSeconds = 30.0;

    /// <summary>Нижняя граница короткого окна.</summary>
    internal const double WindowShortCapThresholdSeconds = 10.0;

    /// <summary>Cap для windowDuration >= 60 s — отсутствие cap на уровне window.</summary>
    /// <remarks>Значение 1 означает «нет cap».</remarks>
    internal const decimal WindowNoCap = 1.0m;

    /// <summary>Cap для windowDuration в [30, 60).</summary>
    internal const decimal WindowLargeCap = 0.50m;

    /// <summary>Cap для windowDuration в [10, 30).</summary>
    internal const decimal WindowMediumCap = 0.35m;

    /// <summary>Cap для windowDuration &lt; 10 s.</summary>
    internal const decimal WindowShortCap = 0.25m;

    // -- Volume caps ----------------------------------------------------------

    // TODO: ввести symbol-specific пороги объёма (например, в единицах symbol > thresholds).
    // Текущие пороги калиброваны по BTCUSDT (единицы: base asset, например BTC).

    /// <summary>Нижний порог объёма (< 1 BTC → low-volume cap).</summary>
    internal const decimal VolumeLowThreshold = 1.0m;

    /// <summary>Средний порог объёма (< 3 BTC → medium-volume cap).</summary>
    internal const decimal VolumeMediumThreshold = 3.0m;

    /// <summary>Cap для totalVolume &lt; VolumeLowThreshold или когда объём не рассчитан.</summary>
    internal const decimal VolumeLowCap = 0.35m;

    /// <summary>Cap для totalVolume в [VolumeLowThreshold, VolumeMediumThreshold).</summary>
    internal const decimal VolumeMediumCap = 0.50m;

    // -- Conflict caps --------------------------------------------------------

    /// <summary>Cap при конфликте orderBook vs tradeFlow.</summary>
    internal const decimal ConflictCap = 0.50m;

    /// <summary>
    /// Усиленный cap при конфликте + stale tradeFlow или window &lt; 30 s.
    /// </summary>
    internal const decimal ConflictWithWeaknessCap = 0.25m;

    // -- Public API -----------------------------------------------------------

    /// <summary>
    /// Применяет все quality caps к <paramref name="rawScore"/> и возвращает скорректированный score.
    /// <list type="bullet">
    ///   <item>Знак исходного score сохраняется.</item>
    ///   <item>Применяется строгий cap минимума (наименьший из всех сработавших caps).</item>
    ///   <item>Если rawScore == 0, возвращается 0 без изменений.</item>
    /// </list>
    /// </summary>
    /// <param name="rawScore">Нескорректированный score до применения caps.</param>
    /// <param name="tradeFlow">Снимок данных потока.</param>
    /// <param name="orderBookPressureScore">Нормализованный score давления книги ордеров.</param>
    /// <param name="capturedAtUtc">
    /// Момент создания снимка. Если <c>null</c>, freshness cap не применяется
    /// (например, логика возраста обрабатывается во внешнем age-check).
    /// </param>
    /// <param name="maxTradeFlowAgeMs">
    /// Максимальный допустимый возраст tradeFlow в мс.
    /// Defaults to <see cref="DefaultMaxTradeFlowAgeMs"/>.
    /// </param>
    public static decimal ApplyCaps(
        decimal rawScore,
        TradeFlowSnapshot tradeFlow,
        decimal orderBookPressureScore,
        DateTimeOffset? capturedAtUtc = null,
        long maxTradeFlowAgeMs = DefaultMaxTradeFlowAgeMs)
    {
        if (rawScore == 0m) return 0m;

        var cap = 1.0m; // без cap по умолчанию

        // 1. Freshness cap (только при наличии reference time)
        cap = Math.Min(cap, ComputeFreshnessCap(tradeFlow, capturedAtUtc, maxTradeFlowAgeMs));

        // 2. Window duration cap
        var windowSeconds = (tradeFlow.WindowEndUtc - tradeFlow.WindowStartUtc).TotalSeconds;
        cap = Math.Min(cap, ComputeWindowCap(windowSeconds));

        // 3. Volume cap
        var totalVolume = tradeFlow.BuyVolume + tradeFlow.SellVolume;
        cap = Math.Min(cap, ComputeVolumeCap(totalVolume));

        // 4. Conflict cap
        if (HasOrderBookConflict(rawScore, orderBookPressureScore))
        {
            var isWeak = IsStaleOrShortWindow(tradeFlow, capturedAtUtc, maxTradeFlowAgeMs, windowSeconds);
            var conflictCap = isWeak ? ConflictWithWeaknessCap : ConflictCap;
            cap = Math.Min(cap, conflictCap);
        }

        // Apply cap with sign preservation
        return ApplyCapToScore(rawScore, cap);
    }

    /// <summary>
    /// Возвращает набор quality-тегов, связанных с качеством tradeFlow.
    /// Теги предназначены для отдельной регистрации в MarketTagsBuilder V2.
    /// </summary>
    public static IReadOnlyList<string> ComputeQualityTags(
        decimal rawScore,
        TradeFlowSnapshot tradeFlow,
        decimal orderBookPressureScore,
        DateTimeOffset? capturedAtUtc = null,
        long maxTradeFlowAgeMs = DefaultMaxTradeFlowAgeMs)
    {
        var tags = new List<string>();

        if (capturedAtUtc.HasValue && IsStale(tradeFlow, capturedAtUtc.Value, maxTradeFlowAgeMs))
            tags.Add(MarketTagConstants.StaleTradeFlow);

        var windowSeconds = (tradeFlow.WindowEndUtc - tradeFlow.WindowStartUtc).TotalSeconds;
        if (windowSeconds < WindowMediumCapThresholdSeconds)
            tags.Add(MarketTagConstants.ShortTradeFlowWindow);

        var totalVolume = tradeFlow.BuyVolume + tradeFlow.SellVolume;
        if (totalVolume < VolumeMediumThreshold)
            tags.Add(MarketTagConstants.LowTradeFlowVolume);

        if (rawScore != 0m && HasOrderBookConflict(rawScore, orderBookPressureScore))
            tags.Add(MarketTagConstants.OrderBookTradeFlowConflict);

        // Сводный предупредительный тег: хотя бы один factors сработал
        if (tags.Count > 0)
            tags.Add(MarketTagConstants.WeakTradeFlowConfirmation);

        return tags;
    }

    // -- Private helpers ------------------------------------------------------

    /// <summary>
    /// Вычисляет freshness cap. Возвращает 1.0 (нет cap), если capturedAtUtc == null.
    /// </summary>
    private static decimal ComputeFreshnessCap(
        TradeFlowSnapshot tradeFlow,
        DateTimeOffset? capturedAtUtc,
        long maxTradeFlowAgeMs)
    {
        if (!capturedAtUtc.HasValue)
            return WindowNoCap; // нет reference time — freshness cap не применяется

        var ageMs = ComputeAgeMs(tradeFlow, capturedAtUtc.Value);

        if (ageMs > maxTradeFlowAgeMs * 2L) return VeryStaleCap;
        if (ageMs > maxTradeFlowAgeMs) return StaleCap;

        return WindowNoCap;
    }

    /// <summary>Вычисляет window cap по длине окна в секундах.</summary>
    internal static decimal ComputeWindowCap(double windowSeconds)
    {
        if (windowSeconds < WindowShortCapThresholdSeconds) return WindowShortCap;
        if (windowSeconds < WindowMediumCapThresholdSeconds) return WindowMediumCap;
        if (windowSeconds < WindowLargeCapThresholdSeconds) return WindowLargeCap;
        return WindowNoCap;
    }

    /// <summary>Вычисляет volume cap по суммарному объёму потока.</summary>
    internal static decimal ComputeVolumeCap(decimal totalVolume)
    {
        if (totalVolume <= 0m || totalVolume < VolumeLowThreshold) return VolumeLowCap;
        if (totalVolume < VolumeMediumThreshold) return VolumeMediumCap;
        return WindowNoCap;
    }

    /// <summary>
    /// Возвращает true, если знаки rawScore и orderBookPressureScore противоположны
    /// (или нейтральный конфликт).
    /// </summary>
    internal static bool HasOrderBookConflict(decimal tradeFlowScore, decimal orderBookScore)
        => (tradeFlowScore > 0m && orderBookScore < 0m)
        || (tradeFlowScore < 0m && orderBookScore > 0m);

    /// <summary>
    /// Определяет, является ли tradeFlow слабым для применения conflict cap:
    /// stale или window &lt; 30 s.
    /// </summary>
    private static bool IsStaleOrShortWindow(
        TradeFlowSnapshot tradeFlow,
        DateTimeOffset? capturedAtUtc,
        long maxTradeFlowAgeMs,
        double windowSeconds)
    {
        if (windowSeconds < WindowMediumCapThresholdSeconds) return true;
        if (capturedAtUtc.HasValue && IsStale(tradeFlow, capturedAtUtc.Value, maxTradeFlowAgeMs)) return true;
        return false;
    }

    /// <summary>Возвращает true, если tradeFlow старше maxTradeFlowAgeMs.</summary>
    internal static bool IsStale(TradeFlowSnapshot tradeFlow, DateTimeOffset reference, long maxTradeFlowAgeMs)
        => ComputeAgeMs(tradeFlow, reference) > maxTradeFlowAgeMs;

    private static long ComputeAgeMs(TradeFlowSnapshot tradeFlow, DateTimeOffset reference)
        => (long)Math.Max(0.0, (reference - tradeFlow.WindowEndUtc).TotalMilliseconds);

    /// <summary>
    /// Применяет cap к score с сохранением знака.
    /// Если |rawScore| &lt;= cap, score не изменяется (cap не срабатывает).
    /// </summary>
    internal static decimal ApplyCapToScore(decimal rawScore, decimal cap)
    {
        var abs = Math.Abs(rawScore);
        if (abs <= cap) return rawScore;
        return rawScore > 0m ? cap : -cap;
    }
}
