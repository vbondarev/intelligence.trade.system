using Bybit.Net.Enums;
using Bybit.Net.Interfaces.Clients.V5;
using Bybit.Net.Objects.Models.V5;
using FluentAssertions;
using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Exchanges.Bybit.Realtime;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Language;

namespace Intelligence.TradeSystem.Exchanges.Tests.Realtime;

public sealed class BybitSettlementMetadataRefreshLoopTests
{
    private static readonly DateTimeOffset InitialTime =
        new(2026, 10, 13, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Missing_Symbol_Requests_Are_Coalesced_And_Refresh_New_Metadata()
    {
        var timeProvider = new ManualTimeProvider(InitialTime);
        var exchangeData = InstrumentMetadata.ExchangeData(("BTCUSDT", "USDT"));
        using var resolver = InstrumentMetadata.Resolver(
            exchangeData,
            timeProvider);
        await resolver.EnsureLoadedAsync();
        timeProvider.Advance(BybitInstrumentSettlementResolver.MinimumRefreshAttemptInterval);
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<CryptoExchange.Net.Objects.HttpResult<BybitResponse<BybitLinearInverseSymbol>>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        exchangeData
            .Setup(data => data.GetLinearInverseSymbolsAsync(
                Category.Linear, null, null, null, null, BybitInstrumentSettlementResolver.PageLimit, null, It.IsAny<CancellationToken>()))
            .Callback(() => signal.TrySetResult())
            .Returns(() => response.Task);

        await using var loop = new BybitSettlementMetadataRefreshLoop(
            resolver,
            timeProvider,
            NullLogger.Instance);
        loop.Start();
        for (var index = 0; index < 100; index++)
        {
            loop.RequestMissingSymbolRefresh();
        }

        await signal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        resolver.TryResolve("NEWUSDC", out _).Should().BeFalse();
        response.SetResult(InstrumentMetadata.Page(
            null,
            ("BTCUSDT", "USDT"),
            ("NEWUSDC", "USDC")));

        await SettlementMetadataRefreshTestWait.WaitForResolutionAsync(resolver, "NEWUSDC");
        resolver.TryResolve("NEWUSDC", out var settlement).Should().BeTrue();
        settlement.Should().Be(SettlementAsset.From("USDC"));
        exchangeData.Verify(data => data.GetLinearInverseSymbolsAsync(
            Category.Linear, null, null, null, null, BybitInstrumentSettlementResolver.PageLimit, null, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task Timer_Refreshes_Expired_Metadata_Without_Incoming_Events()
    {
        var timeProvider = new ManualTimeProvider(InitialTime);
        var exchangeData = InstrumentMetadata.ExchangeData(("BTCUSDT", "USDT"));
        using var resolver = InstrumentMetadata.Resolver(exchangeData, timeProvider);
        await resolver.EnsureLoadedAsync();

        var refreshStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<CryptoExchange.Net.Objects.HttpResult<BybitResponse<BybitLinearInverseSymbol>>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        exchangeData
            .Setup(data => data.GetLinearInverseSymbolsAsync(
                Category.Linear, null, null, null, null, BybitInstrumentSettlementResolver.PageLimit, null, It.IsAny<CancellationToken>()))
            .Callback(() => refreshStarted.TrySetResult())
            .Returns(() => response.Task);

        await using var loop = new BybitSettlementMetadataRefreshLoop(
            resolver,
            timeProvider,
            NullLogger.Instance);
        loop.Start();
        timeProvider.Advance(
            BybitInstrumentSettlementResolver.RefreshInterval
            + BybitSettlementMetadataRefreshLoop.MaintenanceInterval);

        await refreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        resolver.TryResolve("NEWUSDC", out _).Should().BeFalse();
        response.SetResult(InstrumentMetadata.Page(
            null,
            ("BTCUSDT", "USDT"),
            ("NEWUSDC", "USDC")));

        await SettlementMetadataRefreshTestWait.WaitForResolutionAsync(resolver, "NEWUSDC");
        resolver.TryResolve("NEWUSDC", out var settlement).Should().BeTrue();
        settlement.Should().Be(SettlementAsset.From("USDC"));
        exchangeData.Verify(data => data.GetLinearInverseSymbolsAsync(
            Category.Linear, null, null, null, null, BybitInstrumentSettlementResolver.PageLimit, null, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task Stop_Cancels_InFlight_Refresh_And_Prevents_Further_Requests()
    {
        var exchangeData = InstrumentMetadata.ExchangeData(("BTCUSDT", "USDT"));
        var timeProvider = new ManualTimeProvider(InitialTime);
        using var resolver = InstrumentMetadata.Resolver(exchangeData, timeProvider);
        await resolver.EnsureLoadedAsync();
        timeProvider.Advance(BybitInstrumentSettlementResolver.MinimumRefreshAttemptInterval);

        var refreshStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<CryptoExchange.Net.Objects.HttpResult<BybitResponse<BybitLinearInverseSymbol>>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        exchangeData
            .Setup(data => data.GetLinearInverseSymbolsAsync(
                Category.Linear, null, null, null, null, BybitInstrumentSettlementResolver.PageLimit, null, It.IsAny<CancellationToken>()))
            .Callback(new InvocationAction(invocation =>
            {
                var cancellationToken = (CancellationToken)invocation.Arguments[7]!;
                cancellationToken.Register(() => response.TrySetCanceled(cancellationToken));
                refreshStarted.TrySetResult();
            }))
            .Returns(() => response.Task);

        await using var loop = new BybitSettlementMetadataRefreshLoop(
            resolver,
            timeProvider,
            NullLogger.Instance);
        loop.Start();
        loop.RequestMissingSymbolRefresh();
        await refreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await loop.DisposeAsync();
        loop.RequestMissingSymbolRefresh();

        exchangeData.Verify(data => data.GetLinearInverseSymbolsAsync(
            Category.Linear, null, null, null, null, BybitInstrumentSettlementResolver.PageLimit, null, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        resolver.TryResolve("BTCUSDT", out _).Should().BeTrue();
    }
}
