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
        DateTimeOffset observedAt,
        DateTimeOffset? requestStartedAt)
    {
        Status = status;
        Balance = balance;
        Failure = failure;
        ObservedAt = observedAt;
        RequestStartedAt = requestStartedAt;
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
    /// Момент, к которому относится полученное значение баланса по часам backend.
    /// Для успешного чтения это время получения ответа, а не отправки запроса.
    /// </summary>
    public DateTimeOffset ObservedAt { get; }

    /// <summary>
    /// Момент начала REST-запроса баланса по часам backend, если провайдер его зафиксировал.
    /// Вместе с <see cref="ObservedAt"/> образует интервал, внутри которого порядок относительно
    /// потокового wallet-наблюдения может быть недоказан.
    /// </summary>
    public DateTimeOffset? RequestStartedAt { get; }

    public static AccountBalanceObservation Complete(
        AccountBalance balance,
        DateTimeOffset? observedAt = null,
        DateTimeOffset? requestStartedAt = null)
    {
        ArgumentNullException.ThrowIfNull(balance);
        var completedAt = observedAt ?? DateTimeOffset.UtcNow;
        return new(
            AccountBalanceObservationStatus.Complete,
            balance,
            null,
            completedAt,
            requestStartedAt);
    }

    public static AccountBalanceObservation Failed(
        ExchangeFailure failure,
        DateTimeOffset? observedAt = null,
        DateTimeOffset? requestStartedAt = null)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new(
            AccountBalanceObservationStatus.Failed,
            null,
            failure,
            observedAt ?? DateTimeOffset.UtcNow,
            requestStartedAt);
    }
}
