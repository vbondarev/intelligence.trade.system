namespace Intelligence.TradeSystem.MarketIntelligence.Analysis.Timeframes;

/// <summary>
/// Качество точки входа, вычисленное детерминированно на основе дистанций и RSI-флагов.
/// </summary>
public enum EntryQuality
{
    /// <summary>Хорошая точка входа: тренд подтверждён, цена близко к уровню, RSI не перегрет.</summary>
    Good = 1,

    /// <summary>Приемлемая точка входа условия частично выполнены.</summary>
    Fair = 2,

    /// <summary>Плохая точка входа: bias нейтральный, цена далеко от уровня или RSI перегрет.</summary>
    Poor = 3,
}
