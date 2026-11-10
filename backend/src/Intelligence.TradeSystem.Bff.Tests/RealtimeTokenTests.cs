using System.Net;
using Intelligence.TradeSystem.Bff.Authentication;
using Intelligence.TradeSystem.Bff.Tests.Support;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace Intelligence.TradeSystem.Bff.Tests;

public sealed class RealtimeTokenTests
{
    [Fact]
    public async Task Rotated_token_is_shared_with_parallel_requests_and_session()
    {
        using var factory = new BffApplicationFactory();
        factory.TokenEndpoint.Responder = (_, _) => Task.FromResult(UpstreamResponses.TokenSuccess("access-2", "refresh-2"));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync(accessTokenLifetime: TimeSpan.FromSeconds(30));
        var service = factory.Services.GetRequiredService<BffTokenService>();
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            service.GetAccessTokenForSessionAsync(browser.GetSessionKey()!, "user-subject", CancellationToken.None)));
        results.Should().OnlyContain(result => result.Status == BffAccessTokenStatus.Available && result.AccessToken == "access-2");
        factory.TokenEndpoint.Count.Should().Be(1);
        (await browser.GetStoredTicketAsync())!.Properties.GetTokenValue("refresh_token").Should().Be("refresh-2");
    }

    [Fact]
    public async Task Removed_session_or_wrong_subject_cannot_return_token()
    {
        using var factory = new BffApplicationFactory();
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync();
        var key = browser.GetSessionKey()!;
        var service = factory.Services.GetRequiredService<BffTokenService>();
        (await service.GetAccessTokenForSessionAsync(key, "other", CancellationToken.None)).Status.Should().Be(BffAccessTokenStatus.SessionEnded);
        await factory.Services.GetRequiredService<Microsoft.AspNetCore.Authentication.Cookies.ITicketStore>().RemoveAsync(key);
        (await service.GetAccessTokenForSessionAsync(key, "user-subject", CancellationToken.None)).Status.Should().Be(BffAccessTokenStatus.SessionEnded);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, 1, false)]
    [InlineData(HttpStatusCode.ServiceUnavailable, 2, true)]
    public async Task Rejected_grant_removes_session_but_unavailable_identity_preserves_it(
        HttpStatusCode status, int expected, bool sessionRemains)
    {
        using var factory = new BffApplicationFactory();
        factory.TokenEndpoint.Responder = (_, _) => Task.FromResult(status == HttpStatusCode.BadRequest
            ? UpstreamResponses.TokenError(status, "invalid_grant") : new HttpResponseMessage(status));
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync(accessTokenLifetime: TimeSpan.FromSeconds(30));
        var service = factory.Services.GetRequiredService<BffTokenService>();
        (await service.GetAccessTokenForSessionAsync(browser.GetSessionKey()!, "user-subject", CancellationToken.None)).Status.Should().Be((BffAccessTokenStatus)expected);
        ((await browser.GetStoredTicketAsync()) is not null).Should().Be(sessionRemains);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Logout_intent_during_refresh_does_not_remove_ticket_before_oidc_logout(bool rejected)
    {
        using var factory = new BffApplicationFactory();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.TokenEndpoint.Responder = async (_, _) =>
        {
            started.SetResult();
            await release.Task;
            return rejected ? UpstreamResponses.TokenError(HttpStatusCode.BadRequest, "invalid_grant")
                : UpstreamResponses.TokenSuccess("access-2", "refresh-2");
        };
        using var browser = factory.CreateBrowser();
        await browser.SignInAsync(accessTokenLifetime: TimeSpan.FromSeconds(30));
        var antiforgery = await browser.GetAntiforgeryTokenAsync();
        var service = factory.Services.GetRequiredService<BffTokenService>();
        var refresh = service.GetAccessTokenForSessionAsync(browser.GetSessionKey()!, "user-subject", CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using var logout = await browser.PostLogoutAsync(antiforgery);
        logout.IsSuccessStatusCode.Should().BeTrue();
        release.SetResult();
        (await refresh).Status.Should().Be(BffAccessTokenStatus.SessionEnded);
        var ticket = await browser.GetStoredTicketAsync();
        ticket.Should().NotBeNull();
        InMemoryAuthenticationTicketStore.TryGetLogoutIntentExpiry(ticket!.Properties, out _).Should().BeTrue();
    }
}
