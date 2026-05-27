namespace Intelligence.TradeSystem.Indicators.Results;

/// <summary>
/// Представляет результат расчёта scalar-индикатора (SMA, EMA, RSI, ATR и аналогичных).
/// </summary>
/// <remarks>
/// <para>
/// Позволяет явно различать следующие состояния:
/// </para>
/// <list type="bullet">
///   <item><description>Значение рассчитано полноценно — <see cref="IsAvailable"/> = <see langword="true"/>, <see cref="IsFallback"/> = <see langword="false"/>.</description></item>
///   <item><description>Значение рассчитано по fallback-логике — <see cref="IsAvailable"/> = <see langword="true"/>, <see cref="IsFallback"/> = <see langword="true"/>.</description></item>
///   <item><description>Значение недоступно — <see cref="IsAvailable"/> = <see langword="false"/>, <see cref="Value"/> = <see langword="null"/>.</description></item>
/// </list>
/// <para>
/// <see cref="Value"/> равен <see langword="null"/> только когда <see cref="IsAvailable"/> = <see langword="false"/>.
/// Для создания экземпляров используйте factory methods:
/// <see cref="Available"/>, <see cref="Fallback"/>, <see cref="Unavailable"/>.
/// </para>
/// </remarks>
public sealed record IndicatorValue
{
    /// <summary>
    /// Числовое значение индикатора. Равно <see langword="null"/>, когда <see cref="IsAvailable"/> = <see langword="false"/>.
    /// </summary>
    public required decimal? Value { get; init; }

    /// <summary>
    /// Указывает, что значение доступно для использования.
    /// Когда <see langword="true"/>, <see cref="Value"/> гарантированно не равен <see langword="null"/>.
    /// </summary>
    public required bool IsAvailable { get; init; }

    /// <summary>
    /// Указывает, что значение рассчитано по fallback-логике (например, по неполному окну).
    /// Значение можно использовать, но с пониженной надёжностью.
    /// </summary>
    public required bool IsFallback { get; init; }

    /// <summary>
    /// Причина, по которой значение является fallback или недоступным.
    /// Равно <see cref="IndicatorValueReason.None"/> только для штатно рассчитанных значений.
    /// </summary>
    public required IndicatorValueReason Reason { get; init; }

    /// <summary>
    /// Создаёт результат с полноценно рассчитанным значением.
    /// </summary>
    /// <param name="value">Числовое значение индикатора.</param>
    /// <returns>
    /// <see cref="IndicatorValue"/> с <see cref="IsAvailable"/> = <see langword="true"/>,
    /// <see cref="IsFallback"/> = <see langword="false"/> и <see cref="Reason"/> = <see cref="IndicatorValueReason.None"/>.
    /// </returns>
    public static IndicatorValue Available(decimal value) => new()
    {
        Value = value,
        IsAvailable = true,
        IsFallback = false,
        Reason = IndicatorValueReason.None,
    };

    /// <summary>
    /// Создаёт результат с fallback-значением, рассчитанным по нестандартной логике.
    /// </summary>
    /// <param name="value">Числовое значение индикатора.</param>
    /// <param name="reason">
    /// Причина применения fallback. Не может быть <see cref="IndicatorValueReason.None"/>,
    /// так как fallback всегда имеет конкретную причину.
    /// </param>
    /// <returns>
    /// <see cref="IndicatorValue"/> с <see cref="IsAvailable"/> = <see langword="true"/>,
    /// <see cref="IsFallback"/> = <see langword="true"/> и заданным <paramref name="reason"/>.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Выбрасывается, если <paramref name="reason"/> равен <see cref="IndicatorValueReason.None"/>.
    /// Fallback-значение обязано иметь явную причину.
    /// </exception>
    public static IndicatorValue Fallback(decimal value, IndicatorValueReason reason)
    {
        if (reason == IndicatorValueReason.None)
        {
            throw new ArgumentException(
                "Fallback value must have an explicit reason. Use IndicatorValueReason other than None.",
                nameof(reason));
        }

        return new()
        {
            Value = value,
            IsAvailable = true,
            IsFallback = true,
            Reason = reason,
        };
    }

    /// <summary>
    /// Создаёт результат, обозначающий отсутствие значения.
    /// </summary>
    /// <param name="reason">
    /// Причина недоступности. Не может быть <see cref="IndicatorValueReason.None"/>,
    /// так как недоступное значение всегда имеет конкретную причину.
    /// </param>
    /// <returns>
    /// <see cref="IndicatorValue"/> с <see cref="IsAvailable"/> = <see langword="false"/>,
    /// <see cref="Value"/> = <see langword="null"/> и заданным <paramref name="reason"/>.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Выбрасывается, если <paramref name="reason"/> равен <see cref="IndicatorValueReason.None"/>.
    /// Недоступное значение обязано иметь явную причину.
    /// </exception>
    public static IndicatorValue Unavailable(IndicatorValueReason reason)
    {
        if (reason == IndicatorValueReason.None)
        {
            throw new ArgumentException(
                "Unavailable value must have an explicit reason. Use IndicatorValueReason other than None.",
                nameof(reason));
        }

        return new()
        {
            Value = null,
            IsAvailable = false,
            IsFallback = false,
            Reason = reason,
        };
    }
}
