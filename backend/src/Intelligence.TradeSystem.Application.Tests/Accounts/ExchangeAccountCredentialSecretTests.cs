using System.Text.Json;
using FluentAssertions;
using Intelligence.TradeSystem.Application.Accounts.Credentials;
using Intelligence.TradeSystem.Application.Concurrency;
using Xunit;

namespace Intelligence.TradeSystem.Application.Tests.Accounts;

public sealed class ExchangeAccountCredentialSecretTests
{
    [Fact]
    public void Secret_value_object_redacts_values_from_diagnostics_and_json()
    {
        const string apiKey = "test-api-key-do-not-log";
        const string apiSecret = "test-api-secret-do-not-log";
        var secret = new ExchangeAccountCredentialSecret(apiKey, apiSecret);

        secret.ToString().Should().NotContain(apiKey).And.NotContain(apiSecret);
        JsonSerializer.Serialize(secret)
            .Should().NotContain(apiKey)
            .And.NotContain(apiSecret);
        JsonSerializer.Serialize(new ExchangeAccountCredential(
                secret,
                ConcurrencyVersion.Initial))
            .Should().NotContain(apiKey)
            .And.NotContain(apiSecret);
        typeof(ExchangeAccountCredentialSecret)
            .GetProperties()
            .Should().BeEmpty();
    }

    [Fact]
    public void Secret_values_are_available_only_inside_the_controlled_callback()
    {
        var secret = new ExchangeAccountCredentialSecret(
            "test-api-key-use",
            "test-api-secret-use");

        string? apiKey = null;
        string? apiSecret = null;
        secret.Use((key, value) =>
        {
            apiKey = key;
            apiSecret = value;
        });

        apiKey.Should().Be("test-api-key-use");
        apiSecret.Should().Be("test-api-secret-use");
    }
}
