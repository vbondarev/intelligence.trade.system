namespace Intelligence.TradeSystem.Api.Models.Payloads;

/// <summary>
/// Метка давления стакана заявок, вычисленная из <c>imbalanceTop10</c>.
/// Порог: ±15% (<c>|imbalanceTop10| &gt; 0.15</c>).
/// </summary>
public enum PressureLabel
{
    /// <summary>Давление сбалансировано: <c>|imbalanceTop10| &lt;= 0.15</c>.</summary>
    Balanced = 0,

    /// <summary>Доминирование покупателей: <c>imbalanceTop10 &gt; 0.15</c>.</summary>
    BidDominant = 1,

    /// <summary>Доминирование продавцов: <c>imbalanceTop10 &lt; -0.15</c>.</summary>
    AskDominant = 2,
}

