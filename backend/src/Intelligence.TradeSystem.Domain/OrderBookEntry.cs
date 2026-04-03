namespace Intelligence.TradeSystem.Domain;

/// <summary>
/// Один ценовой уровень стакана заявок — сырые данные с биржи.
/// </summary>
public sealed record OrderBookEntry(decimal Price, decimal Size)
{
    /// <summary>Цена уровня.</summary>
    public decimal Price { get; init; } = Price;

    /// <summary>Суммарный объём заявок на данном уровне (в базовых единицах/контрактах).</summary>
    public decimal Size { get; init; } = Size;
}

