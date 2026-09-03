namespace Intelligence.TradeSystem.Domain.Identity;

/// <summary>
/// Строго типизированный идентификатор конкретного подключённого биржевого аккаунта пользователя.
/// Это не идентификатор биржи (см. <see cref="ExchangeId"/>) — один пользователь может иметь
/// несколько аккаунтов на одной и той же бирже, и каждый из них получает собственный
/// <see cref="ExchangeAccountId"/>.
/// </summary>
public readonly record struct ExchangeAccountId
{
    /// <summary>Значение идентификатора.</summary>
    public Guid Value { get; }

    private ExchangeAccountId(Guid value) => Value = value;

    /// <summary>Создаёт новый уникальный идентификатор биржевого аккаунта.</summary>
    public static ExchangeAccountId New() => new(Guid.NewGuid());

    /// <summary>
    /// Создаёт идентификатор из существующего <see cref="Guid"/>.
    /// </summary>
    /// <exception cref="ArgumentException">Передан <see cref="Guid.Empty"/>.</exception>
    public static ExchangeAccountId FromGuid(Guid value) => value != Guid.Empty
        ? new ExchangeAccountId(value)
        : throw new ArgumentException("ExchangeAccountId value cannot be Guid.Empty.", nameof(value));

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
