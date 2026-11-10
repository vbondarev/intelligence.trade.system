using System.Collections.Concurrent;
using Intelligence.TradeSystem.Bff.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Intelligence.TradeSystem.Bff.Realtime;

internal sealed class RealtimeSessionRegistry : IDisposable
{
    private readonly Lock sync = new();
    private readonly Dictionary<string, SessionState> sessions = new(StringComparer.Ordinal);
    private bool disposed;

    internal async Task<bool> RegisterAsync(string key, string connectionId, Action abort, ITicketStore store, string subject)
    {
        var session = Acquire(key, create: true)!;
        try
        {
            await session.Gate.WaitAsync();
            try
            {
                if (!IsValid(await store.RetrieveAsync(key), subject)) return false;
                return session.Connections.TryAdd(connectionId, abort);
            }
            finally { session.Gate.Release(); }
        }
        finally { Release(key, session); }
    }

    internal async Task<bool> SendIfActiveAsync(string key, string connectionId, ITicketStore store, string subject, Func<Task> send)
    {
        var session = Acquire(key, create: false);
        if (session is null) return false;
        try
        {
            await session.Gate.WaitAsync();
            try
            {
                if (!session.Connections.ContainsKey(connectionId) || !IsValid(await store.RetrieveAsync(key), subject)) return false;
                await send();
                return true;
            }
            finally { session.Gate.Release(); }
        }
        finally { Release(key, session); }
    }

    internal async Task<bool> IsActiveAsync(string key, string connectionId, ITicketStore store, string subject)
    {
        var session = Acquire(key, create: false);
        if (session is null) return false;
        try
        {
            await session.Gate.WaitAsync();
            try { return session.Connections.ContainsKey(connectionId) && IsValid(await store.RetrieveAsync(key), subject); }
            finally { session.Gate.Release(); }
        }
        finally { Release(key, session); }
    }

    internal async Task UnregisterAsync(string key, string connectionId)
    {
        var session = Acquire(key, create: false);
        if (session is null) return;
        try
        {
            await session.Gate.WaitAsync();
            try { session.Connections.TryRemove(connectionId, out _); }
            finally { session.Gate.Release(); }
        }
        finally { Release(key, session); }
    }

    internal async Task RevokeAsync(string key)
    {
        var session = Acquire(key, create: false);
        if (session is null) return;
        try
        {
            // Abort отменяет активную отправку до ожидания gate и не задерживает другие sessions.
            var aborted = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (id, abort) in session.Connections)
            {
                aborted.Add(id);
                abort();
            }
            await session.Gate.WaitAsync();
            try
            {
                foreach (var (id, abort) in session.Connections)
                    if (session.Connections.TryRemove(id, out _) && !aborted.Contains(id)) abort();
            }
            finally { session.Gate.Release(); }
        }
        finally { Release(key, session); }
    }

    private SessionState? Acquire(string key, bool create)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!sessions.TryGetValue(key, out var session))
            {
                if (!create) return null;
                session = new SessionState();
                sessions.Add(key, session);
            }
            session.Users++;
            return session;
        }
    }

    private void Release(string key, SessionState session)
    {
        lock (sync)
        {
            session.Users--;
            if (session.Users == 0 && (disposed || session.Connections.IsEmpty))
            {
                sessions.Remove(key);
                session.Dispose();
            }
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            disposed = true;
            foreach (var (key, session) in sessions.ToArray())
            {
                if (session.Users != 0) continue;
                sessions.Remove(key);
                session.Dispose();
            }
        }
    }

    private static bool IsValid(AuthenticationTicket? ticket, string subject) =>
        ticket is not null && string.Equals(ticket.Principal.FindFirst("sub")?.Value, subject, StringComparison.Ordinal)
        && !InMemoryAuthenticationTicketStore.TryGetLogoutIntentExpiry(ticket.Properties, out _);

    private sealed class SessionState : IDisposable
    {
        internal SemaphoreSlim Gate { get; } = new(1, 1);
        internal ConcurrentDictionary<string, Action> Connections { get; } = new(StringComparer.Ordinal);
        internal int Users { get; set; }
        public void Dispose() => Gate.Dispose();
    }
}
