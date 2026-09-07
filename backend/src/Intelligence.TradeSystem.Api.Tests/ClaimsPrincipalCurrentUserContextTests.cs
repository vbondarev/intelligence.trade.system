using System.Security.Claims;
using Intelligence.TradeSystem.Api.Authentication;
using Intelligence.TradeSystem.Application.Users;
using Microsoft.AspNetCore.Http;

namespace Intelligence.TradeSystem.Api.Tests;

public sealed class ClaimsPrincipalCurrentUserContextTests
{
    [Fact]
    public void UserId_returns_the_domain_id_for_a_valid_user_principal()
    {
        var expected = Guid.NewGuid();
        var context = CreateContext(expected.ToString(), "user");

        context.UserId.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData(null, "user")]
    [InlineData("not-a-guid", "user")]
    [InlineData("00000000-0000-0000-0000-000000000000", "user")]
    [InlineData("11111111-1111-1111-1111-111111111111", null)]
    [InlineData("11111111-1111-1111-1111-111111111111", "machine")]
    public void UserId_throws_a_controlled_application_exception_for_an_invalid_principal(
        string? subject,
        string? principalType)
    {
        var context = CreateContext(subject, principalType);

        var act = () => context.UserId;

        act.Should().Throw<InvalidCurrentUserException>();
    }

    private static ClaimsPrincipalCurrentUserContext CreateContext(
        string? subject,
        string? principalType)
    {
        var claims = new List<Claim>();
        if (subject is not null)
        {
            claims.Add(new Claim("sub", subject));
        }

        if (principalType is not null)
        {
            claims.Add(new Claim("trade_principal_type", principalType));
        }

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
        };
        return new ClaimsPrincipalCurrentUserContext(
            new HttpContextAccessor { HttpContext = httpContext });
    }
}
