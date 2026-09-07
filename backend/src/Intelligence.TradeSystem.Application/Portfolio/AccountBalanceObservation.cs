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
        ExchangeFailure? failure)
    {
        Status = status;
        Balance = balance;
        Failure = failure;
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

    public static AccountBalanceObservation Complete(AccountBalance balance)
    {
        ArgumentNullException.ThrowIfNull(balance);
        return new(AccountBalanceObservationStatus.Complete, balance, null);
    }

    public static AccountBalanceObservation Failed(ExchangeFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new(AccountBalanceObservationStatus.Failed, null, failure);
    }
}
