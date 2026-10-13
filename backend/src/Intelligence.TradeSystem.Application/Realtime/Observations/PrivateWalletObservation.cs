using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Realtime.Observations;

/// <summary>
/// Нормализованная запись приватного потока кошелька одного биржевого аккаунта.
/// </summary>
/// <remarks>
/// Account-level значения сохраняют биржевую USD-семантику; отсутствующие поля остаются
/// <see langword="null"/>. Сообщение кошелька не делает свежими рыночные показатели позиций.
/// </remarks>
public sealed record PrivateWalletObservation
{
    public required ExchangeId Exchange { get; init; }

    public required ExchangeAccountId ExchangeAccountId { get; init; }

    /// <summary>
    /// Балансы поддерживаемых типов аккаунта. Записи неподдерживаемых типов не включаются.
    /// </summary>
    public required IReadOnlyList<AccountBalance> Balances { get; init; }

    /// <summary>
    /// Время формирования сообщения биржей (<c>creationTime</c>); <see langword="null"/>, если
    /// достоверная отметка недоступна.
    /// </summary>
    public DateTimeOffset? MessageCreatedAt { get; init; }

    public required DateTimeOffset ReceivedAt { get; init; }

    public bool IsLatencyMeasurable => MessageCreatedAt is not null;
}
