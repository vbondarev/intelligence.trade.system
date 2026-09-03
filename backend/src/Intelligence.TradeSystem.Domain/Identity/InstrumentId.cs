namespace Intelligence.TradeSystem.Domain.Identity;

/// <summary>
/// Строго типизированный идентификатор торгового инструмента внутри домена (например,
/// <c>BTCUSDT</c>). Не содержит знаний о конкретной бирже и не выполняет
/// биржеспецифичную нормализацию символа.
/// </summary>
public readonly record struct InstrumentId
{
    /// <summary>Значение идентификатора инструмента.</summary>
    public string Value { get; }

    private InstrumentId(string value) => Value = value;

    /// <summary>
    /// Создаёт идентификатор инструмента из строки. Внешние пробелы удаляются.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Значение равно <see langword="null"/>, пустой строке или состоит только из пробелов.
    /// </exception>
    public static InstrumentId From(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        return new InstrumentId(value.Trim());
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
