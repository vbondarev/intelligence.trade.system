using FluentAssertions;
using Intelligence.TradeSystem.Identity.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Intelligence.TradeSystem.Authentication.IntegrationTests;

[Collection(EnvironmentVariableTestGroup.Name)]
public sealed class IdentityDesignTimeConfigurationTests
{
    private const string EnvironmentVariableName = "ConnectionStrings__TradeSystemIdentity";

    private const string UnreachableConnectionString =
        "Host=127.0.0.1;Port=1;Database=tradesystem_identity;Username=none;Password=none;Timeout=1";

    [Fact]
    public void Contract_uses_the_identity_connection_string_environment_variable()
    {
        IdentityDesignTimeConnectionString.EnvironmentVariableName.Should().Be(EnvironmentVariableName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Factory_fails_fast_when_connection_string_is_missing_or_blank(string? connectionString)
    {
        using var _ = new EnvironmentVariableScope(EnvironmentVariableName, connectionString);

        var act = () => new IdentityDbContextFactory().CreateDbContext([]);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*{EnvironmentVariableName}*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_rejects_missing_or_blank_connection_string(string? connectionString)
    {
        var act = () => IdentityDesignTimeConnectionString.Validate(connectionString);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*{EnvironmentVariableName}*");
    }

    [Fact]
    public void Factory_rejects_malformed_connection_string_without_disclosing_it()
    {
        const string secret = "identity-design-time-secret-marker";
        using var _ = new EnvironmentVariableScope(
            EnvironmentVariableName,
            $"Host=localhost;Database=test;Username=test;Password=safe;Timeout={secret}");

        var act = () => new IdentityDbContextFactory().CreateDbContext([]);

        var exception = act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*{EnvironmentVariableName}*некорректн*")
            .Which;
        exception.InnerException.Should().BeNull();
        exception.Message.Should().NotContain(secret);
        exception.ToString().Should().NotContain(secret);
    }

    [Fact]
    public void Factory_creates_context_for_valid_connection_string_without_connecting_to_the_database()
    {
        using var _ = new EnvironmentVariableScope(EnvironmentVariableName, UnreachableConnectionString);

        using var context = new IdentityDbContextFactory().CreateDbContext([]);

        context.Database.GetConnectionString().Should().Be(UnreachableConnectionString);
    }
}
