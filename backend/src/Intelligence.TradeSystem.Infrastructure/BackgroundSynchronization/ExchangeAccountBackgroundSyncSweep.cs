using System.Collections.Concurrent;
using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Domain.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Intelligence.TradeSystem.Infrastructure.BackgroundSynchronization;

public sealed class ExchangeAccountBackgroundSyncSweep(
    IServiceScopeFactory scopeFactory,
    IOptions<ExchangeAccountBackgroundSyncOptions> options,
    TimeProvider timeProvider,
    ILogger<ExchangeAccountBackgroundSyncSweep> logger)
    : IExchangeAccountBackgroundSyncSweep
{
    private readonly ExchangeAccountBackgroundSyncOptions settings = options.Value;

    public async Task<ExchangeAccountBackgroundSyncSweepResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var outcomeCounts = new ConcurrentDictionary<ExchangeAccountSyncOutcome, int>();
        var counters = new SweepCounters();
        var candidateLoadFailed = false;
        ExchangeAccountId? after = null;
        var seenAccountIds = new HashSet<ExchangeAccountId>();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<ExchangeAccountSyncCandidate> candidates;
            try
            {
                candidates = await LoadBatchAsync(after, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                candidateLoadFailed = true;
                ExchangeAccountBackgroundSyncLogMessages.LogCandidateLoadFailed(
                    logger,
                    exception.GetType().FullName);
                break;
            }

            if (candidates.Count == 0)
            {
                break;
            }

            var batch = candidates
                .Where(candidate => seenAccountIds.Add(candidate.ExchangeAccountId))
                .ToArray();
            counters.CandidateCount += batch.Length;

            if (batch.Length > 0)
            {
                await ProcessBatchAsync(
                        batch,
                        outcomeCounts,
                        counters,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            var nextAfter = candidates[^1].ExchangeAccountId;
            if (after == nextAfter)
            {
                ExchangeAccountBackgroundSyncLogMessages.LogCursorDidNotAdvance(logger);
                break;
            }

            after = nextAfter;
            if (candidates.Count < settings.BatchSize)
            {
                break;
            }
        }

        return new ExchangeAccountBackgroundSyncSweepResult(
            counters.CandidateCount,
            counters.ProcessedCount,
            counters.UnexpectedFailureCount,
            candidateLoadFailed,
            new Dictionary<ExchangeAccountSyncOutcome, int>(outcomeCounts));
    }

    private async Task<IReadOnlyList<ExchangeAccountSyncCandidate>> LoadBatchAsync(
        ExchangeAccountId? after,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var source = scope.ServiceProvider
            .GetRequiredService<IExchangeAccountSyncCandidateSource>();
        return await source
            .GetBatchAsync(after, settings.BatchSize, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task ProcessBatchAsync(
        IReadOnlyList<ExchangeAccountSyncCandidate> candidates,
        ConcurrentDictionary<ExchangeAccountSyncOutcome, int> outcomeCounts,
        SweepCounters counters,
        CancellationToken cancellationToken)
    {
        await Parallel.ForEachAsync(
                candidates,
                new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = settings.MaxConcurrency,
                },
                async (candidate, accountCancellationToken) =>
                {
                    Interlocked.Increment(ref counters.ProcessedCount);
                    await ProcessAccountAsync(
                            candidate,
                            outcomeCounts,
                            counters,
                            accountCancellationToken)
                        .ConfigureAwait(false);
                })
            .ConfigureAwait(false);
    }

    private async Task ProcessAccountAsync(
        ExchangeAccountSyncCandidate candidate,
        ConcurrentDictionary<ExchangeAccountSyncOutcome, int> outcomeCounts,
        SweepCounters counters,
        CancellationToken cancellationToken)
    {
        try
        {
            ExchangeAccountSyncResult result;
            await using (var scope = scopeFactory.CreateAsyncScope())
            {
                var syncService = scope.ServiceProvider
                    .GetRequiredService<IExchangeAccountSyncService>();
                result = await syncService
                    .SynchronizeAsync(
                        candidate.UserId,
                        candidate.ExchangeAccountId,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            outcomeCounts.AddOrUpdate(result.Outcome, 1, (_, count) => count + 1);
            LogOutcome(candidate, result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Interlocked.Increment(ref counters.UnexpectedFailureCount);
            ExchangeAccountBackgroundSyncLogMessages.LogAccountFailed(
                logger,
                candidate.UserId.Value,
                candidate.ExchangeAccountId.Value,
                exception.GetType().FullName);
        }
    }

    private void LogOutcome(
        ExchangeAccountSyncCandidate candidate,
        ExchangeAccountSyncResult result)
    {
        var lastSuccessfulSyncAt = result.Account?.LastSyncedAt ?? candidate.LastSyncedAt;
        var lastSuccessfulSyncAge = lastSuccessfulSyncAt is { } syncAt
            ? Math.Max(0, (timeProvider.GetUtcNow() - syncAt).TotalSeconds)
            : (double?)null;

        ExchangeAccountBackgroundSyncTelemetry.RecordAccountAttempt(
            result.Outcome,
            lastSuccessfulSyncAge);

        switch (result.Outcome)
        {
            case ExchangeAccountSyncOutcome.Synchronized:
                ExchangeAccountBackgroundSyncLogMessages.LogAccountCompleted(
                    logger,
                    result.Outcome,
                    candidate.UserId.Value,
                    candidate.ExchangeAccountId.Value,
                    lastSuccessfulSyncAt,
                    lastSuccessfulSyncAge);
                break;
            case ExchangeAccountSyncOutcome.ExchangeUnavailable:
            case ExchangeAccountSyncOutcome.CredentialsUnavailable:
                ExchangeAccountBackgroundSyncLogMessages.LogAccountDegraded(
                    logger,
                    result.Outcome,
                    candidate.UserId.Value,
                    candidate.ExchangeAccountId.Value,
                    lastSuccessfulSyncAt,
                    lastSuccessfulSyncAge);
                break;
            case ExchangeAccountSyncOutcome.AccountDisabled:
            case ExchangeAccountSyncOutcome.NotFound:
            case ExchangeAccountSyncOutcome.AlreadyApplied:
            case ExchangeAccountSyncOutcome.Superseded:
                ExchangeAccountBackgroundSyncLogMessages.LogAccountCompleted(
                    logger,
                    result.Outcome,
                    candidate.UserId.Value,
                    candidate.ExchangeAccountId.Value,
                    lastSuccessfulSyncAt,
                    lastSuccessfulSyncAge);
                break;
            default:
                ExchangeAccountBackgroundSyncLogMessages.LogUnknownOutcome(
                    logger,
                    result.Outcome,
                    candidate.UserId.Value,
                    candidate.ExchangeAccountId.Value);
                break;
        }
    }

    private sealed class SweepCounters
    {
        public int CandidateCount;
        public int ProcessedCount;
        public int UnexpectedFailureCount;
    }
}
