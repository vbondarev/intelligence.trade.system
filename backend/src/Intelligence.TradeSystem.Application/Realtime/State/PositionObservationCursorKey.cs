using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Realtime.State;

/// <summary>
/// Provider identity позиции, в пределах которой упорядочиваются частичные потоковые наблюдения.
/// </summary>
/// <remarks>
/// Направление позиции в ключ не входит: в one-way mode Bybit (<c>positionIdx = 0</c>) направление
/// меняется в пределах одной последовательности <c>seq</c> символа, а сообщение о нулевом размере
/// приходит без направления. В hedge mode направление однозначно задаётся <see cref="PositionIdx"/>.
/// Ключ не заменяет <see cref="ExchangePositionKey"/> и не идентифицирует жизненный цикл позиции.
/// </remarks>
public readonly record struct PositionObservationCursorKey
{
    public const int SymbolMaxLength = 128;

    public ExchangeAccountId ExchangeAccountId { get; }

    public MarketCategory Category { get; }

    public InstrumentId Symbol { get; }

    public int PositionIdx { get; }

    private PositionObservationCursorKey(
        ExchangeAccountId exchangeAccountId,
        MarketCategory category,
        InstrumentId symbol,
        int positionIdx)
    {
        ExchangeAccountId = exchangeAccountId;
        Category = category;
        Symbol = symbol;
        PositionIdx = positionIdx;
    }

    /// <exception cref="ArgumentException">Идентификаторы не инициализированы или символ слишком длинный.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Неизвестная категория или отрицательный <paramref name="positionIdx"/>.</exception>
    public static PositionObservationCursorKey Create(
        ExchangeAccountId exchangeAccountId,
        MarketCategory category,
        InstrumentId symbol,
        int positionIdx)
    {
        if (exchangeAccountId == default)
        {
            throw new ArgumentException("ExchangeAccountId must be initialized.", nameof(exchangeAccountId));
        }

        if (!Enum.IsDefined(category))
        {
            throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown market category.");
        }

        if (symbol.Value is null)
        {
            throw new ArgumentException("Symbol must be initialized.", nameof(symbol));
        }

        if (symbol.Value.Length > SymbolMaxLength)
        {
            throw new ArgumentException("Symbol is too long.", nameof(symbol));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(positionIdx);

        return new PositionObservationCursorKey(exchangeAccountId, category, symbol, positionIdx);
    }

    /// <inheritdoc />
    public override string ToString() => $"{ExchangeAccountId}/{Category}/{Symbol}/{PositionIdx}";
}
