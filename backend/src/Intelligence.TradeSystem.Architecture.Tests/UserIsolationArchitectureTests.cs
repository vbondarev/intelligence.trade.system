using FluentAssertions;
using Intelligence.TradeSystem.Api.Controllers;
using Intelligence.TradeSystem.Application.Accounts.Credentials;
using Intelligence.TradeSystem.Application.Users;
using Intelligence.TradeSystem.Domain;
using Xunit;

namespace Intelligence.TradeSystem.Architecture.Tests;

public sealed class UserIsolationArchitectureTests
{
    [Fact]
    public void Application_Does_Not_Reference_AspNetCore()
    {
        typeof(ICurrentUserContext).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Should().NotContain(name =>
                name != null &&
                name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
    }

    [Fact]
    public void Infrastructure_Does_Not_Reference_Api()
    {
        typeof(Intelligence.TradeSystem.Infrastructure.StartupExtensions).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Should().NotContain("Intelligence.TradeSystem.Api");
    }

    [Fact]
    public void CurrentUserContext_Adapter_Is_Defined_Only_In_Api()
    {
        var apiAssembly = typeof(MarketAnalysisController).Assembly;
        var productionAssemblies = new[]
        {
            apiAssembly,
            typeof(ICurrentUserContext).Assembly,
            typeof(Intelligence.TradeSystem.Domain.Position).Assembly,
            typeof(Intelligence.TradeSystem.Infrastructure.StartupExtensions).Assembly,
        };

        var adapters = productionAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type =>
                !type.IsAbstract &&
                !type.IsInterface &&
                typeof(ICurrentUserContext).IsAssignableFrom(type))
            .ToArray();

        adapters.Should().ContainSingle();
        Assert.Same(apiAssembly, adapters[0].Assembly);
    }

    [Fact]
    public void Credential_contracts_stay_out_of_domain_and_application_implementation_dependencies()
    {
        typeof(ExchangeAccount)
            .GetProperties()
            .Select(property => property.Name)
            .Should()
            .NotContain(name =>
                name.Contains("ApiKey", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("ApiSecret", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Credential", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Encryption", StringComparison.OrdinalIgnoreCase));

        typeof(IExchangeAccountCredentialStore).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Should()
            .NotContain(name =>
                name == "Bybit.Net" ||
                name == "Microsoft.EntityFrameworkCore" ||
                name == "System.Security.Cryptography");
    }
}
