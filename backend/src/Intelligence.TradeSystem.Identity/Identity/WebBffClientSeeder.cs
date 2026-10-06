using Intelligence.TradeSystem.Identity.Configuration;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Intelligence.TradeSystem.Identity.Identity;

/// <summary>
/// Создаёт или синхронизирует только собственный confidential client BFF; остальные
/// OpenIddict applications не изменяются.
/// </summary>
public sealed partial class WebBffClientSeeder(
    IServiceScopeFactory scopeFactory,
    WebBffClientOptions options,
    ILogger<WebBffClientSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var applicationManager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var clientId = options.ClientId!;
        var descriptor = CreateDescriptor(options);
        var application = await applicationManager.FindByClientIdAsync(clientId, cancellationToken);

        if (application is null)
        {
            await applicationManager.CreateAsync(descriptor, cancellationToken);
            LogClientCreated(clientId);
            return;
        }

        // Уже действующий secret сохраняется в виде существующего hash: повторный старт
        // не перехеширует его и не меняет запись без необходимости.
        if (await applicationManager.ValidateClientSecretAsync(application, options.ClientSecret!, cancellationToken))
        {
            var existing = new OpenIddictApplicationDescriptor();
            await applicationManager.PopulateAsync(existing, application, cancellationToken);
            descriptor.ClientSecret = existing.ClientSecret;
        }

        await applicationManager.UpdateAsync(application, descriptor, cancellationToken);
        LogClientSynchronized(clientId);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    internal static OpenIddictApplicationDescriptor CreateDescriptor(WebBffClientOptions options)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = options.ClientId,
            ClientSecret = options.ClientSecret,
            ClientType = ClientTypes.Confidential,
            DisplayName = "Intelligence Trade Web BFF",
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.Endpoints.EndSession,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code,
                Permissions.Prefixes.Scope + StartupExtensions.ApiScope,
            },
            Requirements =
            {
                Requirements.Features.ProofKeyForCodeExchange,
            },
        };

        foreach (var redirectUri in options.RedirectUris)
        {
            descriptor.RedirectUris.Add(new Uri(redirectUri, UriKind.Absolute));
        }

        foreach (var postLogoutRedirectUri in options.PostLogoutRedirectUris)
        {
            descriptor.PostLogoutRedirectUris.Add(new Uri(postLogoutRedirectUri, UriKind.Absolute));
        }

        return descriptor;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "OIDC client {ClientId} для Web BFF создан.")]
    private partial void LogClientCreated(string clientId);

    [LoggerMessage(Level = LogLevel.Information, Message = "OIDC client {ClientId} для Web BFF синхронизирован.")]
    private partial void LogClientSynchronized(string clientId);
}
