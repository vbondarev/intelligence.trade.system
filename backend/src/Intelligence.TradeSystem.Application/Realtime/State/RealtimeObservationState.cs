using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Realtime.State;

/// <summary>
/// Сохранённое состояние приватного realtime-наблюдения одного биржевого аккаунта.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Generation"/> монотонно растёт при каждом запуске, остановке или смене credentials
/// наблюдателя. Запись состояния и курсоров принимается только от текущего поколения, поэтому
/// наблюдатель прежнего поколения не может применить устаревшие сообщения.
/// </para>
/// <para>
/// Состояние описывает транспорт и полноту наблюдения, а не свежесть фактических или рыночных
/// данных. Оно не содержит credentials, сообщений биржи и сырых payload.
/// </para>
/// </remarks>
public sealed record RealtimeObservationState
{
    public const int FailureCodeMaxLength = 64;

    public required ExchangeAccountId ExchangeAccountId
    {
        get;
        init => field = value != default
            ? value
            : throw new ArgumentException("ExchangeAccountId must be initialized.", nameof(ExchangeAccountId));
    }

    public required long Generation
    {
        get;
        init => field = value > 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(Generation), value, "Generation must be positive.");
    }

    public required RealtimeObservationTransportStatus TransportStatus
    {
        get;
        init => field = Enum.IsDefined(value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(TransportStatus), value, "Unknown transport status.");
    }

    /// <summary>Время последнего подтверждения подписок.</summary>
    public DateTimeOffset? LastConnectedAt { get; init; }

    /// <summary>Время последнего подтверждения работоспособности транспорта текущего поколения.</summary>
    public DateTimeOffset? LastHeartbeatAt { get; init; }

    /// <summary>Время последней успешной полной REST-сверки, подтвердившей полноту фактического состояния.</summary>
    public DateTimeOffset? LastFullyReconciledAt { get; init; }

    /// <summary>Требуется ли REST recovery: потоковые наблюдения могли быть пропущены.</summary>
    public required bool RecoveryRequired { get; init; }

    /// <summary>
    /// Нормализованный машиночитаемый код причины деградации или остановки; не содержит текста
    /// ошибки биржи. Допустимы ASCII-буквы, цифры, <c>_</c>, <c>.</c> и <c>-</c>; первый символ — буква.
    /// </summary>
    public string? FailureCode
    {
        get;
        init => field = IsValidFailureCode(value)
            ? value
            : throw new ArgumentException("FailureCode must be a short machine-readable code.", nameof(FailureCode));
    }

    /// <summary>
    /// Подтверждает ли состояние работающий транспорт на момент <paramref name="now"/>: требуется
    /// <see cref="RealtimeObservationTransportStatus.Connected"/> и heartbeat не старше
    /// <paramref name="heartbeatStaleAfter"/>. Сохранённый признак подключения без свежего
    /// heartbeat, например после перезапуска процесса, работу транспорта не подтверждает.
    /// Heartbeat позже <paramref name="now"/> недостоверен и работу транспорта также не подтверждает.
    /// </summary>
    public bool IsTransportOperational(DateTimeOffset now, TimeSpan heartbeatStaleAfter)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(heartbeatStaleAfter, TimeSpan.Zero);

        if (TransportStatus != RealtimeObservationTransportStatus.Connected || LastHeartbeatAt is not { } heartbeat)
        {
            return false;
        }

        var age = now - heartbeat;
        return age >= TimeSpan.Zero && age <= heartbeatStaleAfter;
    }

    private static bool IsValidFailureCode(string? value)
    {
        if (value is null)
        {
            return true;
        }

        if (value.Length is 0 or > FailureCodeMaxLength || !char.IsAsciiLetter(value[0]))
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('_' or '.' or '-'))
            {
                return false;
            }
        }

        return true;
    }
}
