namespace Intelligence.TradeSystem.Domain.Snapshots;

/// <summary>Направление открытой позиции.</summary>
public enum PositionSide
{
    /// <summary>Направление не определено.</summary>
    Unknown = 0,

    /// <summary>Длинная позиция (ставка на рост цены).</summary>
    Long = 1,

    /// <summary>Короткая позиция (ставка на падение цены).</summary>
    Short = 2
}
