namespace Intelligence.TradeSystem.Domain.Identity;

/// <summary>
/// Строго типизированный идентификатор пользователя системы.
/// Исключает случайную подмену идентификатором другой сущности на уровне типов.
/// </summary>
public readonly record struct UserId
{
    /// <summary>Значение идентификатора.</summary>
    public Guid Value { get; }

    private UserId(Guid value) => Value = value;

    /// <summary>Создаёт новый уникальный идентификатор пользователя.</summary>
    public static UserId New() => new(Guid.NewGuid());

    /// <summary>
    /// Создаёт идентификатор из существующего <see cref="Guid"/>.
    /// </summary>
    /// <exception cref="ArgumentException">Передан <see cref="Guid.Empty"/>.</exception>
    public static UserId FromGuid(Guid value) => value != Guid.Empty
        ? new UserId(value)
        : throw new ArgumentException("UserId value cannot be Guid.Empty.", nameof(value));

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
