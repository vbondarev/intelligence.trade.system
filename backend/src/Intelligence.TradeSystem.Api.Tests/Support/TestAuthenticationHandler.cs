using System.Security.Claims;
using System.Text.Encodings.Web;
using Intelligence.TradeSystem.Api.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Intelligence.TradeSystem.Api.Tests.Support;

/// <summary>
/// Схема аутентификации только для тестов, заменяющая реальную OIDC/JWT bearer-схему
/// в тестах на основе <c>WebApplicationFactory</c>. Она аутентифицирует caller как principal
/// <c>TradeUser</c> с помощью request header <see cref="UserIdHeader"/> или оставляет запрос
/// неаутентифицированным (что запускает обычный challenge <c>401</c>), если header
/// отсутствует; это позволяет тестам одновременно проверять реальную маршрутизацию,
/// model binding и authorization.
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
