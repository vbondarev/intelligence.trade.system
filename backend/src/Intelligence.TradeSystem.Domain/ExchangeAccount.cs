using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Domain;

/// <summary>Конкретный подключённый биржевой аккаунт пользователя.</summary>
public sealed class ExchangeAccount
{
    private ExchangeAccount(
        ExchangeAccountId id,
        UserId userId,
        ExchangeId exchangeId,
        ExchangeAccountConnectionStatus connectionStatus,
        ExchangeAccountCapabilities capabilities,
        DateTimeOffset? lastSyncedAt,
        string? lastError)
    {
        Id = id;
        UserId = userId;
        ExchangeId = exchangeId;
        ConnectionStatus = connectionStatus;
        Capabilities = capabilities;
        LastSyncedAt = lastSyncedAt;
        LastError = lastError;
    }

    public ExchangeAccountId Id { get; }
    public UserId UserId { get; }
    public ExchangeId ExchangeId { get; }
    public ExchangeAccountConnectionStatus ConnectionStatus { get; private set; }
    public ExchangeAccountCapabilities Capabilities { get; }
    public DateTimeOffset? LastSyncedAt { get; private set; }
    public string? LastError { get; private set; }

    public static ExchangeAccount Create(
        ExchangeAccountId id,
        UserId userId,
        ExchangeId exchangeId,
        ExchangeAccountConnectionStatus connectionStatus = ExchangeAccountConnectionStatus.Unknown,
        ExchangeAccountCapabilities capabilities = ExchangeAccountCapabilities.None,
        DateTimeOffset? lastSyncedAt = null,
        string? lastError = null)
    {
        if (id == default)
            throw new ArgumentException("ExchangeAccountId must be initialized.", nameof(id));

        if (userId == default)
            throw new ArgumentException("UserId must be initialized.", nameof(userId));

        if (!Enum.IsDefined(exchangeId))
            throw new ArgumentOutOfRangeException(nameof(exchangeId), exchangeId, "ExchangeId must be defined.");

        if (!Enum.IsDefined(connectionStatus))
            throw new ArgumentOutOfRangeException(
                nameof(connectionStatus), connectionStatus, "Connection status must be defined.");

        const ExchangeAccountCapabilities allowedCapabilities =
            ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions;

        if ((capabilities & ~allowedCapabilities) != 0)
            throw new ArgumentOutOfRangeException(
                nameof(capabilities), capabilities, "Capabilities contain undefined flags.");

        return new ExchangeAccount(id, userId, exchangeId, connectionStatus, capabilities, lastSyncedAt, lastError);
    }

    public void MarkConnected()
    {
        EnsureNotDisabled();
        ConnectionStatus = ExchangeAccountConnectionStatus.Connected;
        LastError = null;
    }

    public void MarkUnavailable(string error)
    {
        EnsureNotDisabled();
        LastError = ValidateError(error);
        ConnectionStatus = ExchangeAccountConnectionStatus.Unavailable;
    }

    public void RecordSuccessfulSync(DateTimeOffset syncedAt)
    {
        EnsureNotDisabled();
        if (syncedAt == default)
            throw new ArgumentException("Sync timestamp must be initialized.", nameof(syncedAt));

        LastSyncedAt = syncedAt;
        LastError = null;
        ConnectionStatus = ExchangeAccountConnectionStatus.Connected;
    }

    public void RecordSyncFailure(string error) => MarkUnavailable(error);

    public void Disable()
    {
        ConnectionStatus = ExchangeAccountConnectionStatus.Disabled;
        LastError = null;
    }

    private void EnsureNotDisabled()
    {
        if (ConnectionStatus == ExchangeAccountConnectionStatus.Disabled)
        {
            throw new InvalidOperationException(
                "A disabled exchange account cannot be moved back to an active state.");
        }
    }

    private static string ValidateError(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return error;
    }
}
