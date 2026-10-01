using Intelligence.TradeSystem.Api.Models.Payloads;

namespace Intelligence.TradeSystem.Api.Configuration;

/// <summary>
/// Конфигурация порогов свежести секций снапшота по режимам анализа.
/// </summary>
public sealed record SnapshotFreshnessOptions
{
    /// <summary>Имя секции в <c>appsettings.json</c>.</summary>
    public const string SectionName = "SnapshotFreshness";

    /// <summary>Пороги свежести для режима <see cref="AnalysisMode.Intraday"/>.</summary>
    public required SectionFreshnessOptions Intraday { get; init; }

    /// <summary>Пороги свежести для режима <see cref="AnalysisMode.Swing"/>.</summary>
    public required SectionFreshnessOptions Swing { get; init; }

    /// <summary>Пороги свежести для режима <see cref="AnalysisMode.Portfolio"/>.</summary>
    public required SectionFreshnessOptions Portfolio { get; init; }

    /// <summary>Возвращает настройки свежести для указанного режима.</summary>
    public SectionFreshnessOptions ForMode(AnalysisMode mode) => mode switch
    {
        AnalysisMode.Intraday => Intraday,
        AnalysisMode.Swing => Swing,
        AnalysisMode.Portfolio => Portfolio,
        _ => Intraday,
    };

    /// <summary>
    /// Доля от максимального возраста секции, при достижении которой генерируется мягкое
    /// предупреждение "порог близости к устареванию". Должна быть в диапазоне (0, 1).
    /// По умолчанию <c>0.8</c> — предупреждение появляется при достижении 80% порога.
    /// </summary>
    public decimal StalenessProximityFactor { get; init; } = 0.8m;
}
