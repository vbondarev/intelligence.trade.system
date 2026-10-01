using Microsoft.Extensions.Options;

namespace Intelligence.TradeSystem.Api.Configuration;

/// <summary>
/// Проверяет обязательную конфигурацию <see cref="SnapshotFreshnessOptions"/> при startup.
/// </summary>
/// <remarks>
/// C# <c>required</c> не защищает configuration binding: отсутствующие в конфигурации
/// секции и пороги остаются <see langword="null"/> или <see cref="TimeSpan.Zero"/>,
/// поэтому инварианты проверяются здесь явно.
/// </remarks>
internal sealed class SnapshotFreshnessOptionsValidator : IValidateOptions<SnapshotFreshnessOptions>
{
    public ValidateOptionsResult Validate(string? name, SnapshotFreshnessOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        ValidateSection(nameof(SnapshotFreshnessOptions.Intraday), options.Intraday, failures);
        ValidateSection(nameof(SnapshotFreshnessOptions.Swing), options.Swing, failures);
        ValidateSection(nameof(SnapshotFreshnessOptions.Portfolio), options.Portfolio, failures);

        if (options.StalenessProximityFactor <= 0m || options.StalenessProximityFactor >= 1m)
        {
            failures.Add(
                $"{SnapshotFreshnessOptions.SectionName}:{nameof(SnapshotFreshnessOptions.StalenessProximityFactor)} " +
                "должен быть в диапазоне (0, 1).");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateSection(
        string sectionName,
        SectionFreshnessOptions? section,
        List<string> failures)
    {
        var sectionPath = $"{SnapshotFreshnessOptions.SectionName}:{sectionName}";

        if (section is null)
        {
            failures.Add($"Секция {sectionPath} обязательна.");
            return;
        }

        (string Name, TimeSpan Value)[] requiredThresholds =
        [
            (nameof(SectionFreshnessOptions.PriceMaxAge), section.PriceMaxAge),
            (nameof(SectionFreshnessOptions.DerivativesMaxAge), section.DerivativesMaxAge),
            (nameof(SectionFreshnessOptions.OrderBookMaxAge), section.OrderBookMaxAge),
            (nameof(SectionFreshnessOptions.TradeFlowMaxAge), section.TradeFlowMaxAge),
            (nameof(SectionFreshnessOptions.M15MaxAge), section.M15MaxAge),
            (nameof(SectionFreshnessOptions.H1MaxAge), section.H1MaxAge),
            (nameof(SectionFreshnessOptions.H4MaxAge), section.H4MaxAge),
            (nameof(SectionFreshnessOptions.D1MaxAge), section.D1MaxAge),
        ];

        foreach (var (thresholdName, value) in requiredThresholds)
        {
            if (value <= TimeSpan.Zero)
            {
                failures.Add($"{sectionPath}:{thresholdName} должен быть больше нуля.");
            }
        }

        (string Name, TimeSpan? Value)[] optionalThresholds =
        [
            (nameof(SectionFreshnessOptions.PortfolioMaxAge), section.PortfolioMaxAge),
            (nameof(SectionFreshnessOptions.AggregatedContextMaxAge), section.AggregatedContextMaxAge),
        ];

        foreach (var (thresholdName, value) in optionalThresholds)
        {
            if (value is { } configured && configured <= TimeSpan.Zero)
            {
                failures.Add($"{sectionPath}:{thresholdName}, если задан, должен быть больше нуля.");
            }
        }
    }
}
