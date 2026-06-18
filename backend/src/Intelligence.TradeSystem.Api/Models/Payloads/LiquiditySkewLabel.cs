namespace Intelligence.TradeSystem.Api.Models.Payloads;

/// <summary>
/// Метка перекоса ликвидности, вычисленная из соотношения объёмов bid/ask Top20.
/// </summary>
public enum LiquiditySkewLabel
{
    /// <summary>Ликвидность сбалансирована: ratio в диапазоне (0.85, 1.15).</summary>
    Balanced = 1,

    /// <summary>Преобладает ликвидность снизу (bid heavy): ratio &gt;= 1.15.</summary>
    LowerLiquidityHeavy = 2,

    /// <summary>Преобладает ликвидность сверху (ask heavy): ratio &lt;= 0.85.</summary>
    UpperLiquidityHeavy = 3,
}
