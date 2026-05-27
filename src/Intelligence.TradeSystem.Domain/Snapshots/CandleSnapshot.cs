namespace Intelligence.TradeSystem.Domain.Snapshots;

/// <summary>OHLCV-данные одной свечи (candlestick).</summary>
public sealed record CandleSnapshot
{
    /// <summary>Время открытия свечи (UTC).</summary>
    public DateTimeOffset OpenTimeUtc { get; init; }

    /// <summary>Цена открытия свечи.</summary>
    public decimal Open { get; init; }

    /// <summary>Максимальная цена за период свечи.</summary>
    public decimal High { get; init; }

    /// <summary>Минимальная цена за период свечи.</summary>
    public decimal Low { get; init; }

    /// <summary>Цена закрытия свечи.</summary>
    public decimal Close { get; init; }

    /// <summary>Торговый объём за период свечи (в базовой валюте/контрактах).</summary>
    public decimal Volume { get; init; }

    /// <summary>Оборот за период свечи (в котируемой валюте, обычно USDT).</summary>
    public decimal Turnover { get; init; }
}
