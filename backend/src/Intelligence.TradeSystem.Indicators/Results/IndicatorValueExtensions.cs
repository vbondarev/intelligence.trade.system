namespace Intelligence.TradeSystem.Indicators.Results;

/// <summary>
/// Extension methods для централизованного извлечения значений из <see cref="IndicatorValue"/>.
/// </summary>
public static class IndicatorValueExtensions
{
    /// <summary>
    /// Возвращает числовое значение индикатора или <see langword="null"/>, если значение недоступно.
    /// </summary>
    /// <remarks>
    /// Предпочтительный метод для nullable-контрактов: DTO, LLM payload, snapshot contracts.
    /// </remarks>
    /// <param name="value">Результат расчёта индикат��ра.</param>
    /// <returns><see cref="IndicatorValue.Value"/> — может быть <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException">Выбрасывается, если <paramref name="value"/> равен <see langword="null"/>.</exception>
    public static decimal? OrNull(this IndicatorValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value.Value;
    }

    /// <summary>
    /// Возвращает числовое значение индикатора или выбрасывает исключение, если значение недоступно.
    /// </summary>
    /// <remarks>
    /// Fail-fast метод для обязательных индикаторов. Используйте там, где отсутствие
    /// значения является ошибкой и не должно молча превращаться в <c>0</c>.
    /// Fallback-значения (<see cref="IndicatorValue.IsFallback"/> == <see langword="true"/>)
    /// считаются доступными и возвращаются без исключения.
    /// </remarks>
    /// <param name="value">Результат расчёта индикатора.</param>
    /// <returns>Числовое значение индикатора.</returns>
    /// <exception cref="ArgumentNullException">Выбрасывается, если <paramref name="value"/> равен <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Выбрасывается, если <see cref="IndicatorValue.IsAvailable"/> равен <see langword="false"/>.</exception>
    public static decimal RequireValue(this IndicatorValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.IsAvailable && value.Value.HasValue)
        {
            return value.Value.Value;
        }

        throw new InvalidOperationException(
            $"Indicator value is not available. Reason: {value.Reason}.");
    }

    /// <summary>
    /// Возвращает <see langword="true"/>, если значение индикатора доступно и может быть использовано.
    /// </summary>
    /// <remarks>
    /// Безопасная проверка доступности значения. Возвращает <see langword="false"/> при
    /// <see langword="null"/> receiver вместо исключения.
    /// Возвращает <see langword="true"/> как для полноценных, так и для fallback-значений.
    /// </remarks>
    /// <param name="value">Результат расчёта индикатора. Может быть <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="true"/>, если <see cref="IndicatorValue.IsAvailable"/> == <see langword="true"/>
    /// и <see cref="IndicatorValue.Value"/> имеет значение; иначе <see langword="false"/>.
    /// </returns>
    public static bool HasUsableValue(this IndicatorValue? value)
    {
        return value is { IsAvailable: true } && value.Value.HasValue;
    }

    /// <summary>
    /// Возвращает <see langword="true"/>, если значение индикатора требует отражения в диагностике или предупреждениях.
    /// </summary>
    /// <remarks>
    /// Используется для формирования <c>IndicatorDiagnostics</c> в <c>TimeframeSnapshotAssembler</c>
    /// и последующей передачи в API/LLM payload через <c>indicatorDiagnostics</c>.
    /// Возвращает <see langword="true"/> для fallback-значений и недоступных значений.
    /// </remarks>
    /// <param name="value">Результат расчёта индикатора.</param>
    /// <returns>
    /// <see langword="true"/>, если <see cref="IndicatorValue.IsFallback"/> == <see langword="true"/>
    /// или <see cref="IndicatorValue.IsAvailable"/> == <see langword="false"/>; иначе <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">Выбрасывается, если <paramref name="value"/> равен <see langword="null"/>.</exception>
    public static bool ShouldReportDiagnostic(this IndicatorValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value.IsFallback || !value.IsAvailable;
    }
}
