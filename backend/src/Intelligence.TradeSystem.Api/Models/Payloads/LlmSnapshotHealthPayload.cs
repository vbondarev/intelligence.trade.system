namespace Intelligence.TradeSystem.Api.Models.Payloads;

/// <summary>Оценка свежести и полноты снапшота.</summary>
public sealed record LlmSnapshotHealthPayload
{
    /// <summary><c>true</c>, если все обязательные секции моложе допустимых порогов для текущего режима.</summary>
    public required bool IsFresh { get; init; }

    /// <summary><c>true</c>, если одна или несколько обязательных секций отсутствуют.</summary>
    public required bool IsPartial { get; init; }

    /// <summary>Список предупреждений о свежести или корректности данных.</summary>
    public required IReadOnlyList<string> Warnings { get; init; }

    /// <summary>Список отсутствующих обязательных секций. <c>null</c> если все секции присутствуют.</summary>
    public IReadOnlyList<string>? MissingSections { get; init; }

    /// <summary>Возраст каждой секции в миллисекундах на момент формирования снапшота.</summary>
    public IReadOnlyDictionary<string, long>? SectionAgesMs { get; init; }
}
