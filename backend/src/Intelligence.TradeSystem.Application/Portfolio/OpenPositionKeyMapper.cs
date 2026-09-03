using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Application.Portfolio;

/// <summary>
/// Сопоставляет сырые <see cref="OpenPosition"/>, полученные с биржи, с ключом
/// <see cref="ExchangePositionKey"/> бизнес-позиции. Не является частью Domain, поскольку
/// Domain не должен зависеть от <see cref="OpenPosition"/>.
/// </summary>
internal static class OpenPositionKeyMapper
{
    /// <summary>
    /// Пытается построить <see cref="ExchangePositionKey"/> из наблюдаемой открытой позиции.
    /// Возвращает <see langword="false"/>, если позицию нельзя безопасно сопоставить
    /// (неизвестная сторона, некорректный символ или отрицательный <c>positionIdx</c>).
    /// </summary>
    public static bool TryMapKey(
        OpenPosition position,
        ExchangeAccountId exchangeAccountId,
        out ExchangePositionKey key,
        out string? warning)
    {
        key = default;

        if (string.IsNullOrWhiteSpace(position.Symbol))
        {
            warning = "Skipped an observed position with a missing symbol.";
            return false;
        }

        if (position.Side is not (PositionSide.Long or PositionSide.Short))
        {
            warning = $"Skipped {position.Symbol} ({position.Category}): unsupported position side '{position.Side}'.";
            return false;
        }

        if (position.PositionIdx < 0)
        {
            warning = $"Skipped {position.Symbol} ({position.Category}): negative PositionIdx {position.PositionIdx}.";
            return false;
        }

        try
        {
            key = ExchangePositionKey.Create(
                exchangeAccountId, InstrumentId.From(position.Symbol), position.Side, position.PositionIdx);
            warning = null;
            return true;
        }
        catch (ArgumentException ex)
        {
            warning = $"Skipped {position.Symbol} ({position.Category}): {ex.Message}";
            return false;
        }
    }
}
