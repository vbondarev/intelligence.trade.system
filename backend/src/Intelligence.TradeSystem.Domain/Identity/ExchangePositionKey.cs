using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Domain.Identity;

/// <summary>
/// Ключ сопоставления наблюдаемой на бирже позиции с бизнес-позицией системы.
/// </summary>
/// <remarks>
/// Это не замена <see cref="PositionId"/>. <see cref="PositionId"/> — идентичность сделки
/// внутри Intelligence.TradeSystem, устойчивая к повторному открытию позиции.
/// <see cref="ExchangePositionKey"/> — снимок координат позиции на бирже (аккаунт,
/// инструмент, направление и <c>positionIdx</c>), используемый только для сопоставления
/// с текущим состоянием биржи. Два ключа равны только при совпадении всех компонентов.
/// </remarks>
public readonly record struct ExchangePositionKey
{
    /// <summary>Биржевой аккаунт, на котором наблюдается позиция.</summary>
    public ExchangeAccountId ExchangeAccountId { get; }

    /// <summary>Торговый инструмент.</summary>
    public InstrumentId InstrumentId { get; }

    /// <summary>Направление позиции.</summary>
    public PositionSide PositionSide { get; }

    /// <summary>
    /// Индекс позиции на бирже (например, для поддержки hedge mode на Bybit).
    /// Не может быть отрицательным.
    /// </summary>
    public int PositionIdx { get; }

    private ExchangePositionKey(
        ExchangeAccountId exchangeAccountId,
        InstrumentId instrumentId,
        PositionSide positionSide,
        int positionIdx)
    {
        ExchangeAccountId = exchangeAccountId;
        InstrumentId = instrumentId;
        PositionSide = positionSide;
        PositionIdx = positionIdx;
    }

    /// <summary>
    /// Создаёт ключ сопоставления позиции с биржей.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="positionIdx"/> отрицателен.</exception>
    public static ExchangePositionKey Create(
        ExchangeAccountId exchangeAccountId,
        InstrumentId instrumentId,
        PositionSide positionSide,
        int positionIdx)
    {
        if (positionIdx < 0)
            throw new ArgumentOutOfRangeException(
                nameof(positionIdx), positionIdx, "PositionIdx cannot be negative.");

        return new ExchangePositionKey(exchangeAccountId, instrumentId, positionSide, positionIdx);
    }

    /// <inheritdoc />
    public override string ToString() =>
        $"{ExchangeAccountId}/{InstrumentId}/{PositionSide}/{PositionIdx}";
}
