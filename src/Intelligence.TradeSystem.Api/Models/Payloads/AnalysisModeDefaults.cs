namespace Intelligence.TradeSystem.Api.Models.Payloads;

/// <summary>
/// Возвращает первичные таймфреймы для каждого режима анализа.
/// </summary>
public static class AnalysisModeDefaults
{
    /// <summary>
    /// Возвращает список первичных таймфреймов для указанного режима анализа.
    /// </summary>
    public static IReadOnlyList<string> GetPrimaryTimeframes(AnalysisMode mode) =>
        mode switch
        {
            AnalysisMode.Intraday => ["15m", "1h", "4h"],
            AnalysisMode.Swing => ["1h", "4h", "1d"],
            AnalysisMode.Portfolio => ["4h", "1d"],
            _ => ["15m", "1h", "4h"],
        };
}
