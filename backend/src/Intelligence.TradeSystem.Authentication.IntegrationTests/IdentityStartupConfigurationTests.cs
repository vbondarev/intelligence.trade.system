using FluentAssertions;
using Intelligence.TradeSystem.Identity;
using Intelligence.TradeSystem.Identity.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Intelligence.TradeSystem.Authentication.IntegrationTests;

public sealed class IdentityStartupConfigurationTests
{
    private const string UnreachableConnectionString =
        "Host=127.0.0.1;Port=1;Database=tradesystem_identity;Username=none;Password=none;Timeout=1";

    [Fact]
    public void Missing_identity_connection_string_fails_during_host_creation()
    {
        using var factory = CreateFactory("Testing");

        var act = () => factory.CreateClient();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*ConnectionStrings:TradeSystemIdentity*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_identity_connection_string_fails_during_host_creation(string connectionString)
    {
        using var factory = CreateFactory(
            "Testing",
            ("ConnectionStrings:TradeSystemIdentity", connectionString));

        var act = () => factory.CreateClient();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*ConnectionStrings:TradeSystemIdentity*");
    }

    [Fact]
    public void Malformed_identity_connection_string_fails_without_disclosing_it()
    {
        const string secret = "identity-secret-marker";
        var connectionString =
            $"Host=localhost;Database=test;Username=test;Password=safe;Timeout={secret}";
        using var factory = CreateFactory(
            "Testing",
            ("ConnectionStrings:TradeSystemIdentity", connectionString));

        var act = () => factory.CreateClient();

        var exception = act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*ConnectionStrings:TradeSystemIdentity*некорректн*")
            .Which;
        exception.Message.Should().NotContain(secret);
        exception.ToString().Should().NotContain(secret);
    }

    [Fact]
    public void Valid_identity_connection_string_is_registered_without_connecting_to_the_database()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:TradeSystemIdentity"] = UnreachableConnectionString,
            })
            .Build();
        var services = new ServiceCollection();

        var act = () => services.AddIdentityPersistence(configuration);

        act.Should().NotThrow();
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IdentityDbContext));
    }

    [Fact]
    public void Production_requires_an_issuer()
    {
        using var factory = CreateFactory(
            Environments.Production,
            ("ConnectionStrings:TradeSystemIdentity", UnreachableConnectionString));

        var act = () => factory.CreateClient();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Identity:Issuer*");
    }

    [Fact]
    public void Production_rejects_an_insecure_issuer()
    {
        using var factory = CreateFactory(
            Environments.Production,
            ("ConnectionStrings:TradeSystemIdentity", UnreachableConnectionString),
            ("Identity:Issuer", "http://identity.example"));

        var act = () => factory.CreateClient();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Identity:Issuer*HTTPS*");
    }

    [Fact]
    public void Production_requires_persistent_signing_certificates()
    {
        using var factory = CreateFactory(
            Environments.Production,
            ("ConnectionStrings:TradeSystemIdentity", UnreachableConnectionString),
            ("Identity:Issuer", "https://identity.example"),
            ("Identity:EncryptionCertificatePath", "missing-encryption-certificate.pfx"));

        var act = () => factory.CreateClient();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*persistent signing certificates*");
    }

    [Fact]
    public void Production_requires_an_encryption_certificate_without_disclosing_certificate_password()
    {
        const string certificatePassword = "signing-certificate-password-marker";
        using var factory = CreateFactory(
            Environments.Production,
            ("ConnectionStrings:TradeSystemIdentity", UnreachableConnectionString),
            ("Identity:Issuer", "https://identity.example"),
            ("Identity:SigningCertificates:0:Path", "missing-signing-certificate.pfx"),
            ("Identity:SigningCertificates:0:Password", certificatePassword));

        var act = () => factory.CreateClient();

        var exception = act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*encryption certificate path*")
            .Which;
        exception.Message.Should().NotContain(certificatePassword);
    }

    private static WebApplicationFactory<IdentityApplicationMarker> CreateFactory(
        string environment,
        params (string Key, string Value)[] settings)
    {
        return new WebApplicationFactory<IdentityApplicationMarker>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(environment);
                foreach (var (key, value) in settings)
                {
                    builder.UseSetting(key, value);
                }
            });
    }
}
