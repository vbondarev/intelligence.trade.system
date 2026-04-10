using System.Diagnostics.CodeAnalysis;

namespace Intelligence.TradeSystem.Domain.Snapshots;

/// <summary>Направление открытой позиции.</summary>
public enum PositionSide
{
    /// <summary>Направление не определено.</summary>
    Unknown = 0,

    /// <summary>Длинная позиция (ставка на рост цены).</summary>
    [SuppressMessage(
        "Naming",
        "CA1720:Identifiers should not contain type names",
        Justification = "Long является устоявшимся финансовым термином предметной области трейдинга.")]
    Long = 1,

    /// <summary>Короткая позиция (ставка на падение цены).</summary>
    [SuppressMessage(
        "Naming",
        "CA1720:Identifiers should not contain type names",
        Justification = "Short является устоявшимся финансовым термином предметной области трейдинга.")]
    Short = 2
}
