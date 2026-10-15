namespace Intelligence.TradeSystem.Application.Realtime;

/// <summary>
/// Результат применения одного приватного потокового наблюдения.
/// </summary>
/// <remarks>
/// <see cref="ReasonCode"/> — машиночитаемая причина без текста ошибок биржи и бизнес-данных.
/// </remarks>
public sealed record RealtimeObservationApplyResult
{
    public const string UnsupportedObservation = "unsupported_observation";
    public const string UnsupportedCategory = "unsupported_category";
    public const string AmbiguousObservation = "ambiguous_observation";
    public const string InvalidIdentity = "invalid_identity";
    public const string IncompleteObservation = "incomplete_observation";
    public const string MissingObservationTime = "missing_observation_time";
    public const string MissingRevision = "missing_revision";
    public const string RevisionConflict = "revision_conflict";
    public const string OlderRevision = "older_revision";
    public const string OlderThanPosition = "older_than_position";
    public const string OlderThanPositionHistory = "older_than_position_history";
    public const string OlderThanClosure = "older_than_closure";
    public const string OlderThanBalance = "older_than_balance";
    public const string BalanceConflict = "balance_conflict";
    public const string SettlementMismatch = "settlement_mismatch";
    public const string RestConfirmationIncomplete = "rest_confirmation_incomplete";
    public const string RestConfirmationStale = "rest_confirmation_stale";
    public const string WalletUnconfirmed = "wallet_unconfirmed";
    public const string WalletIncomplete = "wallet_incomplete";
    public const string BufferOverflow = "buffer_overflow";

    private RealtimeObservationApplyResult(
        RealtimeObservationApplyOutcome outcome,
        string? reasonCode,
        bool confirmedByRest)
    {
        Outcome = outcome;
        ReasonCode = reasonCode;
        ConfirmedByRest = confirmedByRest;
    }

    public RealtimeObservationApplyOutcome Outcome { get; }

    public string? ReasonCode { get; }

    /// <summary>
    /// Состояние позиции подтверждено scoped REST-наблюдением, а не только потоковым сообщением.
    /// </summary>
    public bool ConfirmedByRest { get; }

    public static RealtimeObservationApplyResult Applied(bool confirmedByRest = false) =>
        new(RealtimeObservationApplyOutcome.Applied, null, confirmedByRest);

    public static RealtimeObservationApplyResult Duplicate() =>
        new(RealtimeObservationApplyOutcome.Duplicate, null, false);

    public static RealtimeObservationApplyResult Stale(string reasonCode) =>
        new(RealtimeObservationApplyOutcome.Stale, reasonCode, false);

    public static RealtimeObservationApplyResult Ignored(string reasonCode) =>
        new(RealtimeObservationApplyOutcome.Ignored, reasonCode, false);

    public static RealtimeObservationApplyResult RecoveryRequired(string reasonCode) =>
        new(RealtimeObservationApplyOutcome.RecoveryRequired, reasonCode, false);

    public static RealtimeObservationApplyResult GenerationSuperseded() =>
        new(RealtimeObservationApplyOutcome.GenerationSuperseded, null, false);

    public static RealtimeObservationApplyResult AccountUnavailable() =>
        new(RealtimeObservationApplyOutcome.AccountUnavailable, null, false);
}
