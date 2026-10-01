using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Intelligence.TradeSystem.Api.Tests;

public sealed class ApiStartupConfigurationTests
{
    [Fact]
    public void Missing_connection_string_fails_during_host_creation()
    {
        using var factory = CreateFactory(
            new KeyValuePair<string, string?>("ConnectionStrings:TradeSystem", null));

        var act = () => factory.CreateClient();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*ConnectionStrings:TradeSystem*");
    }

    [Fact]
    public void Blank_connection_string_fails_during_host_creation()
    {
        using var factory = CreateFactory(
            new KeyValuePair<string, string?>("ConnectionStrings:TradeSystem", " "));

        var act = () => factory.CreateClient();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*ConnectionStrings:TradeSystem*");
    }

    [Fact]
    public void Malformed_connection_string_fails_during_host_creation()
    {
        using var factory = CreateFactory(
            new KeyValuePair<string, string?>(
                "ConnectionStrings:TradeSystem",
                "Host=localhost;ThisIsNotAKeyValue"));

        var act = () => factory.CreateClient();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*ConnectionStrings:TradeSystem*");
    }

    [Fact]
    public void Malformed_connection_string_fails_without_disclosing_it()
    {
        const string secret = "business-secret-marker";
        using var factory = CreateFactory(
            new KeyValuePair<string, string?>(
                "ConnectionStrings:TradeSystem",
                $"Host=localhost;Database=tradesystem;Timeout={secret}"));

        var act = () => factory.CreateClient();

        var exception = act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*ConnectionStrings:TradeSystem*malformed*")
            .Which;
        exception.Message.Should().NotContain(secret);
        exception.ToString().Should().NotContain(secret);
    }

    [Fact]
    public void Valid_connection_string_to_unreachable_database_does_not_fail_host_creation()
    {
        using var factory = new ApiWebApplicationFactory();

        var act = () => factory.CreateClient().Dispose();

        act.Should().NotThrow();
    }

    private static WebApplicationFactory<Program> CreateFactory(
        KeyValuePair<string, string?> connectionString)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.UseSetting(connectionString.Key, connectionString.Value));
    }
}
