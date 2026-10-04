using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using Intelligence.TradeSystem.Bff.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;

namespace Intelligence.TradeSystem.Bff.Authentication;

/// <summary>
/// Server-side хранилище authentication tickets: browser cookie содержит только opaque
/// session key, а OIDC tokens остаются в памяти процесса BFF. Restart процесса завершает
/// все browser sessions.
/// </summary>
/// <remarks>
/// Новую session создаёт только <see cref="StoreAsync"/>. <see cref="RenewAsync"/> обновляет
/// лишь существующую запись, поэтому запрос, прочитавший ticket до logout, не может
/// восстановить удалённую session.
/// </remarks>
internal sealed class InMemoryAuthenticationTicketStore(
    IMemoryCache cache,
    BffSessionOptions sessionOptions,
    TimeProvider timeProvider) : ITicketStore
{
    internal const string SessionKeyItem = ".bff.session_key";
    internal const string TokenGenerationItem = ".bff.token_generation";
    internal const string LogoutIntentItem = "logout_intent_expires_at";

    private const string CacheKeyPrefix = "bff-session:";
    private const int SessionKeyBytes = 32;

    private readonly Lock gate = new();

    public Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        var key = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(SessionKeyBytes));
        var copy = Prepare(key, ticket);
        lock (gate)
        {
            Set(key, copy);
        }

        return Task.FromResult(key);
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(ticket);

        var copy = Prepare(key, ticket);
        lock (gate)
        {
            if (!TryGetLiveTicket(key, out var existing))
            {
                return Task.CompletedTask;
            }

            // Cookie handler может продлить session копией ticket, прочитанной до параллельного
            // token refresh. Более новое поколение tokens не откатывается таким renew.
            if (GetTokenGeneration(existing.Properties) > GetTokenGeneration(copy.Properties))
            {
                copy.Properties.StoreTokens(existing.Properties.GetTokens());
                SetTokenGeneration(copy.Properties, GetTokenGeneration(existing.Properties));
            }

            // Копия, прочитанная до POST /bff/auth/logout, не должна стереть подтверждённый
            // logout intent: иначе logout/complete отклонит уже подтверждённый logout.
            if (TryGetLogoutIntentExpiry(existing.Properties, out var storedIntent)
                && storedIntent > timeProvider.GetUtcNow()
                && (!TryGetLogoutIntentExpiry(copy.Properties, out var incomingIntent) || incomingIntent < storedIntent))
            {
                copy.Properties.Items[LogoutIntentItem] = existing.Properties.Items[LogoutIntentItem];
            }

            Set(key, copy);
        }

        return Task.CompletedTask;
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return Task.FromResult<AuthenticationTicket?>(null);
        }

        lock (gate)
        {
            return Task.FromResult(TryGetLiveTicket(key, out var ticket) ? Copy(ticket) : null);
        }
    }

    public Task RemoveAsync(string key)
    {
        if (!string.IsNullOrEmpty(key))
        {
            lock (gate)
            {
                cache.Remove(CacheKey(key));
            }
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

    internal static bool TryGetLogoutIntentExpiry(AuthenticationProperties properties, out DateTimeOffset expiresAt)
    {
        expiresAt = default;
        return properties.Items.TryGetValue(LogoutIntentItem, out var value)
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out expiresAt);
    }

    private bool TryGetLiveTicket(string key, [NotNullWhen(true)] out AuthenticationTicket? ticket)
    {
        if (!cache.TryGetValue(CacheKey(key), out ticket) || ticket is null)
        {
            ticket = null;
            return false;
        }

        if (ticket.Properties.ExpiresUtc is { } expiresUtc && expiresUtc <= timeProvider.GetUtcNow())
        {
            cache.Remove(CacheKey(key));
            ticket = null;
            return false;
        }

        return true;
    }

    private static AuthenticationTicket Prepare(string key, AuthenticationTicket ticket)
    {
        var copy = Copy(ticket);
        copy.Properties.Items[SessionKeyItem] = key;
        return copy;
    }

    private void Set(string key, AuthenticationTicket ticket)
    {
        var expiresUtc = ticket.Properties.ExpiresUtc
            ?? timeProvider.GetUtcNow().Add(sessionOptions.Lifetime);
        cache.Set(CacheKey(key), ticket, new MemoryCacheEntryOptions { AbsoluteExpiration = expiresUtc });
    }

    private static string CacheKey(string key) => CacheKeyPrefix + key;

    // Запросы одной session не должны разделять изменяемый экземпляр properties.
    private static AuthenticationTicket Copy(AuthenticationTicket ticket) =>
        new(ticket.Principal.Clone(), ticket.Properties.Clone(), ticket.AuthenticationScheme);
}
