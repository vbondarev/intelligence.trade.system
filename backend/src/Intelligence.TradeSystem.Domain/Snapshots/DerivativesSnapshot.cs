namespace Intelligence.TradeSystem.Domain.Snapshots;

/// <summary>
/// Снимок данных деривативного рынка (бессрочные/фьючерсные контракты).
/// Содержит ставку финансирования, открытый интерес, соотношение лонг/шорт
/// и динамику их изменений — ключевые сигналы для оценки позиционирования участников.
/// </summary>
public sealed record DerivativesSnapshot
{
    /// <summary>
    /// Текущая ставка финансирования (funding rate).
    /// Положительное значение означает, что лонги платят шортам; отрицательное — наоборот.
    /// </summary>
    public decimal FundingRate { get; init; }

    /// <summary>
    /// Время следующего начисления ставки финансирования (UTC).
    /// <c>null</c>, если данные недоступны.
    /// </summary>
    public DateTimeOffset? NextFundingTimeUtc { get; init; }

    /// <summary>
    /// Открытый интерес — суммарный объём незакрытых контрактов (в базовых единицах).
    /// Отражает активность участников рынка.
    /// </summary>
    public decimal OpenInterest { get; init; }

    /// <summary>
    /// Стоимость открытого интереса в USD.
    /// Удобна для сравнения между инструментами с разным номиналом контракта.
    /// </summary>
    public decimal OpenInterestValue { get; init; }

    /// <summary>
    /// Доля лонг-позиций среди всех участников в диапазоне [0, 1].
    /// Значение выше 0.5 указывает на преобладание бычьего позиционирования.
    /// </summary>
    public decimal LongRatio { get; init; }

    /// <summary>
    /// Доля шорт-позиций среди всех участников в диапазоне [0, 1].
    /// Значение выше 0.5 указывает на преобладание медвежьего позиционирования.
    /// </summary>
    public decimal ShortRatio { get; init; }

    /// <summary>
    /// Премия mark-цены над индексной ценой в процентах.
    /// Положительная — контракт торгуется с премией (бычий сигнал),
    /// отрицательная — с дисконтом (медвежий сигнал).
    /// <c>null</c>, если данные недоступны.
    /// </summary>
    public decimal? PremiumVsIndexPct { get; init; }

    /// <summary>Изменение открытого интереса за последний 1 час в процентах.</summary>
    public decimal OpenInterestChange1hPct { get; init; }

    /// <summary>Изменение открытого интереса за последние 4 часа в процентах.</summary>
    public decimal OpenInterestChange4hPct { get; init; }

    /// <summary>
    /// Средняя ставка финансирования за последние 24 часа.
    /// Сглаживает краткосрочные всплески и отражает устойчивый сентимент рынка.
    /// </summary>
    public decimal FundingRateAvg24h { get; init; }
}
