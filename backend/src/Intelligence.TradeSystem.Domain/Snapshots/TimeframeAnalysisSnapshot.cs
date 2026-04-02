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

    /// <summary>Экспоненциальная скользящая средняя за 20 периодов (EMA 20).</summary>
    public decimal Ema20 { get; init; }

    /// <summary>Экспоненциальная скользящая средняя за 50 периодов (EMA 50).</summary>
    public decimal Ema50 { get; init; }

    /// <summary>
    /// Экспоненциальная скользящая средняя за 200 периодов (EMA 200).
    /// Ключевой ориентир долгосрочного тренда.
    /// </summary>
    public decimal Ema200 { get; init; }

    /// <summary>
    /// Индекс относительной силы за 14 периодов (RSI 14).
    /// Диапазон [0, 100]: выше 70 — перекупленность, ниже 30 — перепроданность.
    /// </summary>
    public decimal Rsi14 { get; init; }

    /// <summary>
    /// Средний истинный диапазон за 14 периодов (ATR 14).
    /// Характеризует текущую волатильность инструмента в единицах цены.
    /// </summary>
    public decimal Atr14 { get; init; }

    /// <summary>Простая скользящая средняя объёма за 20 периодов. Базовый ориентир нормального объёма.</summary>
    public decimal VolumeSma20 { get; init; }

    /// <summary>
    /// Отношение объёма последней свечи к <see cref="VolumeSma20"/>.
    /// Значение выше 1 означает повышенную активность, ниже 1 — пониженную.
    /// </summary>
    public decimal VolumeRatio { get; init; }

    /// <summary>
    /// Внутренняя оценка силы тренда в диапазоне [0, 1].
    /// Вычисляется на основе выравнивания EMA, наклона средних и подтверждения объёмом.
    /// </summary>
    public decimal TrendStrengthScore { get; init; }

    /// <summary>Направление рыночного тренда, определённое для данного таймфрейма.</summary>
    public MarketTrend Trend { get; init; }

    /// <summary>Ближайший значимый уровень поддержки (первый снизу от текущей цены).</summary>
    public decimal Support1 { get; init; }

    /// <summary>Второй значимый уровень поддержки (глубже первого).</summary>
    public decimal Support2 { get; init; }

    /// <summary>Ближайший значимый уровень сопротивления (первый сверху от текущей цены).</summary>
    public decimal Resistance1 { get; init; }

    /// <summary>Второй значимый уровень сопротивления (выше первого).</summary>
    public decimal Resistance2 { get; init; }

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

    /// <summary>
    /// Диапазон свечи в процентах: <c>(High − Low) / Close × 100</c>.
    /// Отражает волатильность внутри периода.
    /// </summary>
    public decimal CandleRangePct { get; init; }

    /// <summary>
    /// Расстояние от текущей цены до <see cref="Support1"/> в процентах.
    /// Помогает оценить риск при позиционировании у поддержки.
    /// </summary>
    public decimal DistanceToSupport1Pct { get; init; }

    /// <summary>
    /// Расстояние от текущей цены до <see cref="Resistance1"/> в процентах.
    /// Помогает оценить потенциал движения к сопротивлению.
    /// </summary>
    public decimal DistanceToResistance1Pct { get; init; }
}
