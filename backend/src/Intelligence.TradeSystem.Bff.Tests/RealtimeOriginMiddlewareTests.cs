using Intelligence.TradeSystem.Bff.Realtime;
using Microsoft.AspNetCore.Http;

namespace Intelligence.TradeSystem.Bff.Tests;

public sealed class RealtimeOriginMiddlewareTests
{
    [Theory]
    [InlineData("https://app.example", true)]
    [InlineData("https://other.example", false)]
    [InlineData("http://app.example", false)]
    [InlineData("null", false)]
    [InlineData("", false)]
    public async Task Hub_origin_must_match_public_request(string origin, bool allowed)
    {
        var called = false;
        var middleware = new RealtimeOriginMiddleware(_ => { called = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/bff/hubs/v1/updates";
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("app.example");
        if (origin.Length > 0)
        {
            context.Request.Headers.Origin = origin;
        }

        await middleware.InvokeAsync(context);

        Assert.Equal(allowed, called);
        if (!allowed)
        {
            Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        }
    }
    [Fact]
    public async Task Access_token_query_is_rejected_even_with_valid_origin()
    {
        var called = false;
        var middleware = new RealtimeOriginMiddleware(_ => { called = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/bff/hubs/v1/updates";
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("app.example");
        context.Request.Headers.Origin = "https://app.example";
        context.Request.QueryString = new QueryString("?access_token=test-token");
        await middleware.InvokeAsync(context);
        called.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }
}
