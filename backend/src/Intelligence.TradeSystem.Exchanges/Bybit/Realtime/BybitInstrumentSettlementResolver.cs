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
    internal const int PageLimit = 1000;
    internal const int MaxPages = 20;

    private readonly IBybitRestClient _client;
    private readonly bool _ownsClient;
    private readonly ILogger<BybitInstrumentSettlementResolver> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private volatile SettlementSnapshot? _snapshot;

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
        if (IsFresh(_snapshot))
        {
            return true;
        }

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsFresh(_snapshot))
            {
                return true;
            }

            var settlements = await LoadAsync(cancellationToken).ConfigureAwait(false);
            if (settlements is not null)
            {
                _snapshot = new SettlementSnapshot(settlements, _timeProvider.GetUtcNow());
            }

            return _snapshot is not null;
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

    private bool IsFresh(SettlementSnapshot? snapshot) =>
        snapshot is not null
        && _timeProvider.GetUtcNow() - snapshot.LoadedAt < RefreshInterval;

    private async Task<Dictionary<string, SettlementAsset>?> LoadAsync(CancellationToken cancellationToken)
    {
        var settlements = new Dictionary<string, SettlementAsset>(StringComparer.Ordinal);
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
                LogFailure(BybitExchangeFailureMapper.Map(response.Error));
                return null;
            }

            foreach (var instrument in response.Data?.List ?? [])
            {
                if (!string.IsNullOrWhiteSpace(instrument.Name)
                    && !string.IsNullOrWhiteSpace(instrument.SettleAsset))
                {
                    settlements[instrument.Name] = SettlementAsset.From(instrument.SettleAsset);
                }
            }

            cursor = response.Data?.NextPageCursor;
            if (string.IsNullOrEmpty(cursor))
            {
                return settlements;
            }
        }

        LogFailure(new ExchangeFailure(ExchangeFailureKind.InvalidResponse, Retryable: false));
        return null;
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
}
