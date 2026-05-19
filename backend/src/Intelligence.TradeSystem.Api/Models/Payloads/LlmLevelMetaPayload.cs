using System.Text.Json.Serialization;

namespace Intelligence.TradeSystem.Api.Models.Payloads;

/// <summary>
/// Метаданные ценового уровня поддержки или сопротивления.
/// Предоставляет LLM контекст происхождения, силы и дистанции уровня.
/// </summary>
public sealed record LlmLevelMetaPayload
{
    /// <summary>Цена уровня. Совпадает с соответствующим плоским полем (<c>support1</c>, <c>resistance1</c> и т.д.).</summary>
    public required decimal Price { get; init; }

    /// <summary>
    /// Нормализованная сила уровня в диапазоне [0, 1].
    /// Отражает относительный объём HVN-кластера, лежащего в основе уровня.
    /// <c>null</c> — источник не поддерживает оценку силы.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? Strength { get; init; }

    /// <summary>
    /// Текстовая метка силы уровня, производная от <see cref="Strength"/>.
    /// Допустимые значения V1: <c>Strong</c>, <c>Moderate</c>, <c>Weak</c>, <c>Unavailable</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StrengthLabel { get; init; }

    /// <summary>
    /// Тип детектора, обнаружившего уровень.
    /// Допустимые значения V1: <c>volume-profile</c>, <c>swing</c>, <c>pivot</c>, <c>liquidity</c>, <c>composite</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Source { get; init; }

    /// <summary>
    /// Расстояние от текущей цены до уровня в процентах.
    /// <c>null</c> — уровень нулевой или текущая цена недоступна.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? DistancePct { get; init; }
}

