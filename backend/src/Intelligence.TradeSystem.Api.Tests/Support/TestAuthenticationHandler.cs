using System.Security.Claims;
using System.Text.Encodings.Web;
using Intelligence.TradeSystem.Api.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Intelligence.TradeSystem.Api.Tests.Support;

/// <summary>
/// Test-only authentication scheme that stands in for the real OIDC/JWT bearer scheme in
/// <c>WebApplicationFactory</c>-based tests. It authenticates the caller as a
/// <c>TradeUser</c> principal using the <see cref="UserIdHeader"/> request header, or leaves
/// the request unauthenticated (triggering the normal <c>401</c> challenge) when the header
/// is absent, so tests can exercise real routing, model binding, and authorization together.
/// </summary>
public sealed class TestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string UserIdHeader = "X-Test-User-Id";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserIdHeader, out var values)
            || !Guid.TryParse(values.ToString(), out var userId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new[]
        {
            new Claim(TradeAuthorization.SubjectClaim, userId.ToString()),
            new Claim(TradeAuthorization.PrincipalTypeClaim, TradeAuthorization.UserPrincipalType),
            new Claim(TradeAuthorization.ScopeClaim, "trade.api"),
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
