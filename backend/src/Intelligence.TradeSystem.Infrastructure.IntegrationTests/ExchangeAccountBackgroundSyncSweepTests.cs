using System.Collections.Concurrent;
using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Portfolio;
using Intelligence.TradeSystem.Infrastructure.BackgroundSynchronization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests;

public sealed class ExchangeAccountBackgroundSyncSweepTests
{
    [Fact]
    public async Task Processes_each_candidate_with_the_explicit_user_and_account_ids()
    {
        var candidates = CreateCandidates(3);
        var tracker = new SyncTracker();
        await using var fixture = CreateFixture(candidates, tracker);

        var result = await fixture.Sweep.RunAsync();

        Assert.Equal(3, result.CandidateCount);
        Assert.Equal(3, result.ProcessedCount);
        Assert.Equal(3, tracker.Calls.Count);
        Assert.Equal(
            candidates
                .Select(candidate => (candidate.UserId.Value, candidate.ExchangeAccountId.Value))
                .Order(),
            tracker.Calls
                .Select(call => (call.UserId.Value, call.ExchangeAccountId.Value))
                .Order());
        Assert.Equal(3, tracker.CreatedServiceIds.Distinct().Count());
        Assert.Equal(3, tracker.DisposedServiceIds.Distinct().Count());
    }

    [Fact]
    public async Task Limits_parallel_account_syncs_to_the_configured_concurrency()
    {
        var candidates = CreateCandidates(4);
        var tracker = new SyncTracker();
        var startedTwo = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        tracker.Handler = async (candidate, cancellationToken) =>
        {
            var active = Interlocked.Increment(ref tracker.ActiveCount);
            tracker.MaxActiveCount = Math.Max(tracker.MaxActiveCount, active);
            if (active == 2)
            {
                startedTwo.TrySetResult(true);
            }

            try
            {
                await release.Task.WaitAsync(cancellationToken);
                return ExchangeAccountSyncResult.NotFound();
            }
            finally
            {
                Interlocked.Decrement(ref tracker.ActiveCount);
            }
        };
        await using var fixture = CreateFixture(
            candidates,
            tracker,
            new ExchangeAccountBackgroundSyncOptions
            {
                BatchSize = 4,
                MaxConcurrency = 2,
            });

        var sweepTask = fixture.Sweep.RunAsync();
        await startedTwo.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, tracker.MaxActiveCount);
        Assert.Equal(2, tracker.Calls.Count);

        release.SetResult(true);
        var result = await sweepTask;

        Assert.Equal(4, result.ProcessedCount);
        Assert.Equal(2, tracker.MaxActiveCount);
    }

    [Fact]
    public async Task Unexpected_failure_of_one_account_does_not_stop_the_batch()
    {
        var candidates = CreateCandidates(3);
        var tracker = new SyncTracker
        {
            Handler = (candidate, _) => candidate.ExchangeAccountId == candidates[0].ExchangeAccountId
                ? throw new InvalidOperationException("test failure")
                : Task.FromResult(ExchangeAccountSyncResult.NotFound()),
        };
        await using var fixture = CreateFixture(candidates, tracker);

        var result = await fixture.Sweep.RunAsync();

        Assert.Equal(3, result.ProcessedCount);
        Assert.Equal(1, result.UnexpectedFailureCount);
        Assert.Equal(3, tracker.Calls.Count);
        Assert.Equal(2, result.OutcomeCounts[ExchangeAccountSyncOutcome.NotFound]);
    }

    [Fact]
    public async Task Candidate_source_failure_finishes_the_sweep_without_an_immediate_retry()
    {
        var tracker = new SyncTracker();
        await using var fixture = CreateFixture([], tracker, candidateLoadFails: true);

        var result = await fixture.Sweep.RunAsync();

        Assert.True(result.CandidateLoadFailed);
        Assert.Equal(0, result.CandidateCount);
        Assert.Empty(tracker.Calls);
    }

    [Fact]
    public async Task Expected_outcomes_are_recorded_without_failing_the_sweep()
    {
        var outcomes = new[]
        {
            ExchangeAccountSyncOutcome.Synchronized,
            ExchangeAccountSyncOutcome.AlreadyApplied,
            ExchangeAccountSyncOutcome.Superseded,
            ExchangeAccountSyncOutcome.ExchangeUnavailable,
            ExchangeAccountSyncOutcome.CredentialsUnavailable,
            ExchangeAccountSyncOutcome.AccountDisabled,
            ExchangeAccountSyncOutcome.NotFound,
        };
        var candidates = CreateCandidates(outcomes.Length);
        var tracker = new SyncTracker
        {
            Handler = (candidate, _) =>
            {
                var index = candidates
                    .Select((item, candidateIndex) => (item, candidateIndex))
                    .Single(item => item.item.ExchangeAccountId == candidate.ExchangeAccountId)
                    .candidateIndex;
                return Task.FromResult(CreateResult(candidates[index], outcomes[index]));
            },
        };
        await using var fixture = CreateFixture(candidates, tracker);

        var result = await fixture.Sweep.RunAsync();

        Assert.Equal(outcomes.Length, result.ProcessedCount);
        Assert.Equal(0, result.UnexpectedFailureCount);
        foreach (var outcome in outcomes)
        {
            Assert.Equal(1, result.OutcomeCounts[outcome]);
        }
    }

    [Fact]
    public async Task Cancellation_is_forwarded_and_prevents_starting_the_next_account()
    {
        var candidates = CreateCandidates(3);
        var tracker = new SyncTracker();
        var firstStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        tracker.Handler = async (_, cancellationToken) =>
        {
            firstStarted.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return ExchangeAccountSyncResult.NotFound();
        };
        await using var fixture = CreateFixture(
            candidates,
            tracker,
            new ExchangeAccountBackgroundSyncOptions
            {
                BatchSize = 3,
                MaxConcurrency = 1,
            });
        using var cancellation = new CancellationTokenSource();

        var sweepTask = fixture.Sweep.RunAsync(cancellation.Token);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sweepTask);
        Assert.Single(tracker.Calls);
        Assert.True(tracker.Calls.First().CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public void Schedule_lag_is_never_negative_and_long_sweeps_skip_missed_ticks()
    {
        var planned = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(
            TimeSpan.FromSeconds(7),
            ExchangeAccountBackgroundSyncSchedule.CalculateLag(
                planned,
                planned.AddSeconds(7)));
        Assert.Equal(
            TimeSpan.Zero,
            ExchangeAccountBackgroundSyncSchedule.CalculateLag(
                planned,
                planned.AddSeconds(-1)));
        Assert.Equal(
            planned.AddMinutes(5),
            ExchangeAccountBackgroundSyncSchedule.CalculateNextPlannedStart(
                planned,
                planned.AddMinutes(4),
                TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void Options_validation_rejects_non_positive_intervals_and_limits()
    {
        var result = new ExchangeAccountBackgroundSyncOptionsValidator().Validate(
            Options.DefaultName,
            new ExchangeAccountBackgroundSyncOptions
            {
                Interval = TimeSpan.Zero,
                InitialDelay = TimeSpan.FromSeconds(-1),
                BatchSize = 0,
                MaxConcurrency = 0,
            });

        Assert.Equal(ValidateOptionsResult.Fail(
            [
                "ExchangeAccountBackgroundSync:Interval must be greater than zero.",
                "ExchangeAccountBackgroundSync:InitialDelay must be zero or greater.",
                "ExchangeAccountBackgroundSync:BatchSize must be greater than zero.",
                "ExchangeAccountBackgroundSync:MaxConcurrency must be greater than zero.",
            ]).FailureMessage,
            result.FailureMessage);
    }

    [Fact]
    public async Task Worker_does_not_overlap_a_long_running_sweep()
    {
        var sweep = new BlockingSweep();
        var worker = new TestWorker(
            sweep,
            Options.Create(new ExchangeAccountBackgroundSyncOptions
            {
                InitialDelay = TimeSpan.Zero,
                Interval = TimeSpan.FromDays(1),
            }));
        using var cancellation = new CancellationTokenSource();

        var workerTask = worker.RunAsync(cancellation.Token);
        await sweep.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, sweep.MaxActiveCount);
        Assert.Equal(1, sweep.RunCount);

        sweep.Release.TrySetResult(true);
        cancellation.Cancel();
        await workerTask;
        Assert.Equal(1, sweep.MaxActiveCount);
    }

    [Fact]
    public async Task Disabled_worker_does_not_start_a_sweep()
    {
        var sweep = new BlockingSweep();
        var worker = new TestWorker(
            sweep,
            Options.Create(new ExchangeAccountBackgroundSyncOptions
            {
                Enabled = false,
            }));

        await worker.RunAsync(CancellationToken.None);

        Assert.Equal(0, sweep.RunCount);
    }

    private static ExchangeAccountSyncResult CreateResult(
        ExchangeAccountSyncCandidate candidate,
        ExchangeAccountSyncOutcome outcome)
    {
        var account = ExchangeAccount.Create(
            candidate.ExchangeAccountId,
            candidate.UserId,
            ExchangeId.Bybit,
            ExchangeAccountConnectionStatus.Connected,
            ExchangeAccountCapabilities.ReadBalance |
            ExchangeAccountCapabilities.ReadPositions,
            candidate.LastSyncedAt);

        return outcome switch
        {
            ExchangeAccountSyncOutcome.Synchronized => ExchangeAccountSyncResult.Synchronized(
                account,
                PortfolioState.Create(
                    candidate.ExchangeAccountId,
                    [],
                    new PortfolioCapitalState(null, null, null),
                    DateTimeOffset.UtcNow,
                    TimeSpan.FromMinutes(5))),
            ExchangeAccountSyncOutcome.AlreadyApplied => ExchangeAccountSyncResult.AlreadyApplied(account),
            ExchangeAccountSyncOutcome.Superseded => ExchangeAccountSyncResult.Superseded(account),
            ExchangeAccountSyncOutcome.ExchangeUnavailable => ExchangeAccountSyncResult.ExchangeUnavailable(),
            ExchangeAccountSyncOutcome.CredentialsUnavailable => ExchangeAccountSyncResult.CredentialsUnavailable(),
            ExchangeAccountSyncOutcome.AccountDisabled => ExchangeAccountSyncResult.AccountDisabled(),
            ExchangeAccountSyncOutcome.NotFound => ExchangeAccountSyncResult.NotFound(),
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
        };
    }

    private static ExchangeAccountSyncCandidate[] CreateCandidates(int count) =>
        Enumerable.Range(0, count)
            .Select(_ => new ExchangeAccountSyncCandidate(
                UserId.New(),
                ExchangeAccountId.New(),
                null))
            .OrderBy(candidate => candidate.ExchangeAccountId.Value)
            .ToArray();

    private static TestFixture CreateFixture(
        ExchangeAccountSyncCandidate[] candidates,
        SyncTracker tracker,
        ExchangeAccountBackgroundSyncOptions? settings = null,
        bool candidateLoadFails = false)
    {
        var services = new ServiceCollection();
        var source = new InMemoryCandidateSource(candidates, candidateLoadFails);
        services.AddSingleton(source);
        services.AddSingleton(tracker);
        services.AddScoped<IExchangeAccountSyncCandidateSource>(
            serviceProvider => serviceProvider.GetRequiredService<InMemoryCandidateSource>());
        services.AddScoped<IExchangeAccountSyncService, TrackingSyncService>();
        var provider = services.BuildServiceProvider(validateScopes: true);
        var sweep = new ExchangeAccountBackgroundSyncSweep(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(settings ?? new ExchangeAccountBackgroundSyncOptions
            {
                BatchSize = candidates.Length,
                MaxConcurrency = candidates.Length,
            }),
            TimeProvider.System,
            NullLogger<ExchangeAccountBackgroundSyncSweep>.Instance);
        return new TestFixture(provider, sweep);
    }

    private sealed class TestFixture(
        ServiceProvider provider,
        ExchangeAccountBackgroundSyncSweep sweep) : IAsyncDisposable
    {
        public ExchangeAccountBackgroundSyncSweep Sweep { get; } = sweep;

        public ValueTask DisposeAsync() => provider.DisposeAsync();
    }

    private sealed class InMemoryCandidateSource(
        IReadOnlyList<ExchangeAccountSyncCandidate> candidates,
        bool candidateLoadFails)
        : IExchangeAccountSyncCandidateSource
    {
        public Task<IReadOnlyList<ExchangeAccountSyncCandidate>> GetBatchAsync(
            ExchangeAccountId? after,
            int limit,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (candidateLoadFails)
            {
                throw new InvalidOperationException("test candidate source failure");
            }

            var query = candidates.AsEnumerable();
            if (after is { } cursor)
            {
                query = query.Where(candidate =>
                    candidate.ExchangeAccountId.Value.CompareTo(cursor.Value) > 0);
            }

            return Task.FromResult<IReadOnlyList<ExchangeAccountSyncCandidate>>(
                query.Take(limit).ToArray());
        }
    }

    private sealed class TrackingSyncService(SyncTracker tracker)
        : IExchangeAccountSyncService, IDisposable
    {
        private readonly Guid instanceId = Guid.NewGuid();

        public Task<ExchangeAccountSyncResult> SynchronizeAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            CancellationToken cancellationToken = default)
        {
            tracker.CreatedServiceIds.Add(instanceId);
            tracker.Calls.Enqueue(new SyncCall(
                userId,
                exchangeAccountId,
                instanceId,
                cancellationToken));
            return tracker.Handler?.Invoke(
                       new ExchangeAccountSyncCandidate(userId, exchangeAccountId, null),
                       cancellationToken)
                   ?? Task.FromResult(ExchangeAccountSyncResult.NotFound());
        }

        public void Dispose() => tracker.DisposedServiceIds.Add(instanceId);
    }

    private sealed class SyncTracker
    {
        public ConcurrentQueue<SyncCall> Calls { get; } = new();
        public ConcurrentBag<Guid> CreatedServiceIds { get; } = [];
        public ConcurrentBag<Guid> DisposedServiceIds { get; } = [];
        public Func<ExchangeAccountSyncCandidate, CancellationToken, Task<ExchangeAccountSyncResult>>? Handler { get; set; }
        public int ActiveCount;
        public int MaxActiveCount;
    }

    private sealed class BlockingSweep : IExchangeAccountBackgroundSyncSweep
    {
        public TaskCompletionSource<bool> Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public int ActiveCount;
        public int MaxActiveCount;
        public int RunCount;

        public async Task<ExchangeAccountBackgroundSyncSweepResult> RunAsync(
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref RunCount);
            var active = Interlocked.Increment(ref ActiveCount);
            MaxActiveCount = Math.Max(MaxActiveCount, active);
            Started.TrySetResult(true);
            try
            {
                await Release.Task.WaitAsync(cancellationToken);
                return new ExchangeAccountBackgroundSyncSweepResult(
                    0,
                    0,
                    0,
                    false,
                    new Dictionary<ExchangeAccountSyncOutcome, int>());
            }
            finally
            {
                Interlocked.Decrement(ref ActiveCount);
            }
        }
    }

    private sealed class TestWorker(
        IExchangeAccountBackgroundSyncSweep sweep,
        IOptions<ExchangeAccountBackgroundSyncOptions> options)
        : ExchangeAccountBackgroundSyncWorker(
            sweep,
            options,
            TimeProvider.System,
            NullLogger<ExchangeAccountBackgroundSyncWorker>.Instance)
    {
        public Task RunAsync(CancellationToken cancellationToken) =>
            ExecuteAsync(cancellationToken);
    }

    private sealed record SyncCall(
        UserId UserId,
        ExchangeAccountId ExchangeAccountId,
        Guid ServiceId,
        CancellationToken CancellationToken);
}
