using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Domain;

/// <summary>Конкретная подключённая биржевая учётная запись пользователя.</summary>
public sealed class ExchangeAccount
{
    /// <summary>Максимальная длина нормализованного <see cref="DisplayName"/>.</summary>
    public const int DisplayNameMaxLength = 100;

    private ExchangeAccount(
        ExchangeAccountId id,
        UserId userId,
        ExchangeId exchangeId,
        ExchangeAccountProviderIdentity providerIdentity,
        string displayName,
        ExchangeAccountConnectionStatus connectionStatus,
        ExchangeAccountCapabilities capabilities,
        DateTimeOffset? lastSyncedAt,
        string? lastError,
        DateTimeOffset? lastAppliedBalanceObservationAt,
        DateTimeOffset? lastAppliedPositionsObservationAt)
    {
        Id = id;
        UserId = userId;
        ExchangeId = exchangeId;
        ProviderIdentity = providerIdentity;
        DisplayName = displayName;
        ConnectionStatus = connectionStatus;
        Capabilities = capabilities;
        LastSyncedAt = lastSyncedAt;
        LastError = lastError;
        LastAppliedBalanceObservationAt = lastAppliedBalanceObservationAt;
        LastAppliedPositionsObservationAt = lastAppliedPositionsObservationAt;
    }

    public ExchangeAccountId Id { get; }
    public UserId UserId { get; }
    public ExchangeId ExchangeId { get; }
    public ExchangeAccountProviderIdentity ProviderIdentity { get; }

    /// <summary>
    /// Пользовательское отображаемое имя подключения.
    /// </summary>
    /// <remarks>
    /// Это редактируемые пользовательские метаданные: имя не участвует в provider identity,
    /// не обязано быть уникальным и не зависит от credentials или lifecycle подключения.
    /// </remarks>
    public string DisplayName { get; private set; }

    public ExchangeAccountConnectionStatus ConnectionStatus { get; private set; }
    public ExchangeAccountCapabilities Capabilities { get; }
    public DateTimeOffset? LastSyncedAt { get; private set; }
    public string? LastError { get; private set; }
    /// <summary>
    /// Самое новое принятое наблюдение баланса. Оно не зависит от
    /// <see cref="LastSyncedAt"/>, который фиксирует только полностью успешную системную синхронизацию.
    /// </summary>
    public DateTimeOffset? LastAppliedBalanceObservationAt { get; private set; }
    public DateTimeOffset? LastAppliedPositionsObservationAt { get; private set; }

    public static ExchangeAccount Create(
        ExchangeAccountId id,
        UserId userId,
        ExchangeId exchangeId,
        ExchangeAccountProviderIdentity providerIdentity,
        string displayName,
        ExchangeAccountConnectionStatus connectionStatus = ExchangeAccountConnectionStatus.Unknown,
        ExchangeAccountCapabilities capabilities = ExchangeAccountCapabilities.None,
        DateTimeOffset? lastSyncedAt = null,
        string? lastError = null,
        DateTimeOffset? lastAppliedBalanceObservationAt = null,
        DateTimeOffset? lastAppliedPositionsObservationAt = null)
    {
        if (id == default)
            throw new ArgumentException("ExchangeAccountId must be initialized.", nameof(id));

        if (userId == default)
            throw new ArgumentException("UserId must be initialized.", nameof(userId));

        if (!Enum.IsDefined(exchangeId))
            throw new ArgumentOutOfRangeException(nameof(exchangeId), exchangeId, "ExchangeId must be defined.");

        if (providerIdentity == default)
            throw new ArgumentException(
                "Exchange account provider identity must be initialized.",
                nameof(providerIdentity));

        var normalizedDisplayName = NormalizeDisplayName(displayName);

        if (!Enum.IsDefined(connectionStatus))
            throw new ArgumentOutOfRangeException(
                nameof(connectionStatus), connectionStatus, "Connection status must be defined.");

        const ExchangeAccountCapabilities allowedCapabilities =
            ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions;

        if ((capabilities & ~allowedCapabilities) != 0)
            throw new ArgumentOutOfRangeException(
                nameof(capabilities), capabilities, "Capabilities contain undefined flags.");

        if (lastAppliedBalanceObservationAt == default(DateTimeOffset))
            throw new ArgumentException(
                "Last applied balance observation timestamp must be initialized when provided.",
                nameof(lastAppliedBalanceObservationAt));

        if (lastAppliedPositionsObservationAt == default(DateTimeOffset))
            throw new ArgumentException(
                "Last applied positions observation timestamp must be initialized when provided.",
                nameof(lastAppliedPositionsObservationAt));

        return new ExchangeAccount(
            id,
            userId,
            exchangeId,
            providerIdentity,
            normalizedDisplayName,
            connectionStatus,
            capabilities,
            lastSyncedAt,
            lastError,
            NormalizeObservationTimestamp(lastAppliedBalanceObservationAt),
            NormalizeObservationTimestamp(lastAppliedPositionsObservationAt));
    }

    /// <summary>
    /// Изменяет пользовательское отображаемое имя подключения.
    /// </summary>
    /// <remarks>
    /// Переименование допустимо в любом lifecycle state, включая
    /// <see cref="ExchangeAccountConnectionStatus.Disabled"/>, и не затрагивает identity,
    /// состояние подключения, capabilities и состояние синхронизации.
    /// </remarks>
    /// <returns><c>true</c>, если нормализованное имя отличается от текущего.</returns>
    public bool Rename(string displayName)
    {
        var normalized = NormalizeDisplayName(displayName);
        if (string.Equals(DisplayName, normalized, StringComparison.Ordinal))
            return false;

        DisplayName = normalized;
        return true;
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

    public ExchangeAccountObservationDisposition AdvanceObservationWatermark(
        ExchangeAccountObservationResource resource,
        DateTimeOffset observationAt)
    {
        EnsureNotDisabled();
        if (!Enum.IsDefined(resource))
            throw new ArgumentOutOfRangeException(
                nameof(resource),
                resource,
                "Observation resource must be defined.");

        if (observationAt == default)
            throw new ArgumentException(
                "Observation timestamp must be initialized.",
                nameof(observationAt));

        var normalizedObservationAt = NormalizeObservationTimestamp(observationAt)!.Value;
        var current = resource == ExchangeAccountObservationResource.Balance
            ? LastAppliedBalanceObservationAt
            : LastAppliedPositionsObservationAt;
        if (current is { } currentValue)
        {
            if (normalizedObservationAt < currentValue)
                return ExchangeAccountObservationDisposition.Superseded;

            if (normalizedObservationAt == currentValue)
                return ExchangeAccountObservationDisposition.AlreadyApplied;
        }

        if (resource == ExchangeAccountObservationResource.Balance)
            LastAppliedBalanceObservationAt = normalizedObservationAt;
        else
            LastAppliedPositionsObservationAt = normalizedObservationAt;

        return ExchangeAccountObservationDisposition.Applied;
    }

    public void Disable()
    {
        ConnectionStatus = ExchangeAccountConnectionStatus.Disabled;
        LastError = null;
    }

    /// <summary>
    /// Восстанавливает отключённое подключение того же provider-side аккаунта после успешной
    /// проверки новых credentials.
    /// </summary>
    /// <remarks>
    /// Это единственный переход, который выводит аккаунт из <see cref="ExchangeAccountConnectionStatus.Disabled"/>.
    /// Identity, capabilities, время последней синхронизации и observation watermarks сохраняются,
    /// чтобы история оставалась связанной с прежним <see cref="Id"/>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Аккаунт не находится в состоянии Disabled.</exception>
    public void Reconnect()
    {
        if (ConnectionStatus != ExchangeAccountConnectionStatus.Disabled)
        {
            throw new InvalidOperationException(
                "Повторно подключить можно только отключённый биржевой аккаунт.");
        }

        ConnectionStatus = ExchangeAccountConnectionStatus.Connected;
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

    private static string NormalizeDisplayName(string displayName)
    {
        ArgumentNullException.ThrowIfNull(displayName);
        var normalized = displayName.Trim();
        if (normalized.Length == 0)
        {
            throw new ArgumentException(
                "Отображаемое имя биржевого аккаунта не может быть пустым.",
                nameof(displayName));
        }

        if (normalized.Length > DisplayNameMaxLength)
        {
            throw new ArgumentException(
                $"Отображаемое имя биржевого аккаунта не может быть длиннее {DisplayNameMaxLength} символов.",
                nameof(displayName));
        }

        return normalized;
    }

    private static string ValidateError(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return error;
    }

    private static DateTimeOffset? NormalizeObservationTimestamp(DateTimeOffset? timestamp)
    {
        if (timestamp is not { } value)
            return null;

        var utc = value.ToUniversalTime();
        var ticks = utc.Ticks - utc.Ticks % TimeSpan.TicksPerMicrosecond;
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }
}
