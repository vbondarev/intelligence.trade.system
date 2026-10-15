using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Portfolio;

namespace Intelligence.TradeSystem.Application.Accounts;

public sealed record ExchangeAccountSyncResult(
    ExchangeAccountSyncOutcome Outcome,
    ExchangeAccount? Account,
    PortfolioState? PortfolioState)
{
    /// <summary>
    /// Текущая попытка зафиксировала транзакцию с полным REST-снимком: применёнными свежими балансом
    /// и позициями, полностью согласованными со всеми активными lifecycle.
    /// </summary>
    /// <remarks>
    /// В отличие от <see cref="PortfolioState"/>, признак не наследует полноту прежней сводки, поэтому
    /// только он доказывает, что этот запрос восстановил фактическое состояние аккаунта.
    /// </remarks>
    public bool FullReconciliationCommitted { get; private init; }

    /// <summary>
    /// Начало успешного REST-чтения баланса, принятого этой попыткой. Вместе с
    /// <see cref="BalanceReadCompletedAt"/> задаёт интервал неопределённого порядка для wallet.
    /// </summary>
    public DateTimeOffset? BalanceReadStartedAt { get; private init; }

    /// <summary>
    /// Завершение успешного REST-чтения баланса, принятого этой попыткой.
    /// </summary>
    public DateTimeOffset? BalanceReadCompletedAt { get; private init; }

    /// <summary>
    /// Транзакция этой попытки зафиксировала применение REST-наблюдения позиций
    /// (watermark Advanced как <see cref="ExchangeAccountObservationDisposition.Applied"/>).
    /// </summary>
    /// <remarks>
    /// Независим от <see cref="Outcome"/> и <see cref="FullReconciliationCommitted"/>: баланс мог
    /// завершиться Failed, а позиции — Complete или Partial. Признак не означает полноты сверки
    /// аккаунта; coordinator использует его только как доказательство временного порядка.
    /// </remarks>
    public bool AcceptedPositionsObservation { get; private init; }

    public static ExchangeAccountSyncResult Synchronized(
        ExchangeAccount account,
        PortfolioState portfolioState) =>
        new(ExchangeAccountSyncOutcome.Synchronized, account, portfolioState);

    internal static ExchangeAccountSyncResult FullyReconciled(
        ExchangeAccount account,
        PortfolioState portfolioState) =>
        Synchronized(account, portfolioState) with { FullReconciliationCommitted = true };

    internal ExchangeAccountSyncResult WithAcceptedBalanceRead(
        DateTimeOffset? startedAt,
        DateTimeOffset? completedAt) =>
        this with
        {
            BalanceReadStartedAt = startedAt,
            BalanceReadCompletedAt = completedAt,
        };

    internal ExchangeAccountSyncResult WithAcceptedPositionsObservation(bool accepted) =>
        this with { AcceptedPositionsObservation = accepted };

    public static ExchangeAccountSyncResult NotFound() =>
        new(ExchangeAccountSyncOutcome.NotFound, null, null);

    public static ExchangeAccountSyncResult AccountDisabled() =>
        new(ExchangeAccountSyncOutcome.AccountDisabled, null, null);

    public static ExchangeAccountSyncResult CredentialsUnavailable() =>
        new(ExchangeAccountSyncOutcome.CredentialsUnavailable, null, null);

    public static ExchangeAccountSyncResult ExchangeUnavailable(
        ExchangeAccount account) =>
        new(ExchangeAccountSyncOutcome.ExchangeUnavailable, account, null);

    public static ExchangeAccountSyncResult ExchangeUnavailable() =>
        new(ExchangeAccountSyncOutcome.ExchangeUnavailable, null, null);

    public static ExchangeAccountSyncResult AlreadyApplied(ExchangeAccount account) =>
        new(ExchangeAccountSyncOutcome.AlreadyApplied, account, null);

    public static ExchangeAccountSyncResult Superseded(ExchangeAccount account) =>
        new(ExchangeAccountSyncOutcome.Superseded, account, null);
}
