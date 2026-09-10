using System.Reflection;
using FluentAssertions;
using Intelligence.TradeSystem.Api.Controllers;
using Intelligence.TradeSystem.Application.Accounts.Credentials;
using Intelligence.TradeSystem.Application.Market;
using Intelligence.TradeSystem.Application.Users;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Infrastructure.MarketCaching;
using Intelligence.TradeSystem.MarketIntelligence.Snapshots;
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
    public void Application_And_MarketIntelligence_Do_Not_Reference_Cache_Frameworks()
    {
        var forbiddenAssemblies = new[]
        {
            "Microsoft.Extensions.Caching.Hybrid",
            "Microsoft.Extensions.Caching.Memory",
        };

        typeof(IMarketSnapshotService).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Should().NotContain(name => forbiddenAssemblies.Contains(name));
        typeof(MarketSnapshot).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Should().NotContain(name => forbiddenAssemblies.Contains(name));
        typeof(PublicMarketSnapshotCache).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Should().Contain("Microsoft.Extensions.Caching.Hybrid");
    }

    [Fact]
    public void Public_Market_Cache_Key_Contains_No_User_Or_Private_Dimensions()
    {
        var keyProperties = typeof(PublicMarketSnapshotCacheKey)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance);

        keyProperties.Select(property => property.PropertyType)
            .Should()
            .NotContain(typeof(UserId));
        keyProperties.Select(property => property.Name)
            .Should()
            .NotContain(name =>
                name.Contains("User", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Account", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Token", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Credential", StringComparison.OrdinalIgnoreCase));
        keyProperties.Select(property => property.Name)
            .Should()
            .Contain(["ExchangeId", "Symbol", "Category"]);
    }

    [Fact]
    public void Public_Market_Cache_And_Snapshot_Contain_No_Private_State()
    {
        typeof(CachedMarketSnapshotService).GetConstructors().Single().GetParameters()
            .Select(parameter => parameter.ParameterType)
            .Should()
            .NotContain(typeof(ICurrentUserContext));

        typeof(PublicMarketSnapshotCache).GetConstructors().Single().GetParameters()
            .Select(parameter => parameter.ParameterType.Name)
            .Should()
            .NotContain(name =>
                name.Contains("Repository", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Credential", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("User", StringComparison.OrdinalIgnoreCase));

        typeof(MarketSnapshot).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .Should()
            .NotContain(name =>
                name.Contains("User", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Account", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Position", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Credential", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Portfolio", StringComparison.OrdinalIgnoreCase));
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
