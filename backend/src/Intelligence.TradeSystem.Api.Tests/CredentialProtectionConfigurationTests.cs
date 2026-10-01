using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Intelligence.TradeSystem.Api.Tests;

public sealed class CredentialProtectionConfigurationTests
{
    private static readonly string ValidKey = Convert.ToBase64String(new byte[32]);

    [Fact]
    public void Missing_active_key_id_fails_during_host_creation()
    {
        AssertHostCreationFails(
            "*CredentialProtection:ActiveKeyId*",
            ("CredentialProtection:Keys:test", ValidKey));
    }

    [Fact]
    public void Blank_active_key_id_fails_during_host_creation()
    {
        AssertHostCreationFails(
            "*CredentialProtection:ActiveKeyId*",
            ("CredentialProtection:ActiveKeyId", " "),
            ("CredentialProtection:Keys:test", ValidKey));
    }

    [Fact]
    public void Missing_keys_fail_during_host_creation()
    {
        AssertHostCreationFails(
            "*CredentialProtection:Keys*",
            ("CredentialProtection:ActiveKeyId", "test"));
    }

    [Fact]
    public void Key_that_is_not_base64_fails_without_disclosing_key_material()
    {
        const string keyMaterial = "secret-key-material-marker!!";

        var exception = AssertHostCreationFails(
            "*CredentialProtection key 'test'*Base64*",
            ("CredentialProtection:ActiveKeyId", "test"),
            ("CredentialProtection:Keys:test", keyMaterial));

        exception.Message.Should().NotContain(keyMaterial);
        exception.ToString().Should().NotContain(keyMaterial);
    }

    [Fact]
    public void Key_with_invalid_length_fails_without_disclosing_key_material()
    {
        var keyMaterial = Convert.ToBase64String(new byte[16]);

        var exception = AssertHostCreationFails(
            "*CredentialProtection key 'test'*32 bytes*",
            ("CredentialProtection:ActiveKeyId", "test"),
            ("CredentialProtection:Keys:test", keyMaterial));

        exception.Message.Should().NotContain(keyMaterial);
    }

    [Fact]
    public void Active_key_id_that_does_not_identify_a_configured_key_fails_during_host_creation()
    {
        AssertHostCreationFails(
            "*CredentialProtection:ActiveKeyId*configured key*",
            ("CredentialProtection:ActiveKeyId", "missing"),
            ("CredentialProtection:Keys:test", ValidKey));
    }

    [Fact]
    public void Key_id_longer_than_the_persisted_limit_fails_during_host_creation()
    {
        var keyId = new string('k', 129);

        AssertHostCreationFails(
            "*CredentialProtection key ids*128*",
            ("CredentialProtection:ActiveKeyId", keyId),
            ($"CredentialProtection:Keys:{keyId}", ValidKey));
    }

    [Fact]
    public void Valid_active_key_and_32_byte_key_pass_host_creation()
    {
        using var factory = CreateFactory(
            ("CredentialProtection:ActiveKeyId", "test"),
            ("CredentialProtection:Keys:test", ValidKey));

        var act = () => factory.CreateClient().Dispose();

        act.Should().NotThrow();
    }

    private static InvalidOperationException AssertHostCreationFails(
        string expectedMessage,
        params (string Key, string Value)[] credentialProtectionSettings)
    {
        using var factory = CreateFactory(credentialProtectionSettings);

        var act = () => factory.CreateClient();

        return act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage(expectedMessage)
            .Which;
    }

    private static WebApplicationFactory<Program> CreateFactory(
        params (string Key, string Value)[] credentialProtectionSettings)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting(
                    "ConnectionStrings:TradeSystem",
                    "Host=127.0.0.1;Port=1;Database=tradesystem;Timeout=1;Command Timeout=1");
                builder.UseSetting("Authentication:Audience", "intelligence-trade-api");
                foreach (var (key, value) in credentialProtectionSettings)
                {
                    builder.UseSetting(key, value);
                }
            });
    }
}
