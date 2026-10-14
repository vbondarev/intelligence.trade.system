using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Application.Realtime.Observations;

/// <summary>
/// Нормализованная запись приватного потока позиций одного биржевого аккаунта.
/// </summary>
/// <remarks>
/// <para>
/// Частичное наблюдение одной позиции не заменяет полную REST-синхронизацию аккаунта и не
/// сравнивается с <c>ExchangeAccount.LastAppliedPositionsObservationAt</c>. Порядок определяется
/// парой <see cref="ProviderSequence"/> и <see cref="ProviderUpdatedAt"/> в пределах ключа позиции;
/// последовательности разных инструментов несравнимы.
/// </para>
/// <para>
/// Денежные показатели <see cref="Position"/> выражены в <see cref="SettlementAsset"/>.
/// </para>
/// </remarks>
public sealed record PrivatePositionObservation
{
    public required ExchangeId Exchange { get; init; }

    /// <summary>Аккаунт, для которого создан приватный поток.</summary>
    public required ExchangeAccountId ExchangeAccountId { get; init; }

    public required PrivatePositionObservationKind Kind { get; init; }

    /// <summary>
    /// Причина для <see cref="PrivatePositionObservationKind.Unsupported"/> и
    /// <see cref="PrivatePositionObservationKind.Ambiguous"/>; иначе <see langword="null"/>.
    /// </summary>
    public PrivatePositionObservationIssue? Issue { get; init; }

    /// <summary>Символ в том виде, в котором его прислала биржа.</summary>
    public required string Symbol { get; init; }

    /// <summary>Категория рынка; <see langword="null"/>, если биржевое значение неизвестно.</summary>
    public MarketCategory? Category { get; init; }

    public required int PositionIdx { get; init; }

    public required PositionSide Side { get; init; }

    public required decimal Size { get; init; }

    /// <summary>
    /// Биржевая последовательность позиции; <see langword="null"/>, если биржа её не передала.
    /// Одно значение может повторяться при изменении настроек позиции без сделки.
    /// </summary>
    public long? ProviderSequence { get; init; }

    public DateTimeOffset? ProviderUpdatedAt { get; init; }

    public DateTimeOffset? ProviderCreatedAt { get; init; }

    /// <summary>
    /// Время формирования сообщения биржей (<c>creationTime</c>); <see langword="null"/>, если
    /// достоверная отметка недоступна. Не подменяется временем получения.
    /// </summary>
    public DateTimeOffset? MessageCreatedAt { get; init; }

    /// <summary>Время получения сообщения backend.</summary>
    public required DateTimeOffset ReceivedAt { get; init; }

    /// <summary>
    /// Актив расчёта из подтверждённых метаданных инструмента; <see langword="null"/>, если не подтверждён.
    /// </summary>
    public SettlementAsset? SettlementAsset { get; init; }

    /// <summary>
    /// Нормализованная позиция с биржевыми финансовыми показателями. Заполняется только для
    /// <see cref="PrivatePositionObservationKind.Open"/> и его повтора
    /// <see cref="PrivatePositionObservationKind.Duplicate"/>.
    /// </summary>
    public OpenPosition? Position { get; init; }

    /// <summary>
    /// Пригодно ли наблюдение для измерения задержки доставки: требуется достоверный <c>creationTime</c>.
    /// </summary>
    public bool IsLatencyMeasurable => MessageCreatedAt is not null;
}
