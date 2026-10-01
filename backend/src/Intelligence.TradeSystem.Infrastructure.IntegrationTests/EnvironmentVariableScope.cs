using Xunit;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests;

/// <summary>
/// Временно задаёт environment variable процесса и восстанавливает исходное значение при Dispose.
/// </summary>
/// <remarks>
/// Environment является process-global state, поэтому использующие scope тесты должны входить
/// в <see cref="EnvironmentVariableTestGroup"/>.
/// </remarks>
internal sealed class EnvironmentVariableScope : IDisposable
{
    private readonly string name;
    private readonly string? originalValue;

    public EnvironmentVariableScope(string name, string? value)
    {
        this.name = name;
        originalValue = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    public void Dispose() => Environment.SetEnvironmentVariable(name, originalValue);
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class EnvironmentVariableTestGroup
{
    public const string Name = "EnvironmentVariables";
}
