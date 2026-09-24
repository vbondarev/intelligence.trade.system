using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Intelligence.TradeSystem.Api.Tests;

public sealed class ApiStartupConfigurationTests
{
    [Fact]
    public void Missing_connection_string_fails_during_host_creation()
    {
        using var factory = new WebApplicationFactory<Program>();

        var act = () => factory.CreateClient();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*ConnectionStrings:TradeSystem*");
    }

    [Fact]
    public void Blank_connection_string_fails_during_host_creation()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.UseSetting("ConnectionStrings:TradeSystem", " "));

        var act = () => factory.CreateClient();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*ConnectionStrings:TradeSystem*");
    }
}
