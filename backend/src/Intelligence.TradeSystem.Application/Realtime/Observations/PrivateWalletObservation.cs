using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Realtime.Observations;

/// <summary>
/// Нормализованная запись приватного потока кошелька одного биржевого аккаунта.
/// </summary>
/// <remarks>
/// Account-level значения сохраняют биржевую USD-семантику; отсутствующие поля остаются
/// <see langword="null"/>. Сообщение кошелька не делает свежими рыночные показатели позиций и не
/// заменяет полный REST-снимок баланса.
/// </remarks>
public sealed record PrivateWalletObservation
{
    public required ExchangeId Exchange { get; init; }

    public required ExchangeAccountId ExchangeAccountId { get; init; }

    /// <summary>
    /// <see cref="PrivateWalletObservationKind.PartialUpdate"/> только при непустом
    /// <see cref="Balances"/>; пустой <see cref="Balances"/> всегда сопровождается
    /// <see cref="PrivateWalletObservationKind.Unconfirmed"/>.
    /// </summary>
    public required PrivateWalletObservationKind Kind { get; init; }

    /// <summary>
    /// Применимые балансы поддерживаемых типов аккаунта. Записи неподдерживаемых типов и записи
    /// без значений не включаются, поэтому пустой список не означает нулевого баланса.
    /// </summary>
    public required IReadOnlyList<AccountBalance> Balances { get; init; }

    /// <summary>
    /// Время формирования сообщения биржей (<c>creationTime</c>); <see langword="null"/>, если
    /// достоверная отметка недоступна.
    /// </summary>
    public DateTimeOffset? MessageCreatedAt { get; init; }

    public required DateTimeOffset ReceivedAt { get; init; }

    /// <summary>
    /// Наблюдение не подтверждает состояние кошелька, и его актуальность нужно восстановить
    /// REST-сверкой.
    /// </summary>
    public bool RequiresRestReconciliation => Kind == PrivateWalletObservationKind.Unconfirmed;

    public bool IsLatencyMeasurable => MessageCreatedAt is not null;
}
