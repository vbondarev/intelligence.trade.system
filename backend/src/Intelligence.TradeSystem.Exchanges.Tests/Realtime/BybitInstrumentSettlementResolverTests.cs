using Bybit.Net.Enums;
using Bybit.Net.Interfaces.Clients.V5;
using Bybit.Net.Objects.Models.V5;
using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Objects.Errors;
using FluentAssertions;
using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Exchanges.Bybit.Realtime;
using Microsoft.Extensions.Logging;
using Moq;

namespace Intelligence.TradeSystem.Exchanges.Tests.Realtime;

public sealed class BybitInstrumentSettlementResolverTests
{
    [Fact]
    public async Task EnsureLoadedAsync_Follows_Pagination_And_Ignores_Incomplete_Instruments()
    {
        var exchangeData = InstrumentMetadata.ExchangeData();
        exchangeData
            .Setup(data => data.GetLinearInverseSymbolsAsync(
                Category.Linear, null, null, null, null, BybitInstrumentSettlementResolver.PageLimit, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(InstrumentMetadata.Page("page-2", ("BTCUSDT", "USDT"), ("BROKEN", " ")));
        exchangeData
            .Setup(data => data.GetLinearInverseSymbolsAsync(
                Category.Linear, null, null, null, null, BybitInstrumentSettlementResolver.PageLimit, "page-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(InstrumentMetadata.Page(null, ("BTCPERP", "USDC")));
        using var resolver = InstrumentMetadata.Resolver(exchangeData);

        var loaded = await resolver.EnsureLoadedAsync();

        loaded.Should().BeTrue();
        resolver.TryResolve("BTCUSDT", out var usdt).Should().BeTrue();
        usdt.Should().Be(SettlementAsset.From("USDT"));
        resolver.TryResolve("BTCPERP", out var usdc).Should().BeTrue();
        usdc.Should().Be(SettlementAsset.From("USDC"));
        resolver.TryResolve("BROKEN", out _).Should().BeFalse();
        resolver.TryResolve("ETHUSDT", out _).Should().BeFalse();
    }

    [Fact]
    public void TryResolve_Before_Loading_Does_Not_Confirm_Settlement()
    {
        using var resolver = InstrumentMetadata.Resolver(("BTCUSDT", "USDT"));

        resolver.TryResolve("BTCUSDT", out _).Should().BeFalse();
    }

    [Fact]
    public async Task EnsureLoadedAsync_Reuses_Fresh_Metadata_And_Refreshes_After_Interval()
    {
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 10, 13, 0, 0, 0, TimeSpan.Zero));
        var exchangeData = InstrumentMetadata.ExchangeData(("BTCUSDT", "USDT"));
        using var resolver = InstrumentMetadata.Resolver(exchangeData, timeProvider);

        await resolver.EnsureLoadedAsync();
        timeProvider.Now += BybitInstrumentSettlementResolver.RefreshInterval - TimeSpan.FromSeconds(1);
        await resolver.EnsureLoadedAsync();
        VerifyFirstPageRequests(exchangeData, Times.Once());

        timeProvider.Now += TimeSpan.FromSeconds(1);
        await resolver.EnsureLoadedAsync();
        VerifyFirstPageRequests(exchangeData, Times.Exactly(2));
    }

    [Fact]
    public async Task Failed_Refresh_Keeps_Previous_Metadata_And_Logs_Normalized_Failure()
    {
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 10, 13, 0, 0, 0, TimeSpan.Zero));
        var exchangeData = InstrumentMetadata.ExchangeData(("BTCUSDT", "USDT"));
        var sink = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(sink));
        using var resolver = InstrumentMetadata.Resolver(
            exchangeData,
            timeProvider,
            loggerFactory.CreateLogger<BybitInstrumentSettlementResolver>());
        await resolver.EnsureLoadedAsync();
        exchangeData
            .Setup(data => data.GetLinearInverseSymbolsAsync(
                Category.Linear, null, null, null, null, BybitInstrumentSettlementResolver.PageLimit, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Failure("10016", ErrorType.SystemError, "provider message BTCUSDT"));
        timeProvider.Now += BybitInstrumentSettlementResolver.RefreshInterval;

        var loaded = await resolver.EnsureLoadedAsync();

        loaded.Should().BeTrue();
        resolver.TryResolve("BTCUSDT", out var asset).Should().BeTrue();
        asset.Should().Be(SettlementAsset.From("USDT"));
        var entry = sink.Entries.Should().ContainSingle().Subject;
        entry.EventId.Id.Should().Be(1012);
        entry.LogLevel.Should().Be(LogLevel.Warning);
        entry.Fields.Should().Contain(new KeyValuePair<string, object?>("FailureKind", ExchangeFailureKind.Unavailable));
        entry.Message.Should().NotContain("provider message").And.NotContain("BTCUSDT");
    }

    [Fact]
    public async Task Failed_Partial_Load_Does_Not_Publish_Incomplete_Metadata()
    {
        var exchangeData = InstrumentMetadata.ExchangeData();
        exchangeData
            .Setup(data => data.GetLinearInverseSymbolsAsync(
                Category.Linear, null, null, null, null, BybitInstrumentSettlementResolver.PageLimit, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(InstrumentMetadata.Page("page-2", ("BTCUSDT", "USDT")));
        exchangeData
            .Setup(data => data.GetLinearInverseSymbolsAsync(
                Category.Linear, null, null, null, null, BybitInstrumentSettlementResolver.PageLimit, "page-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Failure("network", ErrorType.NetworkError, "network"));
        using var resolver = InstrumentMetadata.Resolver(exchangeData);

        var loaded = await resolver.EnsureLoadedAsync();

        loaded.Should().BeFalse();
        resolver.TryResolve("BTCUSDT", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Endless_Pagination_Is_Treated_As_Invalid_Response()
    {
        var exchangeData = InstrumentMetadata.ExchangeData();
        exchangeData
            .Setup(data => data.GetLinearInverseSymbolsAsync(
                Category.Linear, null, null, null, null, BybitInstrumentSettlementResolver.PageLimit, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(InstrumentMetadata.Page("again", ("BTCUSDT", "USDT")));
        using var resolver = InstrumentMetadata.Resolver(exchangeData);

        var loaded = await resolver.EnsureLoadedAsync();

        loaded.Should().BeFalse();
        exchangeData.Verify(data => data.GetLinearInverseSymbolsAsync(
            Category.Linear, null, null, null, null, BybitInstrumentSettlementResolver.PageLimit, It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Exactly(BybitInstrumentSettlementResolver.MaxPages));
    }

    [Fact]
    public async Task EnsureLoadedAsync_Propagates_Cancellation_Without_Logging()
    {
        using var cancellation = new CancellationTokenSource();
        var exchangeData = InstrumentMetadata.ExchangeData();
        exchangeData
            .Setup(data => data.GetLinearInverseSymbolsAsync(
                Category.Linear, null, null, null, null, BybitInstrumentSettlementResolver.PageLimit, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                cancellation.Cancel();
                return Failure("cancelled", ErrorType.CancellationRequested, "cancelled");
            });
        var sink = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(sink));
        using var resolver = InstrumentMetadata.Resolver(
            exchangeData,
            logger: loggerFactory.CreateLogger<BybitInstrumentSettlementResolver>());

        var act = () => resolver.EnsureLoadedAsync(cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        sink.Entries.Should().BeEmpty();
    }

    private static void VerifyFirstPageRequests(
        Mock<IBybitRestClientApiExchangeData> exchangeData,
        Times times) =>
        exchangeData.Verify(data => data.GetLinearInverseSymbolsAsync(
            Category.Linear, null, null, null, null, BybitInstrumentSettlementResolver.PageLimit, null, It.IsAny<CancellationToken>()),
            times);

    private static HttpResult<BybitResponse<BybitLinearInverseSymbol>> Failure(
        string providerCode,
        ErrorType errorType,
        string message) =>
        new(
            "Bybit",
            default!,
            new ServerError(
                providerCode,
                new ErrorInfo(errorType, isTransient: false, message, [providerCode]),
                null!)
            {
                Message = message,
            });
}
