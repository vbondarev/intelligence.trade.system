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
}

