namespace Intelligence.TradeSystem.Analysis;

/// <summary>
/// Общие эвристические пороги, используемые несколькими компонентами слоя Analysis.
/// Вынесены в единый источник, чтобы исключить рассинхронизацию между ассемблерами.
/// </summary>
internal static class AnalysisThresholds
{
    /// <summary>
    /// Порог абсолютного значения ставки финансирования, который одновременно используется как:
    /// <list type="bullet">
    /// <item><description>граница экстремального funding в <c>FundingRateSnapshotAssembler</c>;</description></item>
    /// <item><description>точка насыщения funding-bias score до ±1 в <c>SentimentSnapshotAssembler</c>;</description></item>
    /// <item><description>порог тега <c>funding-spike</c> в <c>MarketAnalysisSnapshotAssembler</c>.</description></item>
    /// </list>
    /// </summary>
    internal const decimal FundingExtremeThreshold = 0.001m;

    /// <summary>
    /// Доля невалидных свечей (от общего числа входных), при превышении которой
    /// добавляется диагностика <c>kline.highViolationRate</c>.
    /// Например, 0.20 означает: если отфильтровано более 20 % свечей — данные деградированы.
    /// </summary>
    internal const decimal KlineHighViolationRateThreshold = 0.20m;

    /// <summary>
    /// Минимальное количество валидных свечей, при котором снапшот считается
    /// достаточным для построения без диагностики <c>kline.insufficientData</c>.
    /// Если после фильтрации осталось меньше этого значения (но больше 0),
    /// добавляется предупреждающая диагностика.
    /// </summary>
    internal const int KlineMinimumUsableCount = 2;
}

