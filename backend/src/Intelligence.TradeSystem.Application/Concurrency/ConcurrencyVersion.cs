namespace Intelligence.TradeSystem.Application.Concurrency;

/// <summary>
/// Персистентно-независимый токен оптимистической конкурентности для мутируемых
/// доменных агрегатов (<see cref="Intelligence.TradeSystem.Domain.ExchangeAccount"/>,
/// <see cref="Intelligence.TradeSystem.Domain.Position"/>,
/// <see cref="Intelligence.TradeSystem.Domain.Recommendations.Recommendation"/>).
/// </summary>
/// <remarks>
/// Application и Domain не хранят версию внутри самого агрегата: она существует только
/// как значение, сопровождающее чтение (<see cref="Versioned{T}"/>) и передаваемое обратно
/// в <c>SaveAsync</c> как ожидаемая версия для CAS-обновления. Инфраструктура определяет,
/// как версия физически хранится (например, отдельная колонка `version`).
/// </remarks>
public readonly record struct ConcurrencyVersion
{
    /// <summary>Числовое значение версии. Всегда строго больше нуля.</summary>
    public long Value { get; }

    /// <summary>
    /// Создаёт версию из существующего значения.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Значение меньше либо равно нулю.</exception>
    public ConcurrencyVersion(long value)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(value), value, "ConcurrencyVersion value must be greater than zero.");

        Value = value;
    }

    /// <summary>Начальная версия успешно сохранённой строки.</summary>
    public static ConcurrencyVersion Initial => new(1);

    /// <summary>Следующая версия после успешного CAS-обновления.</summary>
    public ConcurrencyVersion Next() => new(Value + 1);

    /// <inheritdoc />
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
