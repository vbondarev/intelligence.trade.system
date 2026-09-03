using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Domain.Identity;

/// <summary>
/// Ключ сопоставления наблюдаемой на бирже позиции с бизнес-позицией системы.
/// </summary>
/// <remarks>
/// Это не замена <see cref="PositionId"/>. <see cref="PositionId"/> — внутренний
/// идентификатор одного жизненного цикла позиции: он не переиспользуется при её повторном
/// открытии. <see cref="ExchangePositionKey"/> — ключ для сопоставления текущей наблюдаемой
/// на бирже позиции (аккаунт, инструмент, направление и <c>positionIdx</c>) с бизнес-позицией
/// системы. Совпадающий <see cref="ExchangePositionKey"/> после закрытия и последующего
/// повторного открытия позиции не означает совпадающий <see cref="PositionId"/>.
/// Два ключа равны только при совпадении всех компонентов.
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
    /// <exception cref="ArgumentException">
    /// <paramref name="exchangeAccountId"/> или <paramref name="instrumentId"/> не инициализированы
    /// (переданы значением по умолчанию для соответствующего typed identifier).
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="positionSide"/> равен <see cref="PositionSide.Unknown"/> или не является
    /// определённым значением <see cref="PositionSide"/>, либо <paramref name="positionIdx"/> отрицателен.
    /// </exception>
    public static ExchangePositionKey Create(
        ExchangeAccountId exchangeAccountId,
        InstrumentId instrumentId,
        PositionSide positionSide,
        int positionIdx)
    {
        if (exchangeAccountId == default)
            throw new ArgumentException(
                "ExchangeAccountId must be initialized.", nameof(exchangeAccountId));

        if (instrumentId.Value is null)
            throw new ArgumentException(
                "InstrumentId must be initialized.", nameof(instrumentId));

        if (positionSide is not (PositionSide.Long or PositionSide.Short))
            throw new ArgumentOutOfRangeException(
                nameof(positionSide), positionSide, "PositionSide must be either Long or Short.");

        if (positionIdx < 0)
            throw new ArgumentOutOfRangeException(
                nameof(positionIdx), positionIdx, "PositionIdx cannot be negative.");

        return new ExchangePositionKey(exchangeAccountId, instrumentId, positionSide, positionIdx);
    }

    /// <inheritdoc />
    public override string ToString() =>
        $"{ExchangeAccountId}/{InstrumentId}/{PositionSide}/{PositionIdx}";
}
