using System.Globalization;
using System.Security.Cryptography;
using Intelligence.TradeSystem.Web.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;

namespace Intelligence.TradeSystem.Web.Authentication;

/// <summary>
/// Server-side хранилище authentication tickets: browser cookie содержит только opaque
/// session key, а OIDC tokens остаются в памяти процесса BFF. Restart процесса завершает
/// все browser sessions.
/// </summary>
internal sealed class InMemoryAuthenticationTicketStore(
    IMemoryCache cache,
    WebSessionOptions sessionOptions,
    TimeProvider timeProvider) : ITicketStore
{
    internal const string SessionKeyItem = ".bff.session_key";
    internal const string TokenGenerationItem = ".bff.token_generation";

    private const string CacheKeyPrefix = "bff-session:";
    private const int SessionKeyBytes = 32;

    private readonly Lock gate = new();

    public Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        var key = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(SessionKeyBytes));
        Save(key, ticket);
        return Task.FromResult(key);
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(ticket);

        Save(key, ticket);
        return Task.CompletedTask;
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        if (string.IsNullOrEmpty(key)
            || !cache.TryGetValue(CacheKey(key), out AuthenticationTicket? ticket)
            || ticket is null)
        {
            return Task.FromResult<AuthenticationTicket?>(null);
        }

        if (ticket.Properties.ExpiresUtc is { } expiresUtc && expiresUtc <= timeProvider.GetUtcNow())
        {
            cache.Remove(CacheKey(key));
            return Task.FromResult<AuthenticationTicket?>(null);
        }

        return Task.FromResult<AuthenticationTicket?>(Copy(ticket));
    }

    public Task RemoveAsync(string key)
    {
        if (!string.IsNullOrEmpty(key))
        {
            cache.Remove(CacheKey(key));
        }

        return Task.CompletedTask;
    }

    internal static long GetTokenGeneration(AuthenticationProperties properties) =>
        properties.Items.TryGetValue(TokenGenerationItem, out var value)
        && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var generation)
            ? generation
            : 0;

    internal static void SetTokenGeneration(AuthenticationProperties properties, long generation) =>
        properties.Items[TokenGenerationItem] = generation.ToString(CultureInfo.InvariantCulture);

    private void Save(string key, AuthenticationTicket ticket)
    {
        var copy = Copy(ticket);
        copy.Properties.Items[SessionKeyItem] = key;

        var expiresUtc = copy.Properties.ExpiresUtc
            ?? timeProvider.GetUtcNow().Add(sessionOptions.Lifetime);

        lock (gate)
        {
            // Cookie handler может продлить session копией ticket, прочитанной до параллельного
            // token refresh. Более новое поколение tokens не откатывается таким renew.
            if (cache.TryGetValue(CacheKey(key), out AuthenticationTicket? existing)
                && existing is not null
                && GetTokenGeneration(existing.Properties) > GetTokenGeneration(copy.Properties))
            {
                copy.Properties.StoreTokens(existing.Properties.GetTokens());
                SetTokenGeneration(copy.Properties, GetTokenGeneration(existing.Properties));
            }

            cache.Set(
                CacheKey(key),
                copy,
                new MemoryCacheEntryOptions { AbsoluteExpiration = expiresUtc });
        }
    }

    private static string CacheKey(string key) => CacheKeyPrefix + key;

    // Запросы одной session не должны разделять изменяемый экземпляр properties.
    private static AuthenticationTicket Copy(AuthenticationTicket ticket) =>
        new(ticket.Principal.Clone(), ticket.Properties.Clone(), ticket.AuthenticationScheme);
}
