using FluentAssertions;
using Intelligence.TradeSystem.Identity.Migrations;
using Xunit;

namespace Intelligence.TradeSystem.Authentication.IntegrationTests;

public sealed class IdentityMigrationRunnerTests
{
    [Fact]
    public async Task Migration_runner_fails_when_database_is_unreachable()
    {
        await Assert.ThrowsAnyAsync<Exception>(() =>
            IdentityMigrationRunner.ApplyAsync(
                "Host=127.0.0.1;Port=1;Database=unreachable;Username=none;Password=none;Timeout=1"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Migration_runner_rejects_blank_connection_string_before_database_operation(
        string connectionString)
    {
        var act = () => IdentityMigrationRunner.ApplyAsync(connectionString);

        await act.Should()
            .ThrowExactlyAsync<InvalidOperationException>()
            .WithMessage("*ConnectionStrings__TradeSystemIdentity*");
    }

    [Fact]
    public async Task Migration_runner_rejects_malformed_connection_string_without_disclosing_it()
    {
        const string secret = "identity-migration-secret-marker";

        var act = () => IdentityMigrationRunner.ApplyAsync(
            $"Host=localhost;Database=test;Username=test;Password=safe;Timeout={secret}");

        var exception = (await act.Should()
                .ThrowExactlyAsync<InvalidOperationException>()
                .WithMessage("*ConnectionStrings__TradeSystemIdentity*некорректн*"))
            .Which;
        exception.InnerException.Should().BeNull();
        exception.Message.Should().NotContain(secret);
        exception.ToString().Should().NotContain(secret);
    }
}
