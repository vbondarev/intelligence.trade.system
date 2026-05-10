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
        AnalysisMode.Intraday  => Intraday,
        AnalysisMode.Swing     => Swing,
        AnalysisMode.Portfolio => Portfolio,
        _                      => Intraday,
    };

    /// <summary>
    /// Доля от максимального возраста секции, при достижении которой генерируется мягкое
    /// предупреждение "near staleness threshold". Должна быть в диапазоне (0, 1).
    /// По умолчанию <c>0.8</c> — предупреждение появляется при достижении 80% порога.
    /// </summary>
    public decimal StalenessProximityFactor { get; init; } = 0.8m;

    /// <summary>Возвращает экземпляр с значениями по умолчанию.</summary>
    public static SnapshotFreshnessOptions Default => new()
    {
        Intraday = new SectionFreshnessOptions
        {
            PriceMaxAge       = TimeSpan.FromSeconds(2),
            DerivativesMaxAge = TimeSpan.FromSeconds(30),
            OrderBookMaxAge   = TimeSpan.FromSeconds(2),
            TradeFlowMaxAge   = TimeSpan.FromSeconds(5),
            M15MaxAge         = TimeSpan.FromSeconds(60),
            H1MaxAge          = TimeSpan.FromSeconds(60),
            H4MaxAge          = TimeSpan.FromSeconds(60),
            D1MaxAge          = TimeSpan.FromSeconds(60),
        },
        Swing = new SectionFreshnessOptions
        {
            PriceMaxAge       = TimeSpan.FromSeconds(10),
            DerivativesMaxAge = TimeSpan.FromMinutes(2),
            OrderBookMaxAge   = TimeSpan.FromSeconds(15),
            TradeFlowMaxAge   = TimeSpan.FromSeconds(30),
            M15MaxAge         = TimeSpan.FromMinutes(5),
            H1MaxAge          = TimeSpan.FromMinutes(5),
            H4MaxAge          = TimeSpan.FromMinutes(5),
            D1MaxAge          = TimeSpan.FromMinutes(5),
        },
        Portfolio = new SectionFreshnessOptions
        {
            PriceMaxAge       = TimeSpan.FromSeconds(5),
            DerivativesMaxAge = TimeSpan.FromMinutes(1),
            OrderBookMaxAge   = TimeSpan.FromSeconds(5),
            TradeFlowMaxAge   = TimeSpan.FromSeconds(10),
            M15MaxAge         = TimeSpan.FromMinutes(5),
            H1MaxAge          = TimeSpan.FromMinutes(5),
            H4MaxAge          = TimeSpan.FromMinutes(5),
            D1MaxAge          = TimeSpan.FromMinutes(5),
            PortfolioMaxAge   = TimeSpan.FromSeconds(30),
        },
    };
}
