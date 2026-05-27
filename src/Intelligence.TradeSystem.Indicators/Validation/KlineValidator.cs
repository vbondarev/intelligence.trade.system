using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Indicators.Validation;

/// <summary>
/// Детерминированный, без побочных эффектов валидатор рыночных данных свечей.
/// </summary>
/// <remarks>
/// <para>
/// Проверяет каждую свечу на соответствие базовым OHLC-инвариантам:
/// <list type="bullet">
///   <item><description><c>High &gt;= Low</c></description></item>
///   <item><description><c>Open, High, Low, Close, Volume &gt;= 0</c></description></item>
///   <item><description><c>Low &lt;= Open &lt;= High</c></description></item>
///   <item><description><c>Low &lt;= Close &lt;= High</c></description></item>
/// </list>
/// </para>
/// <para>
/// Политика: невалидные свечи <b>отфильтровываются</b> методом <see cref="FilterValid"/>,
/// а нарушения возвращаются как диагностика — без исключений и без потери всего набора.
/// </para>
/// <para>
/// Граничный случай <c>Open</c> / <c>Close</c> вне <c>[Low, High]</c> проверяется
/// точно (без tolerance), поскольку данные уже нормализованы биржевым адаптером.
/// </para>
/// </remarks>
public static class KlineValidator
{
    /// <summary>
    /// Проверяет одну свечу и возвращает результат валидации.
    /// </summary>
    /// <param name="kline">Свеча для проверки.</param>
    /// <param name="index">0-based индекс свечи в исходном массиве (для диагностики).</param>
    public static KlineValidationResult Validate(Kline kline, int index)
    {
        ArgumentNullException.ThrowIfNull(kline);

        if (kline.High < kline.Low)
            return KlineValidationResult.Invalid(index, $"High ({kline.High}) < Low ({kline.Low}).");

        if (kline.Open < 0m)
            return KlineValidationResult.Invalid(index, $"Open ({kline.Open}) is negative.");

        if (kline.High < 0m)
            return KlineValidationResult.Invalid(index, $"High ({kline.High}) is negative.");

        if (kline.Low < 0m)
            return KlineValidationResult.Invalid(index, $"Low ({kline.Low}) is negative.");

        if (kline.Close < 0m)
            return KlineValidationResult.Invalid(index, $"Close ({kline.Close}) is negative.");

        if (kline.Volume < 0m)
            return KlineValidationResult.Invalid(index, $"Volume ({kline.Volume}) is negative.");

        if (kline.Open < kline.Low || kline.Open > kline.High)
            return KlineValidationResult.Invalid(
                index,
                $"Open ({kline.Open}) is outside [Low={kline.Low}, High={kline.High}].");

        if (kline.Close < kline.Low || kline.Close > kline.High)
            return KlineValidationResult.Invalid(
                index,
                $"Close ({kline.Close}) is outside [Low={kline.Low}, High={kline.High}].");

        return KlineValidationResult.Valid(index);
    }

    /// <summary>
    /// Фильтрует коллекцию свечей, возвращая только валидные, и выдаёт список нарушений.
    /// </summary>
    /// <param name="klines">Исходный список свечей.</param>
    /// <param name="violations">
    /// Список <see cref="KlineValidationResult"/> с нарушениями (пустой, если все свечи валидны).
    /// </param>
    /// <returns>
    /// Список валидных свечей. Может быть пустым, если все свечи невалидны.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Этот метод выполняет только фильтрацию и сбор нарушений.
    /// Политика деградации после фильтрации (проверка последней свечи, порог % невалидных,
    /// минимальное количество оставшихся данных) намеренно вынесена в вызывающий слой
    /// (<c>TimeframeSnapshotAssembler</c>), а не реализована здесь.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<Kline> FilterValid(
        IReadOnlyList<Kline> klines,
        out IReadOnlyList<KlineValidationResult> violations)
    {
        ArgumentNullException.ThrowIfNull(klines);

        List<Kline>? valid = null;
        List<KlineValidationResult>? invalidList = null;

        for (var i = 0; i < klines.Count; i++)
        {
            var result = Validate(klines[i], i);
            if (result.IsValid)
            {
                valid ??= new List<Kline>(klines.Count);
                valid.Add(klines[i]);
            }
            else
            {
                invalidList ??= [];
                invalidList.Add(result);
            }
        }

        violations = (IReadOnlyList<KlineValidationResult>?)invalidList ?? [];

        // No invalid klines — return original to avoid allocation.
        // No valid klines — return empty.
        return valid is null
            ? (invalidList is null ? klines : Array.Empty<Kline>())
            : valid;
    }
}
