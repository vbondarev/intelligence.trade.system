using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Realtime.State;

/// <summary>
/// Последняя принятая ревизия частичных потоковых наблюдений позиции.
/// </summary>
/// <remarks>
/// <para>
/// Курсор не зависит от жизненного цикла позиции и сохраняется после её закрытия, чтобы
/// устаревшее событие не было применено повторно. Он не сравнивается с
/// <c>ExchangeAccount.LastAppliedPositionsObservationAt</c> полной REST-синхронизации.
/// </para>
/// <para>
/// Хранилище не допускает регресса известных компонентов ревизии: сохранённые
/// <see cref="LastProviderSequence"/> и <see cref="LastProviderUpdatedAt"/> не уменьшаются и не
/// сбрасываются в <see langword="null"/>. Хотя бы один из них обязателен.
/// </para>
/// <para>
/// Пара <see cref="LastDirectAppliedPositionId"/> и
/// <see cref="LastDirectAppliedPositionVersion"/> связывает последний direct WS с версией
/// сохранённой позиции. REST confirmation и cursor-only observations не оставляют такое доказательство.
/// </para>
/// </remarks>
public sealed record PositionObservationCursor
{
    public const int FingerprintMaxLength = 128;

    public required PositionObservationCursorKey Key
    {
        get;
        init => field = value.ExchangeAccountId != default
            ? value
            : throw new ArgumentException("Cursor key must be initialized.", nameof(Key));
    }

    /// <summary>Биржевая последовательность последнего принятого наблюдения.</summary>
    public long? LastProviderSequence
    {
        get;
        init => field = value is null or > 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(LastProviderSequence), value, "Sequence must be positive.");
    }

    /// <summary>Время изменения позиции по часам биржи для последнего принятого наблюдения.</summary>
    public DateTimeOffset? LastProviderUpdatedAt { get; init; }

    /// <summary>
    /// Время формирования сообщения биржей (<c>creationTime</c>) последнего принятого наблюдения;
    /// не является компонентом порядка.
    /// </summary>
    public DateTimeOffset? LastMessageCreatedAt { get; init; }

    /// <summary>
    /// Отпечаток нормализованного бизнес-содержания последнего принятого наблюдения для распознавания
    /// повторов. Допустимы ASCII-буквы, цифры, <c>:</c>, <c>_</c>, <c>.</c> и <c>-</c>; сырой payload
    /// сохранить нельзя.
    /// </summary>
    public required string LastAcceptedFingerprint
    {
        get;
        init => field = IsValidFingerprint(value)
            ? value
            : throw new ArgumentException("Fingerprint must be a short ASCII token.", nameof(LastAcceptedFingerprint));
    }

    /// <summary>Идентификатор позиции, последний раз непосредственно сохранённой по WS.</summary>
    public PositionId? LastDirectAppliedPositionId { get; init; }

    /// <summary>Версия позиции, сохранённая вместе с последним непосредственным применением WS.</summary>
    public ConcurrencyVersion? LastDirectAppliedPositionVersion { get; init; }

    /// <summary>Известен ли хотя бы один компонент ревизии, необходимый для сохранения курсора.</summary>
    public bool HasRevision => LastProviderSequence is not null || LastProviderUpdatedAt is not null;

    /// <summary>Образуют ли поля direct-WS доказательства допустимую пару.</summary>
    public bool HasValidDirectAppliedPositionStamp =>
        (LastDirectAppliedPositionId, LastDirectAppliedPositionVersion) switch
        {
            (null, null) => true,
            ({ } positionId, { Value: > 0 }) => positionId.Value != Guid.Empty,
            _ => false,
        };

    private static bool IsValidFingerprint(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > FingerprintMaxLength)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not (':' or '_' or '.' or '-'))
            {
                return false;
            }
        }

        return true;
    }
}
