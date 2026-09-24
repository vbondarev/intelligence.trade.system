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
}
