using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Analysis.Assemblers;

/// <summary>
/// Финальный оркестратор слоя Indicators.
/// Собирает корневой <see cref="MarketAnalysisSnapshot"/> из уже вычисленных частичных снапшотов,
/// не выполняя никаких повторных расчётов.
/// <para>
/// Порядок преобразований:
/// <list type="number">
///   <item>Валидация всех обязательных параметров</item>
///   <item>Нормализация <see cref="MarketCategory"/> в строковую форму категории</item>
///   <item>Построение тегов классификации из уже собранных данных</item>
///   <item>Сборка корневого снимка с фиксацией времени сборки (<c>DateTimeOffset.UtcNow</c>)</item>
/// </list>
/// </para>
/// </summary>
public static class MarketAnalysisSnapshotAssembler
{
    /// <summary>
    /// Порог абсолютного значения ставки финансирования, при котором выставляется тег <c>funding-spike</c>.
    /// Совпадает с <see cref="AnalysisThresholds.FundingExtremeThreshold"/>.
    /// </summary>
    private const decimal FundingSpikeThreshold = AnalysisThresholds.FundingExtremeThreshold;

    /// <summary>
    /// Минимальный абсолютный дисбаланс стакана на глубине Top-5,
    /// при превышении которого выставляется тег <c>bid-pressure</c> или <c>ask-pressure</c>.
    /// </summary>
    private const decimal OrderBookPressureThreshold = 0.3m;

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Вычисляет и возвращает <see cref="MarketAnalysisSnapshot"/> из набора готовых снапшотов.
    /// </summary>
    /// <param name="exchange">Название биржи. Например: <c>Bybit</c>.</param>
    /// <param name="symbol">Тикер инструмента. Например: <c>BTCUSDT</c>.</param>
    /// <param name="category">Категория рынка инструмента.</param>
    /// <param name="price">Снапшот текущей цены.</param>
    /// <param name="derivatives">Снапшот деривативных данных.</param>
    /// <param name="orderBook">Снапшот стакана заявок.</param>
    /// <param name="tradeFlow">Снапшот потока совершённых сделок.</param>
    /// <param name="m15">Технический анализ на таймфрейме 15 минут.</param>
    /// <param name="h1">Технический анализ на таймфрейме 1 час.</param>
    /// <param name="h4">Технический анализ на таймфрейме 4 часа.</param>
    /// <param name="d1">Технический анализ на дневном таймфрейме.</param>
    /// <param name="sentiment">Агрегированный снапшот сентимента.</param>
    /// <param name="portfolio">Снапшот торгового счёта и открытых позиций.</param>
    /// <exception cref="ArgumentException">
    /// Если <paramref name="exchange"/> или <paramref name="symbol"/> пустые или состоят из пробелов.
    /// </exception>
    /// <exception cref="ArgumentNullException">Если любой из снапшотов равен <c>null</c>.</exception>
    public static MarketAnalysisSnapshot Assemble(
        string exchange,
        string symbol,
        MarketCategory category,
        PriceSnapshot price,
        DerivativesSnapshot derivatives,
        OrderBookSnapshot orderBook,
        TradeFlowSnapshot tradeFlow,
        TimeframeAnalysisSnapshot m15,
        TimeframeAnalysisSnapshot h1,
        TimeframeAnalysisSnapshot h4,
        TimeframeAnalysisSnapshot d1,
        SentimentSnapshot sentiment,
        PortfolioSnapshot portfolio)
    {
        // 1. Validate
        ArgumentException.ThrowIfNullOrWhiteSpace(exchange);
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentNullException.ThrowIfNull(price);
        ArgumentNullException.ThrowIfNull(derivatives);
        ArgumentNullException.ThrowIfNull(orderBook);
        ArgumentNullException.ThrowIfNull(tradeFlow);
        ArgumentNullException.ThrowIfNull(m15);
        ArgumentNullException.ThrowIfNull(h1);
        ArgumentNullException.ThrowIfNull(h4);
        ArgumentNullException.ThrowIfNull(d1);
        ArgumentNullException.ThrowIfNull(sentiment);
        ArgumentNullException.ThrowIfNull(portfolio);

        // 2. Category string — lowercase to match API conventions: "linear", "spot", "inverse"
        var categoryString = category.ToString().ToLowerInvariant();

        // 3. Tags — derived from already-assembled data; no new calculations
        var tags = BuildTags(derivatives, orderBook, tradeFlow, h1, sentiment);

        // 4. Assemble
        return new MarketAnalysisSnapshot
        {
            Exchange      = exchange,
            Symbol        = symbol,
            Category      = categoryString,
            CapturedAtUtc = DateTimeOffset.UtcNow,

            Price       = price,
            Derivatives = derivatives,
            OrderBook   = orderBook,
            TradeFlow   = tradeFlow,

            M15 = m15,
            H1  = h1,
            H4  = h4,
            D1  = d1,

            Sentiment = sentiment,
            Portfolio = portfolio,

            Tags = tags,
        };
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Формирует набор описательных тегов из уже собранных снапшотов.
    /// Теги предназначены для быстрой классификации снимка downstream-компонентами
    /// (GPT-форматтер, логирование, кэш-инвалидация).
    /// Никаких новых расчётов не выполняется — только чтение готовых полей.
    /// </summary>
    private static List<string> BuildTags(
        DerivativesSnapshot derivatives,
        OrderBookSnapshot orderBook,
        TradeFlowSnapshot tradeFlow,
        TimeframeAnalysisSnapshot h1,
        SentimentSnapshot sentiment)
    {
        var tags = new List<string>();

        // Market regime tag (kebab-case)
        var regimeTag = ToKebabCase(sentiment.MarketRegime);
        if (!string.IsNullOrEmpty(regimeTag))
        {
            tags.Add(regimeTag);
        }

        // Funding rate signal
        if (Math.Abs(derivatives.FundingRate) >= FundingSpikeThreshold)
        {
            tags.Add("funding-spike");
        }
        else if (derivatives.FundingRate > 0m)
        {
            tags.Add("positive-funding");
        }
        else if (derivatives.FundingRate < 0m)
        {
            tags.Add("negative-funding");
        }

        // Trade flow pressure
        if (tradeFlow.HasAggressiveBuyPressure)
        {
            tags.Add("aggressive-buying");
        }
        else if (tradeFlow.HasAggressiveSellPressure)
        {
            tags.Add("aggressive-selling");
        }

        // RSI extremes on H1 (closest relevant timeframe)
        if (h1.RsiOverbought)
        {
            tags.Add("rsi-overbought");
        }
        else if (h1.RsiOversold)
        {
            tags.Add("rsi-oversold");
        }

        // Order book imbalance (Top-5 — most actionable depth)
        if (orderBook.ImbalanceTop5 > OrderBookPressureThreshold)
        {
            tags.Add("bid-pressure");
        }
        else if (orderBook.ImbalanceTop5 < -OrderBookPressureThreshold)
        {
            tags.Add("ask-pressure");
        }

        return tags;
    }

    /// <summary>
    /// Конвертирует PascalCase строку в kebab-case.
    /// Например: <c>MeanReversion</c> → <c>mean-reversion</c>.
    /// </summary>
    private static string ToKebabCase(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var result = new System.Text.StringBuilder(value.Length + 4);

        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsUpper(value[i]) && i > 0)
            {
                result.Append('-');
            }

            result.Append(char.ToLowerInvariant(value[i]));
        }

        return result.ToString();
    }
}

