namespace Intelligence.TradeSystem.Api.Models.Payloads;

/// <summary>
/// Режим анализа, определяющий набор первичных таймфреймов и пороги свежести секций снапшота.
/// </summary>
public enum AnalysisMode
{
    /// <summary>Краткосрочная торговля. Первичные таймфреймы: 15m, 1h, 4h.</summary>
    Intraday = 1,

    /// <summary>Среднесрочная торговля. Первичные таймфреймы: 1h, 4h, 1d.</summary>
    Swing = 2,

    /// <summary>Портфельный анализ с позициями. Первичные таймфреймы: 4h, 1d.</summary>
    Portfolio = 3,
}
