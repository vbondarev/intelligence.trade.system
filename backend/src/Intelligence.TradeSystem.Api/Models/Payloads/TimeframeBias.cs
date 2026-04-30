namespace Intelligence.TradeSystem.Api.Models.Payloads;

/// <summary>
/// Направленное смещение (bias) по таймфрейму, вычисленное на основе тренда и EMA-alignment.
/// </summary>
public enum TimeframeBias
{
    /// <summary>Бычий bias: тренд восходящий и EMA выстроены по-бычьи.</summary>
    Bullish = 1,

    /// <summary>Медвежий bias: тренд нисходящий и EMA выстроены по-медвежьи.</summary>
    Bearish = 2,

    /// <summary>Нейтральный bias: тренд боковой, неизвестный или конфликтует с EMA-alignment.</summary>
    Neutral = 3,
}

