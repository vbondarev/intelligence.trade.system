using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Application.Portfolio;

/// <summary>
/// Result of requesting an account wallet balance.
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
    /// The mapped balance for a <see cref="AccountBalanceObservationStatus.Complete"/> result.
    /// </summary>
    public AccountBalance? Balance { get; }

    /// <summary>
    /// The neutral failure for a <see cref="AccountBalanceObservationStatus.Failed"/> result.
    /// </summary>
    public ExchangeFailure? Failure { get; }

    /// <summary>
    /// The time at which the provider observed the balance.
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
