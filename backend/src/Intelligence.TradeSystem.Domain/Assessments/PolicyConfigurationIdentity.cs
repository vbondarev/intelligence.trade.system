namespace Intelligence.TradeSystem.Domain.Assessments;

/// <summary>
/// Неизменяемая идентичность конфигурации политики, использованной при оценке.
/// </summary>
public readonly record struct PolicyConfigurationIdentity
{
    /// <summary>Стабильная версия конфигурации.</summary>
    public string Version { get; }

    /// <summary>Стабильный hash конфигурации.</summary>
    public string Hash { get; }

    /// <summary>
    /// Создаёт идентичность конфигурации и отклоняет пустые значения.
    /// </summary>
    public PolicyConfigurationIdentity(string version, string hash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);

        Version = version.Trim();
        Hash = hash.Trim();
    }

    /// <summary>Создаёт идентичность конфигурации из версии и hash.</summary>
    public static PolicyConfigurationIdentity From(string version, string hash) => new(version, hash);

    /// <summary>
    /// Идентичность, используемая только legacy API создания оценки до появления policy identity.
    /// </summary>
    public static PolicyConfigurationIdentity Legacy => new("legacy", "legacy");

    /// <inheritdoc />
    public override string ToString() => $"{Version}:{Hash}";
}
