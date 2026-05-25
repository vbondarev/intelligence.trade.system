namespace Intelligence.TradeSystem.Domain.Snapshots;

/// <summary>
/// Результаты технического анализа для одного таймфрейма.
/// Содержит последнюю свечу, скользящие средние, осцилляторы,
/// уровни поддержки/сопротивления и производные булевы сигналы,
/// упрощающие интерпретацию для GPT.
/// </summary>
public sealed record TimeframeAnalysisSnapshot
{
    /// <summary>
    /// Обозначение таймфрейма.
    /// Возможные значения: <c>15m</c>, <c>1h</c>, <c>4h</c>, <c>1d</c>.
    /// </summary>
    public required string Timeframe { get; init; }

    /// <summary>Время открытия последней закрытой свечи данного таймфрейма (UTC).</summary>
    public required DateTimeOffset LastCandleOpenTimeUtc { get; init; }

    /// <summary>OHLCV-данные последней закрытой свечи данного таймфрейма.</summary>
    public required CandleSnapshot LastCandle { get; init; }

    /// <summary>Экспоненциальная скользящая средняя за 20 периодов (EMA 20).
    /// <c>null</c> — недостаточно данных для расчёта.</summary>
    public decimal? Ema20 { get; init; }

    /// <summary>Экспоненциальная скользящая средняя за 50 периодов (EMA 50).
    /// <c>null</c> — недостаточно данных для расчёта.</summary>
    public decimal? Ema50 { get; init; }

    /// <summary>
    /// Экспоненциальная скользящая средняя за 200 периодов (EMA 200).
    /// Ключевой ориентир долгосрочного тренда.
    /// <c>null</c> — недостаточно данных для расчёта.
    /// </summary>
    public decimal? Ema200 { get; init; }

    /// <summary>
    /// Индекс относительной силы за 14 периодов (RSI 14).
    /// Диапазон [0, 100]: выше 70 — перекупленность, ниже 30 — перепроданность.
    /// <c>null</c> — недостаточно данных для расчёта.
    /// </summary>
    public decimal? Rsi14 { get; init; }

    /// <summary>
    /// <c>true</c>, если для данного таймфрейма было достаточно свечей для корректного расчёта RSI 14.
    /// <c>false</c> означает, что <see cref="Rsi14"/> равен <c>null</c>,
    /// а <see cref="RsiOverbought"/> и <see cref="RsiOversold"/> принудительно выставлены в <c>false</c>.
    /// Потребители должны проверять это поле перед интерпретацией RSI-сигналов.
    /// </summary>
    public bool Rsi14IsReliable { get; init; }

    /// <summary>
    /// Средний истинный диапазон за 14 периодов (ATR 14).
    /// Характеризует текущую волатильность инструмента в единицах цены.
    /// <c>null</c> — недостаточно данных (менее двух свечей). Не интерпретировать как нулевую волатильность.
    /// </summary>
    public decimal? Atr14 { get; init; }

    /// <summary>Простая скользящая средняя объёма за 20 периодов. Базовый ориентир нормального объёма.
    /// <c>null</c> — недостаточно данных.</summary>
    public decimal? VolumeSma20 { get; init; }

    /// <summary>
    /// Отношение объёма последней свечи к <see cref="VolumeSma20"/>.
    /// Значение выше 1 означает повышенную активность, ниже 1 — пониженную.
    /// <c>null</c> — если <see cref="VolumeSma20"/> недоступен или равен нулю.
    /// </summary>
    public decimal? VolumeRatio { get; init; }

    /// <summary>
    /// Внутренняя оценка силы тренда в диапазоне [0, 1].
    /// Для направленного тренда лежит в диапазоне [0.80, 1.00] и может усиливаться повышенным объёмом.
    /// Для бокового рынка лежит в диапазоне [0.00, 0.49], чтобы не выглядеть как сильный тренд.
    /// </summary>
    public decimal TrendStrengthScore { get; init; }

    /// <summary>Направление рыночного тренда, определённое для данного таймфрейма.</summary>
    public MarketTrend Trend { get; init; }

    /// <summary>Ближайший значимый уровень поддержки (первый снизу от текущей цены). <c>null</c> — не обнаружен.</summary>
    public decimal? Support1 { get; init; }

    /// <summary>
    /// Нормализованная сила уровня <see cref="Support1"/> в диапазоне [0, 1].
    /// Вычисляется как <c>clusterVolume / maxClusterVolume</c> профиля.
    /// <c>null</c> — уровень не обнаружен.
    /// </summary>
    public decimal? Support1Strength { get; init; }

    /// <summary>
    /// Суммарный объём бакетов HVN-кластера, лежащего в основе <see cref="Support1"/>.
    /// <c>null</c> — уровень не обнаружен.
    /// </summary>
    public decimal? Support1ClusterVolume { get; init; }

    /// <summary>Второй значимый уровень поддержки (глубже первого). <c>null</c> — не обнаружен.</summary>
    public decimal? Support2 { get; init; }

    /// <summary>
    /// Нормализованная сила уровня <see cref="Support2"/> в диапазоне [0, 1].
    /// <c>null</c> — уровень не обнаружен.
    /// </summary>
    public decimal? Support2Strength { get; init; }

    /// <summary>
    /// Суммарный объём бакетов HVN-кластера, лежащего в основе <see cref="Support2"/>.
    /// <c>null</c> — уровень не обнаружен.
    /// </summary>
    public decimal? Support2ClusterVolume { get; init; }

    /// <summary>Ближайший значимый уровень сопротивления (первый сверху от текущей цены). <c>null</c> — не обнаружен.</summary>
    public decimal? Resistance1 { get; init; }

    /// <summary>
    /// Нормализованная сила уровня <see cref="Resistance1"/> в диапазоне [0, 1].
    /// <c>null</c> — уровень не обнаружен.
    /// </summary>
    public decimal? Resistance1Strength { get; init; }

    /// <summary>
    /// Суммарный объём бакетов HVN-кластера, лежащего в основе <see cref="Resistance1"/>.
    /// <c>null</c> — уровень не обнаружен.
    /// </summary>
    public decimal? Resistance1ClusterVolume { get; init; }

    /// <summary>Второй значимый уровень сопротивления (выше первого). <c>null</c> — не обнаружен.</summary>
    public decimal? Resistance2 { get; init; }

    /// <summary>
    /// Нормализованная сила уровня <see cref="Resistance2"/> в диапазоне [0, 1].
    /// <c>null</c> — уровень не обнаружен.
    /// </summary>
    public decimal? Resistance2Strength { get; init; }

    /// <summary>
    /// Суммарный объём бакетов HVN-кластера, лежащего в основе <see cref="Resistance2"/>.
    /// <c>null</c> — уровень не обнаружен.
    /// </summary>
    public decimal? Resistance2ClusterVolume { get; init; }

    /// <summary><c>true</c>, если цена закрытия последней свечи выше EMA 20.</summary>
    public bool IsAboveEma20 { get; init; }

    /// <summary><c>true</c>, если цена закрытия последней свечи выше EMA 50.</summary>
    public bool IsAboveEma50 { get; init; }

    /// <summary><c>true</c>, если цена закрытия последней свечи выше EMA 200.</summary>
    public bool IsAboveEma200 { get; init; }

    /// <summary>
    /// <c>true</c>, если EMA выстроены в бычьем порядке: <c>EMA20 &gt; EMA50 &gt; EMA200</c>.
    /// Классический сигнал устойчивого восходящего тренда.
    /// </summary>
    public bool EmaBullishAlignment { get; init; }

    /// <summary>
    /// <c>true</c>, если EMA выстроены в медвежьем порядке: <c>EMA20 &lt; EMA50 &lt; EMA200</c>.
    /// Классический сигнал устойчивого нисходящего тренда.
    /// </summary>
    public bool EmaBearishAlignment { get; init; }

    /// <summary><c>true</c>, если RSI превысил порог перекупленности (обычно 70).</summary>
    public bool RsiOverbought { get; init; }

    /// <summary><c>true</c>, если RSI опустился ниже порога перепроданности (обычно 30).</summary>
    public bool RsiOversold { get; init; }

    // ─── Indicator availability / fallback flags ─────────────────────────────

    /// <summary>
    /// <c>true</c>, если все три EMA (20, 50, 200) имеют вычисленные значения
    /// (включая fallback по частичному окну).
    /// <c>false</c> — хотя бы одна EMA недоступна из-за нехватки данных.
    /// При <c>false</c> <see cref="Trend"/> принудительно равен <see cref="MarketTrend.Unknown"/>,
    /// а <see cref="EmaBullishAlignment"/> / <see cref="EmaBearishAlignment"/> — <c>false</c>.
    /// </summary>
    public bool EmaIsReliable { get; init; }

    /// <summary>
    /// <c>true</c>, если хотя бы одна из EMA была рассчитана по fallback-логике
    /// (частичное окно, меньше, чем требуемый период).
    /// Потребители должны учитывать это при интерпретации EMA-сигналов.
    /// </summary>
    public bool EmaHasFallback { get; init; }

    /// <summary>
    /// <c>true</c>, если ATR 14 был рассчитан и имеет достоверное значение.
    /// <c>false</c> — недостаточно данных (менее двух свечей); <see cref="Atr14"/> равен <c>null</c>.
    /// Потребители не должны интерпретировать отсутствие ATR как «нулевую волатильность».
    /// </summary>
    public bool AtrIsReliable { get; init; }

    /// <summary>
    /// <c>true</c>, если ATR 14 был рассчитан по fallback-логике (меньше, чем 14 TR-значений).
    /// Значение доступно, но является менее точной оценкой.
    /// </summary>
    public bool AtrIsFallback { get; init; }

    /// <summary>
    /// <c>true</c>, если <see cref="VolumeRatio"/> был рассчитан на основе достоверного
    /// <see cref="VolumeSma20"/> (значение VolumeSma20 доступно и больше нуля).
    /// <c>false</c> — VolumeSma20 недоступен; <see cref="VolumeRatio"/> равен <c>null</c>
    /// и не несёт торгового смысла.
    /// </summary>
    public bool VolumeRatioIsReliable { get; init; }

    /// <summary>
    /// <c>true</c>, если <see cref="VolumeSma20"/> был рассчитан по fallback-логике
    /// (частичное окно: меньше 20 свечей).
    /// <see cref="VolumeRatio"/> доступен, но является менее точной оценкой.
    /// </summary>
    public bool VolumeRatioIsFallback { get; init; }

    /// <summary>
    /// Диапазон свечи в процентах: <c>(High − Low) / Close × 100</c>.
    /// Отражает волатильность внутри периода.
    /// </summary>
    public decimal CandleRangePct { get; init; }

    /// <summary>
    /// Расстояние от текущей цены до <see cref="Support1"/> в процентах.
    /// <c>null</c> — если <see cref="Support1"/> не обнаружен.
    /// </summary>
    public decimal? DistanceToSupport1Pct { get; init; }

    /// <summary>
    /// Расстояние от текущей цены до <see cref="Resistance1"/> в процентах.
    /// <c>null</c> — если <see cref="Resistance1"/> не обнаружен.
    /// </summary>
    public decimal? DistanceToResistance1Pct { get; init; }

    /// <summary>
    /// Диагностические записи для индикаторов этого таймфрейма.
    /// Пустой список означает, что все индикаторы рассчитаны полноценно.
    /// </summary>
    public IReadOnlyList<IndicatorDiagnosticSnapshot> IndicatorDiagnostics { get; init; } = [];
}
