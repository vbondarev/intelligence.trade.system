namespace Intelligence.TradeSystem.MarketIntelligence.Indicators.Validation;

/// <summary>
/// Результат проверки одной свечи <see cref="Domain.Kline"/> на корректность рыночных данных.
/// </summary>
/// <remarks>
/// Используется <see cref="KlineValidator"/> для сигнализации о конкретном нарушении
/// OHLC-инварианта или неотрицательности объёма/цен.
/// <para>
/// <see cref="IsValid"/> = <see langword="true"/> означает полностью корректную свечу;
/// в этом случае <see cref="ViolationReason"/> равен <c>null</c>.
/// </para>
/// </remarks>
public sealed record KlineValidationResult
{
    /// <summary>Индекс свечи в исходном массиве (0-based).</summary>
    public required int KlineIndex { get; init; }

    /// <summary>
    /// <see langword="true"/> — свеча прошла все проверки.
    /// <see langword="false"/> — нарушен хотя бы один инвариант.
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// Человекочитаемое описание нарушения. <c>null</c> когда <see cref="IsValid"/> = <see langword="true"/>.
    /// </summary>
    public required string? ViolationReason { get; init; }

    /// <summary>Создаёт результат для корректной свечи.</summary>
    public static KlineValidationResult Valid(int index) => new()
    {
        KlineIndex = index,
        IsValid = true,
        ViolationReason = null,
    };

    /// <summary>Создаёт результат для свечи с нарушением.</summary>
    public static KlineValidationResult Invalid(int index, string reason) => new()
    {
        KlineIndex = index,
        IsValid = false,
        ViolationReason = reason,
    };
}
