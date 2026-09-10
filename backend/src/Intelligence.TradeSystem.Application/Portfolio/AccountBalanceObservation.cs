using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Application.Portfolio;

/// <summary>
/// Результат запроса баланса кошелька учётной записи.
/// </summary>
public sealed record AccountBalanceObservation
{
    private AccountBalanceObservation(
        AccountBalanceObservationStatus status,
        AccountBalance? balance,
        ExchangeFailure? failure,
        DateTimeOffset observedAt)
    {
        Status = status;
        Balance = balance;
        Failure = failure;
        ObservedAt = observedAt;
    }

    public AccountBalanceObservationStatus Status { get; }

    /// <summary>
    /// Сопоставленный баланс для результата <see cref="AccountBalanceObservationStatus.Complete"/>.
    /// </summary>
    public AccountBalance? Balance { get; }

    /// <summary>
    /// Нейтральное описание ошибки для результата <see cref="AccountBalanceObservationStatus.Failed"/>.
    /// </summary>
    public ExchangeFailure? Failure { get; }

    /// <summary>
    /// Момент, когда провайдер наблюдал баланс.
    /// </summary>
    public DateTimeOffset ObservedAt { get; }

    public static AccountBalanceObservation Complete(
        AccountBalance balance,
        DateTimeOffset? observedAt = null)
    {
        ArgumentNullException.ThrowIfNull(balance);
        return new(
            AccountBalanceObservationStatus.Complete,
            balance,
            null,
            observedAt ?? DateTimeOffset.UtcNow);
    }

    public static AccountBalanceObservation Failed(
        ExchangeFailure failure,
        DateTimeOffset? observedAt = null)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new(
            AccountBalanceObservationStatus.Failed,
            null,
            failure,
            observedAt ?? DateTimeOffset.UtcNow);
    }
}
