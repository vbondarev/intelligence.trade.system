using Intelligence.TradeSystem.Application.Realtime.State;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Realtime;

public sealed class RealtimeObservationReadService
{
    private readonly IRealtimeObservationStateStore _store;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _heartbeatStaleAfter;

    public RealtimeObservationReadService(
        IRealtimeObservationStateStore store,
        RealtimeObservationReadOptions options,
        TimeProvider timeProvider)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        ArgumentNullException.ThrowIfNull(options);
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _heartbeatStaleAfter = options.HeartbeatStaleAfter;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_heartbeatStaleAfter, TimeSpan.Zero);
    }

    public async Task<RealtimeObservationReadModel> GetAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        CancellationToken cancellationToken = default)
    {
        var versioned = await _store
            .GetAsync(userId, exchangeAccountId, cancellationToken)
            .ConfigureAwait(false);

        return RealtimeFreshnessProjection.ProjectObservation(
            versioned?.Value,
            _timeProvider.GetUtcNow(),
            _heartbeatStaleAfter);
    }

    public async Task<IReadOnlyDictionary<ExchangeAccountId, RealtimeObservationReadModel>> GetForAccountsAsync(
        UserId userId,
        IReadOnlyCollection<ExchangeAccountId> exchangeAccountIds,
        CancellationToken cancellationToken = default)
    {
        if (userId == default)
        {
            throw new ArgumentException("UserId must be initialized.", nameof(userId));
        }

        ArgumentNullException.ThrowIfNull(exchangeAccountIds);
        cancellationToken.ThrowIfCancellationRequested();

        var accountIds = exchangeAccountIds.Distinct().ToArray();
        if (accountIds.Length == 0)
        {
            return new Dictionary<ExchangeAccountId, RealtimeObservationReadModel>();
        }

        var states = await _store
            .GetForAccountsAsync(userId, accountIds, cancellationToken)
            .ConfigureAwait(false);
        var now = _timeProvider.GetUtcNow();

        return accountIds.ToDictionary(
            accountId => accountId,
            accountId => RealtimeFreshnessProjection.ProjectObservation(
                states.TryGetValue(accountId, out var versioned) ? versioned.Value : null,
                now,
                _heartbeatStaleAfter));
    }
}
