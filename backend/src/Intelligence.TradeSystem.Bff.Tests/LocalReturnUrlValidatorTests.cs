using Intelligence.TradeSystem.Bff.Security;

namespace Intelligence.TradeSystem.Bff.Tests;

public sealed class LocalReturnUrlValidatorTests
{
    [Theory]
    [InlineData(null, "/app")]
    [InlineData("", "/app")]
    [InlineData("/", "/")]
    [InlineData("/app", "/app")]
    [InlineData("/app/positions?filter=open", "/app/positions?filter=open")]
    [InlineData("/app/%D0%BE%D0%B1%D0%B7%D0%BE%D1%80", "/app/%D0%BE%D0%B1%D0%B7%D0%BE%D1%80")]
    public void Local_paths_are_accepted(string? returnUrl, string expected)
    {
        LocalReturnUrlValidator.TryNormalize(returnUrl, out var normalized).Should().BeTrue();
        normalized.Should().Be(expected);
    }

    [Theory]
    [InlineData("https://evil.example/app")]
    [InlineData("http:/evil.example")]
    [InlineData("javascript:alert(1)")]
    [InlineData("app")]
    [InlineData("//evil.example")]
    [InlineData("/\\evil.example")]
    [InlineData("\\\\evil.example")]
    [InlineData("/app\\..\\evil")]
    [InlineData("/%2F%2Fevil.example")]
    [InlineData("/%2fevil.example")]
    [InlineData("/%5Cevil.example")]
    [InlineData("%2F%2Fevil.example")]
    [InlineData("/app%0D%0ALocation:%20https://evil.example")]
    [InlineData("/app\twith-tab")]
    [InlineData("/app with-space")]
    public void External_network_path_backslash_and_encoded_variants_are_rejected(string returnUrl)
    {
        LocalReturnUrlValidator.TryNormalize(returnUrl, out var normalized).Should().BeFalse();
        normalized.Should().BeEmpty();
    }

    [Fact]
    public void Overlong_return_url_is_rejected()
    {
        var returnUrl = "/" + new string('a', 2048);

        LocalReturnUrlValidator.TryNormalize(returnUrl, out _).Should().BeFalse();
    }
}
