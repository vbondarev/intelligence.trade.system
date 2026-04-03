namespace Intelligence.TradeSystem.Domain.Snapshots;

/// <summary>Один ценовой уровень в стакане заявок.</summary>
public sealed record OrderBookLevel
{
    /// <summary>Цена уровня.</summary>
    public decimal Price { get; init; }

    /// <summary>Суммарный объём заявок на данном ценовом уровне.</summary>
    public decimal Size { get; init; }
}
