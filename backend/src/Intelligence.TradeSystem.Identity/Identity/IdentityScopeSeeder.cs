using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;

namespace Intelligence.TradeSystem.Identity.Identity;

public sealed class IdentityScopeSeeder(IServiceScopeFactory scopeFactory) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var scopeManager = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();
        var existing = await scopeManager.FindByNameAsync(
            StartupExtensions.ApiScope,
            cancellationToken);

        if (existing is not null)
        {
            return;
        }

        await scopeManager.CreateAsync(new OpenIddictScopeDescriptor
        {
            Name = StartupExtensions.ApiScope,
            DisplayName = "Trade API",
            Resources = { StartupExtensions.ApiResource }
        }, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
