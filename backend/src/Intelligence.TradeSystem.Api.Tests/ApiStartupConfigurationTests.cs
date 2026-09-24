using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

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

    private static WebApplicationFactory<Program> CreateFactory(
        KeyValuePair<string, string?> connectionString)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection([connectionString])));
    }
}
