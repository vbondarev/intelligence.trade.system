namespace Intelligence.TradeSystem.Api.Models.Payloads;

/// <summary>
/// Состояние моментума таймфрейма, вычисленное на основе bias, подтверждения тренда и RSI.
/// </summary>
public enum MomentumState
{
    /// <summary>
    /// Инертное состояние: bias == Neutral (Sideways / Unknown / EMA-конфликт).
    /// </summary>
    Neutral = 0,

    /// <summary>
    /// Здоровый моментум: тренд подтверждён, RSI в рабочей зоне.
    /// Bullish: RSI ∈ [55, 70]; Bearish: RSI ∈ [30, 45].
    /// Инвариант: IsTrendConfirmed == true.
    /// </summary>
    Healthy = 1,

    /// <summary>
    /// Слабый моментум: bias присутствует, но тренд не подтверждён либо RSI вне рабочей зоны.
    /// </summary>
    Weak = 2,

    /// <summary>
    /// Перегрев: RSI вышел за экстремальные значения.
    /// Bullish: RSI &gt; 70 или rsiOverbought; Bearish: RSI &lt; 30 или rsiOversold.
    /// </summary>
    Overextended = 3,
}

