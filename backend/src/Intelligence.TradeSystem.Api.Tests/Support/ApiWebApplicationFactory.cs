using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Intelligence.TradeSystem.Api.Tests;

public class ApiWebApplicationFactory : WebApplicationFactory<Program>
{
    private static readonly byte[] TestCredentialKey = new byte[32];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting(
            "ConnectionStrings:TradeSystem",
            "Host=127.0.0.1;Port=1;Database=tradesystem;Timeout=1;Command Timeout=1");
        builder.UseSetting("CredentialProtection:ActiveKeyId", "test");
        builder.UseSetting(
            "CredentialProtection:Keys:test",
            Convert.ToBase64String(TestCredentialKey));
        builder.UseSetting("ExchangeAccountBackgroundSync:Enabled", "false");
        builder.UseSetting("ApplicationEventOutboxDispatcher:Enabled", "false");
    }
}
