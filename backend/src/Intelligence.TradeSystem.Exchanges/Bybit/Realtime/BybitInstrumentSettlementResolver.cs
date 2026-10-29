using Bybit.Net.Enums;
using Bybit.Net.Interfaces.Clients;
using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Exchanges.Bybit.ClientFactory;
using Intelligence.TradeSystem.Exchanges.Bybit.PrivateAccounts;
using Microsoft.Extensions.Logging;

namespace Intelligence.TradeSystem.Exchanges.Bybit.Realtime;

/// <summary>
/// Подтверждает актив расчёта Linear-инструментов Bybit по публичным метаданным инструментов.
/// </summary>
/// <remarks>
/// Приватные сообщения позиций не содержат settle coin, а суффикс символа не является надёжным
/// источником. Актив расчёта инструмента неизменен, поэтому при неудачном обновлении продолжают
/// использоваться ранее загруженные метаданные.
/// </remarks>
public sealed partial class BybitInstrumentSettlementResolver : IDisposable
{
    internal static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(1);
    internal static readonly TimeSpan MinimumRefreshAttemptInterval = TimeSpan.FromMinutes(5);
    internal static readonly TimeSpan RateLimitCooldown = TimeSpan.FromMinutes(10);
    internal const int PageLimit = 1000;
    internal const int MaxPages = 20;

    private readonly IBybitRestClient _client;
    private readonly bool _ownsClient;
    private readonly ILogger<BybitInstrumentSettlementResolver> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private volatile SettlementSnapshot? _snapshot;
    private long? _lastRefreshAttemptTimestamp;
    private long? _rateLimitedAtTimestamp;

    public BybitInstrumentSettlementResolver(
        ILogger<BybitInstrumentSettlementResolver> logger,
        TimeProvider timeProvider)
        : this(BybitClientFactory.CreatePublicClient(), logger, timeProvider, ownsClient: true)
    {
    }

    internal BybitInstrumentSettlementResolver(
        IBybitRestClient client,
        ILogger<BybitInstrumentSettlementResolver> logger,
        TimeProvider timeProvider,
        bool ownsClient = false)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _client = client;
        _ownsClient = ownsClient;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Возвращает актив расчёта Linear-инструмента, если он подтверждён загруженными метаданными.
    /// </summary>
    public bool TryResolve(string symbol, out SettlementAsset settlementAsset)
    {
        if (_snapshot is { } snapshot
            && snapshot.Settlements.TryGetValue(symbol, out var asset))
        {
            settlementAsset = asset;
            return true;
        }

        settlementAsset = default;
        return false;
    }

    /// <summary>
    /// Загружает метаданные, если они отсутствуют или устарели.
    /// </summary>
    /// <returns><see langword="true"/>, если доступны хотя бы ранее загруженные метаданные.</returns>
    public async Task<bool> EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        await RefreshIfDueAsync(missingSymbol: false, cancellationToken).ConfigureAwait(false);
        return _snapshot is not null;
    }

    /// <summary>
    /// Обновляет подтверждённый снимок по TTL или по сигналу об отсутствующем инструменте.
    /// </summary>
    internal async Task<SettlementMetadataRefreshResult> RefreshIfDueAsync(
        bool missingSymbol,
        CancellationToken cancellationToken)
    {
        if (!IsRefreshDue(missingSymbol))
        {
            return SettlementMetadataRefreshResult.Skipped;
        }

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsRefreshDue(missingSymbol) || IsCoolingDown())
            {
                return SettlementMetadataRefreshResult.Skipped;
            }

            _lastRefreshAttemptTimestamp = _timeProvider.GetTimestamp();
            var result = await LoadAsync(cancellationToken).ConfigureAwait(false);
            if (result.Settlements is null)
            {
                if (result.FailureKind == ExchangeFailureKind.RateLimited)
                {
                    _rateLimitedAtTimestamp = _timeProvider.GetTimestamp();
                }

                return SettlementMetadataRefreshResult.Failed;
            }

            _snapshot = new SettlementSnapshot(result.Settlements, _timeProvider.GetUtcNow());
            _rateLimitedAtTimestamp = null;
            return SettlementMetadataRefreshResult.Succeeded;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public void Dispose()
    {
        _refreshLock.Dispose();
        if (_ownsClient)
        {
            _client.Dispose();
        }
    }

    private bool IsRefreshDue(bool missingSymbol) =>
        missingSymbol || !IsFresh(_snapshot);

    private bool IsFresh(SettlementSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return false;
        }

        var age = _timeProvider.GetUtcNow() - snapshot.LoadedAt;
        return age >= TimeSpan.Zero && age < RefreshInterval;
    }

    private bool IsCoolingDown()
    {
        if (_lastRefreshAttemptTimestamp is { } lastAttemptTimestamp)
        {
            var elapsed = _timeProvider.GetElapsedTime(lastAttemptTimestamp);
            if (elapsed >= TimeSpan.Zero && elapsed < MinimumRefreshAttemptInterval)
            {
                return true;
            }
        }

        if (_rateLimitedAtTimestamp is not { } rateLimitedAtTimestamp)
        {
            return false;
        }

        var rateLimitElapsed = _timeProvider.GetElapsedTime(rateLimitedAtTimestamp);
        return rateLimitElapsed >= TimeSpan.Zero && rateLimitElapsed < RateLimitCooldown;
    }

    private async Task<SettlementLoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        var settlements = new Dictionary<string, SettlementAsset>(StringComparer.Ordinal);
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;

        for (var page = 0; page < MaxPages; page++)
        {
            var response = await _client.V5Api.ExchangeData.GetLinearInverseSymbolsAsync(
                Category.Linear,
                limit: PageLimit,
                cursor: cursor,
                ct: cancellationToken).ConfigureAwait(false);

            if (!response.Success)
            {
                BybitExchangeFailureMapper.ThrowIfCancellationRequested(response.Error, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                var failure = BybitExchangeFailureMapper.Map(response.Error);
                LogFailure(failure);
                return SettlementLoadResult.Failed(failure.Kind);
            }

            var data = response.Data;
            if (data is null)
            {
                LogFailure(new ExchangeFailure(ExchangeFailureKind.InvalidResponse, Retryable: false));
                return SettlementLoadResult.Failed(ExchangeFailureKind.InvalidResponse);
            }

            var instruments = data.List;
            if (instruments is not { Length: > 0 })
            {
                LogFailure(new ExchangeFailure(ExchangeFailureKind.InvalidResponse, Retryable: false));
                return SettlementLoadResult.Failed(ExchangeFailureKind.InvalidResponse);
            }

            foreach (var instrument in instruments)
            {
                if (string.IsNullOrWhiteSpace(instrument.Name)
                    || string.IsNullOrWhiteSpace(instrument.SettleAsset))
                {
                    continue;
                }

                SettlementAsset settlementAsset;
                try
                {
                    settlementAsset = SettlementAsset.From(instrument.SettleAsset);
                }
                catch (ArgumentException)
                {
                    LogFailure(new ExchangeFailure(ExchangeFailureKind.InvalidResponse, Retryable: false));
                    return SettlementLoadResult.Failed(ExchangeFailureKind.InvalidResponse);
                }

                if (settlements.TryGetValue(instrument.Name, out var existing)
                    && existing != settlementAsset)
                {
                    LogFailure(new ExchangeFailure(ExchangeFailureKind.InvalidResponse, Retryable: false));
                    return SettlementLoadResult.Failed(ExchangeFailureKind.InvalidResponse);
                }

                settlements[instrument.Name] = settlementAsset;
            }

            cursor = data.NextPageCursor;
            if (string.IsNullOrEmpty(cursor))
            {
                if (settlements.Count == 0 || ConflictsWithPublishedSnapshot(settlements))
                {
                    LogFailure(new ExchangeFailure(ExchangeFailureKind.InvalidResponse, Retryable: false));
                    return SettlementLoadResult.Failed(ExchangeFailureKind.InvalidResponse);
                }

                return SettlementLoadResult.Succeeded(settlements);
            }

            if (!seenCursors.Add(cursor))
            {
                LogFailure(new ExchangeFailure(ExchangeFailureKind.InvalidResponse, Retryable: false));
                return SettlementLoadResult.Failed(ExchangeFailureKind.InvalidResponse);
            }
        }

        LogFailure(new ExchangeFailure(ExchangeFailureKind.InvalidResponse, Retryable: false));
        return SettlementLoadResult.Failed(ExchangeFailureKind.InvalidResponse);
    }

    private bool ConflictsWithPublishedSnapshot(
        IReadOnlyDictionary<string, SettlementAsset> settlements)
    {
        if (_snapshot is not { } snapshot)
        {
            return false;
        }

        return settlements.Any(pair =>
            snapshot.Settlements.TryGetValue(pair.Key, out var existing)
            && existing != pair.Value);
    }

    private void LogFailure(ExchangeFailure failure) =>
        LogMetadataUnavailable(
            _logger,
            failure.Kind,
            failure.Retryable,
            BybitExchangeFailureMapper.SafeProviderCode(failure.ProviderCode));

    [LoggerMessage(
        EventId = 1012,
        Level = LogLevel.Warning,
        Message = "Метаданные инструментов Bybit недоступны; актив расчёта потоковых наблюдений не подтверждается. FailureKind={FailureKind}, Retryable={Retryable}, ProviderCode={ProviderCode}")]
    private static partial void LogMetadataUnavailable(
        ILogger logger,
        ExchangeFailureKind failureKind,
        bool retryable,
        string? providerCode);

    private sealed record SettlementSnapshot(
        IReadOnlyDictionary<string, SettlementAsset> Settlements,
        DateTimeOffset LoadedAt);

    private sealed record SettlementLoadResult(
        Dictionary<string, SettlementAsset>? Settlements,
        ExchangeFailureKind? FailureKind)
    {
        public static SettlementLoadResult Succeeded(Dictionary<string, SettlementAsset> settlements) =>
            new(settlements, null);

        public static SettlementLoadResult Failed(ExchangeFailureKind failureKind) =>
            new(null, failureKind);
    }
}
