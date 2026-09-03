namespace Intelligence.TradeSystem.Domain.Identity;

/// <summary>
/// Строго типизированный идентификатор жизненного цикла конкретной сделки внутри
/// Intelligence.TradeSystem.
/// </summary>
/// <remarks>
/// <see cref="PositionId"/> идентифицирует один жизненный цикл позиции и не
/// переиспользуется при её повторном открытии. Он не является функцией от символа,
/// стороны или <c>positionIdx</c>. Если позиция была закрыта, а затем на том же
/// инструменте и с той же стороной снова открыта новая позиция — это два разных
/// <see cref="PositionId"/>, даже если ключ сопоставления с биржей (см.
/// <see cref="ExchangePositionKey"/>) выглядит одинаково.
/// </remarks>
public readonly record struct PositionId
{
    /// <summary>Значение идентификатора.</summary>
    public Guid Value { get; }

    private PositionId(Guid value) => Value = value;

    /// <summary>Создаёт новый уникальный идентификатор позиции.</summary>
    public static PositionId New() => new(Guid.NewGuid());

    /// <summary>
    /// Создаёт идентификатор из существующего <see cref="Guid"/>.
    /// </summary>
    /// <exception cref="ArgumentException">Передан <see cref="Guid.Empty"/>.</exception>
    public static PositionId FromGuid(Guid value) => value != Guid.Empty
        ? new PositionId(value)
        : throw new ArgumentException("PositionId value cannot be Guid.Empty.", nameof(value));

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
