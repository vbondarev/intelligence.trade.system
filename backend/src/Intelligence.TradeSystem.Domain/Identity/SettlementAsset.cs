namespace Intelligence.TradeSystem.Domain.Identity;

/// <summary>
/// Актив расчёта позиции (settlement asset), в котором выражены её денежные значения,
/// например <c>USDT</c> или <c>USDC</c>. Не зависит от конкретной биржи и не ограничен
/// фиксированным набором значений.
/// </summary>
/// <remarks>
/// Не входит в <see cref="ExchangePositionKey"/>: актив расчёта является неизменяемой
/// характеристикой жизненного цикла позиции, а не частью её идентичности.
/// </remarks>
public readonly record struct SettlementAsset
{
    /// <summary>Значение актива расчёта.</summary>
    public string Value { get; }

    private SettlementAsset(string value) => Value = value;

    /// <summary>
    /// Создаёт актив расчёта из строки. Внешние пробелы удаляются.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Значение равно <see langword="null"/>, пустой строке или состоит только из пробелов.
    /// </exception>
    public static SettlementAsset From(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        return new SettlementAsset(value.Trim());
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
