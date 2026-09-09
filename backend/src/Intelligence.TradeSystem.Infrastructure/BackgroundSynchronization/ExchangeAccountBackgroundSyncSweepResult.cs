using Intelligence.TradeSystem.Application.Accounts;

namespace Intelligence.TradeSystem.Infrastructure.BackgroundSynchronization;

public sealed record ExchangeAccountBackgroundSyncSweepResult(
    int CandidateCount,
    int ProcessedCount,
    int UnexpectedFailureCount,
    bool CandidateLoadFailed,
    IReadOnlyDictionary<ExchangeAccountSyncOutcome, int> OutcomeCounts);
