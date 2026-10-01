using Intelligence.TradeSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests;

[Collection(EnvironmentVariableTestGroup.Name)]
public sealed class TradeSystemDbContextFactoryConfigurationTests
{
    private const string EnvironmentVariableName = "ConnectionStrings__TradeSystem";

    private const string UnreachableConnectionString =
        "Host=127.0.0.1;Port=1;Database=tradesystem;Username=none;Password=none;Timeout=1";

    [Fact]
    public void Contract_uses_the_business_connection_string_environment_variable()
    {
        Assert.Equal(EnvironmentVariableName, TradeSystemDesignTimeConnectionString.EnvironmentVariableName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Factory_fails_fast_when_connection_string_is_missing_or_blank(string? connectionString)
    {
        using var _ = new EnvironmentVariableScope(EnvironmentVariableName, connectionString);

        var exception = Assert.Throws<InvalidOperationException>(
            () => new TradeSystemDbContextFactory().CreateDbContext([]));

        Assert.Contains(EnvironmentVariableName, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_rejects_missing_or_blank_connection_string(string? connectionString)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => TradeSystemDesignTimeConnectionString.Validate(connectionString));

        Assert.Contains(EnvironmentVariableName, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Factory_rejects_malformed_connection_string_without_disclosing_it()
    {
        const string secret = "trade-system-design-time-secret-marker";
        using var _ = new EnvironmentVariableScope(
            EnvironmentVariableName,
            $"Host=localhost;Database=test;Username=test;Password=safe;Timeout={secret}");

        var exception = Assert.Throws<InvalidOperationException>(
            () => new TradeSystemDbContextFactory().CreateDbContext([]));

        Assert.Contains(EnvironmentVariableName, exception.Message, StringComparison.Ordinal);
        Assert.Contains("некорректн", exception.Message, StringComparison.Ordinal);
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain(secret, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Factory_creates_context_for_valid_connection_string_without_connecting_to_the_database()
    {
        using var _ = new EnvironmentVariableScope(EnvironmentVariableName, UnreachableConnectionString);

        using var context = new TradeSystemDbContextFactory().CreateDbContext([]);

        Assert.Equal(UnreachableConnectionString, context.Database.GetConnectionString());
    }
}
