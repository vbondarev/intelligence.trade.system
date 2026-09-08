using Bybit.Net.Enums;
using Bybit.Net.Interfaces.Clients;
using Bybit.Net.Interfaces.Clients.V5;
using Bybit.Net.Objects.Models.V5;
using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Objects.Errors;
using FluentAssertions;
using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Exchanges.Bybit.PrivateAccounts;
using Intelligence.TradeSystem.Exchanges.Bybit.Telemetry;
using Microsoft.Extensions.Logging;
using Moq;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using BybitAccountType = Bybit.Net.Enums.AccountType;
using DomainAccountType = Intelligence.TradeSystem.Domain.AccountType;

namespace Intelligence.TradeSystem.Exchanges.Tests;

[Collection("BybitExchangeTelemetry")]
public sealed class BybitPrivateAccountProviderTests
{
    [Fact]
    public async Task GetOpenPositionsAsync_Returns_Complete_With_Empty_Positions_On_Successful_Empty_Response()
    {
        var trading = new Mock<IBybitRestClientApiTrading>();
        trading
            .Setup(t => t.GetPositionsAsync(
                Category.Linear, "BTCUSDT", null, null, 200, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccess(new BybitResponse<BybitPosition> { List = [] }));

        var provider = CreateProvider(trading);
        var observation = await provider.GetOpenPositionsAsync(MarketCategory.Linear, "BTCUSDT");

        observation.Status.Should().Be(OpenPositionsObservationStatus.Complete);
        observation.Positions.Should().BeEmpty();
        observation.Category.Should().Be(MarketCategory.Linear);
        observation.Symbol.Should().Be("BTCUSDT");
        observation.Error.Should().BeNull();
    }

    [Fact]
    public async Task GetOpenPositionsAsync_Returns_Complete_With_Positions_On_Successful_Response()
    {
        var trading = new Mock<IBybitRestClientApiTrading>();
        trading
            .Setup(t => t.GetPositionsAsync(
                Category.Linear, null, null, null, 200, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccess(new BybitResponse<BybitPosition>
            {
                List =
                [
                    new BybitPosition { Symbol = "BTCUSDT", Quantity = 1.5m, Side = PositionSide.Buy },
                ],
            }));

        var provider = CreateProvider(trading);

        var observation = await provider.GetOpenPositionsAsync(MarketCategory.Linear);

        observation.Status.Should().Be(OpenPositionsObservationStatus.Complete);
        observation.Positions.Should().ContainSingle(p => p.Symbol == "BTCUSDT" && p.Size == 1.5m);
    }

    [Fact]
    public async Task GetOpenPositionsAsync_Returns_Failed_On_Api_Error()
    {
        var trading = new Mock<IBybitRestClientApiTrading>();
        trading
            .Setup(t => t.GetPositionsAsync(
                Category.Linear, null, null, null, 200, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateError<BybitResponse<BybitPosition>>("boom"));

        var provider = CreateProvider(trading);

        var observation = await provider.GetOpenPositionsAsync(MarketCategory.Linear);

        observation.Status.Should().Be(OpenPositionsObservationStatus.Failed);
        observation.Positions.Should().BeEmpty();
        observation.Error.Should().Be("boom");
    }

    [Fact]
    public async Task GetOpenPositionsAsync_Followed_By_Zero_Positions_Never_Looks_The_Same_As_Failed()
    {
        var emptyTrading = new Mock<IBybitRestClientApiTrading>();
        emptyTrading
            .Setup(t => t.GetPositionsAsync(
                Category.Linear, null, null, null, 200, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccess(new BybitResponse<BybitPosition> { List = [] }));
        var emptyObservation = await CreateProvider(emptyTrading).GetOpenPositionsAsync(MarketCategory.Linear);

        var failedTrading = new Mock<IBybitRestClientApiTrading>();
        failedTrading
            .Setup(t => t.GetPositionsAsync(
                Category.Linear, null, null, null, 200, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateError<BybitResponse<BybitPosition>>("boom"));
        var failedObservation = await CreateProvider(failedTrading).GetOpenPositionsAsync(MarketCategory.Linear);

        emptyObservation.Positions.Should().BeEmpty();
        failedObservation.Positions.Should().BeEmpty();
        emptyObservation.Status.Should().Be(OpenPositionsObservationStatus.Complete);
        failedObservation.Status.Should().Be(OpenPositionsObservationStatus.Failed);
        emptyObservation.Status.Should().NotBe(failedObservation.Status);
    }

    [Fact]
    public async Task GetOpenPositionsAsync_Follows_Pagination_Cursor_Before_Reporting_Complete()
    {
        var trading = new Mock<IBybitRestClientApiTrading>();
        trading
            .Setup(t => t.GetPositionsAsync(
                Category.Linear, null, null, null, 200, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccess(new BybitResponse<BybitPosition>
            {
                List = [new BybitPosition { Symbol = "BTCUSDT", Quantity = 1m, Side = PositionSide.Buy }],
                NextPageCursor = "next-page",
            }));
        trading
            .Setup(t => t.GetPositionsAsync(
                Category.Linear, null, null, null, 200, "next-page", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccess(new BybitResponse<BybitPosition>
            {
                List = [new BybitPosition { Symbol = "ETHUSDT", Quantity = 2m, Side = PositionSide.Sell }],
            }));

        var provider = CreateProvider(trading);

        var observation = await provider.GetOpenPositionsAsync(MarketCategory.Linear);

        observation.Status.Should().Be(OpenPositionsObservationStatus.Complete);
        observation.Positions.Should().HaveCount(2);
        observation.Positions.Should().Contain(p => p.Symbol == "BTCUSDT");
        observation.Positions.Should().Contain(p => p.Symbol == "ETHUSDT");
    }

    [Fact]
    public async Task GetOpenPositionsAsync_Returns_Partial_When_A_Later_Page_Fails()
    {
        var trading = new Mock<IBybitRestClientApiTrading>();
        trading
            .Setup(t => t.GetPositionsAsync(
                Category.Linear, null, null, null, 200, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccess(new BybitResponse<BybitPosition>
            {
                List = [new BybitPosition { Symbol = "BTCUSDT", Quantity = 1m, Side = PositionSide.Buy }],
                NextPageCursor = "next-page",
            }));
        trading
            .Setup(t => t.GetPositionsAsync(
                Category.Linear, null, null, null, 200, "next-page", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateError<BybitResponse<BybitPosition>>("page 2 failed"));

        var provider = CreateProvider(trading);

        var observation = await provider.GetOpenPositionsAsync(MarketCategory.Linear);

        observation.Status.Should().Be(OpenPositionsObservationStatus.Partial);
        observation.Positions.Should().ContainSingle(p => p.Symbol == "BTCUSDT");
        observation.Error.Should().Be("page 2 failed");
    }

    [Fact]
    public async Task GetOpenPositionsAsync_Throws_For_Spot_Category()
    {
        var provider = CreateProvider(new Mock<IBybitRestClientApiTrading>());

        var act = async () => await provider.GetOpenPositionsAsync(MarketCategory.Spot);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetOpenPositionsAsync_Propagates_Cancellation_From_Bybit_Result()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var trading = new Mock<IBybitRestClientApiTrading>();
        trading
            .Setup(t => t.GetPositionsAsync(
                Category.Linear, null, null, null, 200, null, cancellation.Token))
            .ReturnsAsync(CreateProviderError<BybitResponse<BybitPosition>>(
                "cancelled",
                ErrorType.CancellationRequested));

        var act = () => CreateProvider(trading)
            .GetOpenPositionsAsync(MarketCategory.Linear, cancellationToken: cancellation.Token);

        var exception = await act.Should().ThrowAsync<OperationCanceledException>();
        exception.Which.CancellationToken.Should().Be(cancellation.Token);
    }

    [Fact]
    public void Default_ExchangeFailureKind_Is_Unknown()
    {
        default(ExchangeFailureKind).Should().Be(ExchangeFailureKind.Unknown);
    }

    [Fact]
    public async Task GetWalletBalanceAsync_Returns_Complete_With_Mapped_Balance_On_Success()
    {
        var account = new Mock<IBybitRestClientApiAccount>();
        account
            .Setup(a => a.GetBalancesAsync(
                BybitAccountType.Unified,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccess(new BybitResponse<BybitBalance>
            {
                List =
                [
                    new BybitBalance
                    {
                        AccountType = BybitAccountType.Unified,
                        TotalEquity = 12_500m,
                        TotalWalletBalance = 12_000m,
                        TotalAvailableBalance = 8_000m,
                        TotalPerpUnrealizedPnl = 500m,
                        Assets =
                        [
                            new BybitAssetBalance
                            {
                                Asset = "USDT",
                                Equity = 12_500m,
                                UsdValue = 12_500m,
                                WalletBalance = 12_000m,
                                Free = 8_000m,
                            },
                        ],
                    },
                ],
            }));

        var observation = await CreateProvider(new Mock<IBybitRestClientApiTrading>(), account)
            .GetWalletBalanceAsync(DomainAccountType.Unified);

        observation.Status.Should().Be(AccountBalanceObservationStatus.Complete);
        observation.Failure.Should().BeNull();
        observation.Balance.Should().NotBeNull();
        observation.Balance!.AccountType.Should().Be(DomainAccountType.Unified);
        observation.Balance.TotalEquity.Should().Be(12_500m);
        observation.Balance.TotalAvailableBalance.Should().Be(8_000m);
        observation.Balance.Coins.Should().ContainSingle(coin =>
            coin.Coin == "USDT" && coin.WalletBalance == 12_000m && coin.AvailableBalance == 8_000m);
    }

    [Fact]
    public async Task GetWalletBalanceAsync_Returns_InvalidResponse_When_Success_Has_No_Balance()
    {
        var account = new Mock<IBybitRestClientApiAccount>();
        account
            .Setup(a => a.GetBalancesAsync(
                BybitAccountType.Unified,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccess(new BybitResponse<BybitBalance> { List = [] }));

        var observation = await CreateProvider(new Mock<IBybitRestClientApiTrading>(), account)
            .GetWalletBalanceAsync(DomainAccountType.Unified);

        observation.Status.Should().Be(AccountBalanceObservationStatus.Failed);
        observation.Balance.Should().BeNull();
        observation.Failure.Should().BeEquivalentTo(
            new ExchangeFailure(ExchangeFailureKind.InvalidResponse, Retryable: false));
    }

    [Theory]
    [InlineData("10003", ErrorType.Unauthorized, ExchangeFailureKind.InvalidCredentials, false)]
    [InlineData("10005", ErrorType.Unauthorized, ExchangeFailureKind.PermissionDenied, false)]
    [InlineData("10006", ErrorType.RateLimitRequest, ExchangeFailureKind.RateLimited, true)]
    [InlineData("missing-credentials", ErrorType.MissingCredentials, ExchangeFailureKind.InvalidCredentials, false)]
    [InlineData("timeout", ErrorType.Timeout, ExchangeFailureKind.Timeout, true)]
    [InlineData("network", ErrorType.NetworkError, ExchangeFailureKind.Unavailable, true)]
    [InlineData("deserialization", ErrorType.DeserializationFailed, ExchangeFailureKind.InvalidResponse, false)]
    [InlineData("10016", ErrorType.SystemError, ExchangeFailureKind.Unavailable, true)]
    [InlineData("unknown-code", ErrorType.Unknown, ExchangeFailureKind.Unknown, false)]
    public async Task GetWalletBalanceAsync_Maps_Provider_Failure(
        string providerCode,
        ErrorType errorType,
        ExchangeFailureKind expectedKind,
        bool expectedRetryable)
    {
        var account = new Mock<IBybitRestClientApiAccount>();
        account
            .Setup(a => a.GetBalancesAsync(
                BybitAccountType.Unified,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateProviderError<BybitResponse<BybitBalance>>(providerCode, errorType));

        var observation = await CreateProvider(new Mock<IBybitRestClientApiTrading>(), account)
            .GetWalletBalanceAsync(DomainAccountType.Unified);

        observation.Status.Should().Be(AccountBalanceObservationStatus.Failed);
        observation.Balance.Should().BeNull();
        observation.Failure.Should().NotBeNull();
        observation.Failure!.Kind.Should().Be(expectedKind);
        observation.Failure.Retryable.Should().Be(expectedRetryable);
        observation.Failure.ProviderCode.Should().Be(providerCode);
    }

    [Fact]
    public async Task GetWalletBalanceAsync_Propagates_Cancellation_And_Passes_Token_To_Bybit()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var account = new Mock<IBybitRestClientApiAccount>();
        account
            .Setup(a => a.GetBalancesAsync(
                BybitAccountType.Unified,
                null,
                It.Is<CancellationToken>(token => token == cancellation.Token)))
            .Returns(() => Task.FromCanceled<HttpResult<BybitResponse<BybitBalance>>>(cancellation.Token));

        var act = () => CreateProvider(new Mock<IBybitRestClientApiTrading>(), account)
            .GetWalletBalanceAsync(DomainAccountType.Unified, cancellation.Token);

        var exception = await act.Should().ThrowAsync<OperationCanceledException>();
        exception.Which.CancellationToken.Should().Be(cancellation.Token);
        account.Verify(a => a.GetBalancesAsync(
            BybitAccountType.Unified,
            null,
            cancellation.Token), Times.Once);
    }

    [Fact]
    public async Task GetWalletBalanceAsync_Propagates_Cancellation_From_Bybit_Result()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var account = new Mock<IBybitRestClientApiAccount>();
        account
            .Setup(a => a.GetBalancesAsync(
                BybitAccountType.Unified,
                null,
                cancellation.Token))
            .ReturnsAsync(CreateProviderError<BybitResponse<BybitBalance>>(
                "cancelled",
                ErrorType.CancellationRequested));

        var act = () => CreateProvider(new Mock<IBybitRestClientApiTrading>(), account)
            .GetWalletBalanceAsync(DomainAccountType.Unified, cancellation.Token);

        var exception = await act.Should().ThrowAsync<OperationCanceledException>();
        exception.Which.CancellationToken.Should().Be(cancellation.Token);
    }

    [Fact]
    public async Task GetWalletBalanceAsync_Propagates_Cancellation_Without_Attaching_NonCancelled_Token()
    {
        using var cancellation = new CancellationTokenSource();

        var account = new Mock<IBybitRestClientApiAccount>();
        account
            .Setup(a => a.GetBalancesAsync(
                BybitAccountType.Unified,
                null,
                cancellation.Token))
            .ReturnsAsync(CreateProviderError<BybitResponse<BybitBalance>>(
                "cancelled",
                ErrorType.CancellationRequested));

        var act = () => CreateProvider(new Mock<IBybitRestClientApiTrading>(), account)
            .GetWalletBalanceAsync(DomainAccountType.Unified, cancellation.Token);

        var exception = await act.Should().ThrowAsync<OperationCanceledException>();
        exception.Which.CancellationToken.Should().Be(default(CancellationToken));
        exception.Which.CancellationToken.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task GetWalletBalanceAsync_Retries_Timeout_Once_And_Records_Two_Attempts()
    {
        var account = new Mock<IBybitRestClientApiAccount>();
        var attempts = 0;
        account
            .Setup(a => a.GetBalancesAsync(
                BybitAccountType.Unified,
                null,
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                attempts++;
                return Task.FromResult(attempts == 1
                    ? CreateProviderError<BybitResponse<BybitBalance>>("timeout", ErrorType.Timeout)
                    : CreateSuccess(new BybitResponse<BybitBalance>
                    {
                        List =
                        [
                            new BybitBalance
                            {
                                AccountType = BybitAccountType.Unified,
                                TotalEquity = 100m,
                            },
                        ],
                    }));
            });

        var observation = await CreateProvider(
                new Mock<IBybitRestClientApiTrading>(),
                account,
                retryDelay: static (_, _) => Task.CompletedTask)
            .GetWalletBalanceAsync(DomainAccountType.Unified);

        observation.Status.Should().Be(AccountBalanceObservationStatus.Complete);
        attempts.Should().Be(2);
    }

    [Theory]
    [InlineData("10003", ErrorType.Unauthorized, ExchangeFailureKind.InvalidCredentials)]
    [InlineData("10005", ErrorType.Unauthorized, ExchangeFailureKind.PermissionDenied)]
    public async Task GetWalletBalanceAsync_Does_Not_Retry_NonRetryable_Credentials_Failures(
        string providerCode,
        ErrorType errorType,
        ExchangeFailureKind expectedKind)
    {
        var account = new Mock<IBybitRestClientApiAccount>();
        var attempts = 0;
        account
            .Setup(a => a.GetBalancesAsync(
                BybitAccountType.Unified,
                null,
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                attempts++;
                return Task.FromResult(CreateProviderError<BybitResponse<BybitBalance>>(providerCode, errorType));
            });

        var observation = await CreateProvider(
                new Mock<IBybitRestClientApiTrading>(),
                account,
                retryDelay: static (_, _) => Task.CompletedTask)
            .GetWalletBalanceAsync(DomainAccountType.Unified);

        observation.Failure!.Kind.Should().Be(expectedKind);
        attempts.Should().Be(1);
    }

    [Fact]
    public async Task GetWalletBalanceAsync_Does_Not_Retry_Rate_Limit_Without_Usable_RetryAfter()
    {
        var account = new Mock<IBybitRestClientApiAccount>();
        var attempts = 0;
        account
            .Setup(a => a.GetBalancesAsync(
                BybitAccountType.Unified,
                null,
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                attempts++;
                return Task.FromResult(
                    CreateProviderError<BybitResponse<BybitBalance>>("10006", ErrorType.RateLimitRequest));
            });

        var observation = await CreateProvider(
                new Mock<IBybitRestClientApiTrading>(),
                account,
                retryDelay: static (_, _) => Task.CompletedTask)
            .GetWalletBalanceAsync(DomainAccountType.Unified);

        observation.Failure!.Kind.Should().Be(ExchangeFailureKind.RateLimited);
        observation.Failure.Retryable.Should().BeTrue();
        attempts.Should().Be(1);
    }

    [Fact]
    public async Task GetWalletBalanceAsync_Retries_RateLimit_With_Valid_RetryAfter_And_Preserves_Reason()
    {
        var activities = new List<Activity>();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == BybitExchangeTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => activities.Add(activity),
        };
        ActivitySource.AddActivityListener(activityListener);

        var measurements = new List<MetricMeasurement>();
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == BybitExchangeTelemetry.MeterName)
                listener.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            measurements.Add(new(instrument.Name, value, tags.ToArray())));
        meterListener.Start();

        var account = new Mock<IBybitRestClientApiAccount>();
        var attempts = 0;
        account
            .Setup(a => a.GetBalancesAsync(
                BybitAccountType.Unified,
                null,
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                attempts++;
                return Task.FromResult(
                    attempts == 1
                        ? CreateRateLimitError<BybitResponse<BybitBalance>>(DateTime.UtcNow.AddSeconds(1))
                        : CreateSuccess(new BybitResponse<BybitBalance>
                        {
                            List = [new BybitBalance { AccountType = BybitAccountType.Unified }],
                        }));
            });

        var observation = await CreateProvider(
                new Mock<IBybitRestClientApiTrading>(),
                account,
                retryDelay: static (_, _) => Task.CompletedTask)
            .GetWalletBalanceAsync(DomainAccountType.Unified);

        observation.Status.Should().Be(AccountBalanceObservationStatus.Complete);
        attempts.Should().Be(2);
        var activity = activities.Should().ContainSingle().Subject;
        activity.GetTagItem("exchange.outcome").Should().Be(BybitExchangeTelemetry.SuccessOutcome);
        activity.GetTagItem("exchange.failure_kind").Should().Be(string.Empty);
        activity.GetTagItem("retry.count").Should().Be(1);
        activity.GetTagItem("retry.failure_kind").Should().Be(nameof(ExchangeFailureKind.RateLimited));
        measurements.Any(measurement => measurement.Name == "exchange.request.failures").Should().BeFalse();
    }

    [Fact]
    public async Task GetWalletBalanceAsync_Does_Not_Retry_RateLimit_With_Excessive_RetryAfter()
    {
        var account = new Mock<IBybitRestClientApiAccount>();
        var attempts = 0;
        account
            .Setup(a => a.GetBalancesAsync(
                BybitAccountType.Unified,
                null,
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                attempts++;
                return Task.FromResult(
                    CreateRateLimitError<BybitResponse<BybitBalance>>(DateTime.UtcNow.AddSeconds(5)));
            });

        var observation = await CreateProvider(
                new Mock<IBybitRestClientApiTrading>(),
                account,
                retryDelay: static (_, _) => Task.CompletedTask)
            .GetWalletBalanceAsync(DomainAccountType.Unified);

        observation.Status.Should().Be(AccountBalanceObservationStatus.Failed);
        observation.Failure!.Kind.Should().Be(ExchangeFailureKind.RateLimited);
        attempts.Should().Be(1);
    }

    [Fact]
    public async Task GetOpenPositionsAsync_Retries_The_Failed_Page_Without_Restarting_Pagination()
    {
        var activities = new List<Activity>();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == BybitExchangeTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => activities.Add(activity),
        };
        ActivitySource.AddActivityListener(activityListener);

        var trading = new Mock<IBybitRestClientApiTrading>();
        var secondPageAttempts = 0;
        trading
            .Setup(t => t.GetPositionsAsync(
                Category.Linear, null, null, null, 200, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccess(new BybitResponse<BybitPosition>
            {
                List = [new BybitPosition { Symbol = "BTCUSDT", Quantity = 1m, Side = PositionSide.Buy }],
                NextPageCursor = "next-page",
            }));
        trading
            .Setup(t => t.GetPositionsAsync(
                Category.Linear, null, null, null, 200, "next-page", It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                secondPageAttempts++;
                return Task.FromResult(secondPageAttempts == 1
                    ? CreateProviderError<BybitResponse<BybitPosition>>("network", ErrorType.NetworkError)
                    : CreateSuccess(new BybitResponse<BybitPosition>
                    {
                        List = [new BybitPosition { Symbol = "ETHUSDT", Quantity = 2m, Side = PositionSide.Sell }],
                    }));
            });

        var observation = await CreateProvider(
                trading,
                retryDelay: static (_, _) => Task.CompletedTask)
            .GetOpenPositionsAsync(MarketCategory.Linear);

        observation.Status.Should().Be(OpenPositionsObservationStatus.Complete);
        observation.Positions.Should().HaveCount(2);
        secondPageAttempts.Should().Be(2);
        var activity = activities.Should().ContainSingle().Subject;
        activity.GetTagItem("retry.count").Should().Be(1);
        activity.GetTagItem("retry.failure_kind").Should().Be(nameof(ExchangeFailureKind.Unavailable));
        activity.GetTagItem("exchange.outcome").Should().Be(BybitExchangeTelemetry.SuccessOutcome);
    }

    [Fact]
    public async Task Private_Failure_Log_Uses_Normalized_Fields_And_Excludes_Provider_Message_And_Secrets()
    {
        const string secret = "test-api-secret-secret-value";
        var sink = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(sink));
        var account = new Mock<IBybitRestClientApiAccount>();
        account
            .Setup(a => a.GetBalancesAsync(
                BybitAccountType.Unified,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HttpResult<BybitResponse<BybitBalance>>(
                "Bybit",
                default!,
                new ServerError(
                    "10003",
                    new ErrorInfo(ErrorType.Unauthorized, false, secret, ["10003"]),
                    null!)
                {
                    Message = secret,
                }));

        await CreateProvider(
                new Mock<IBybitRestClientApiTrading>(),
                account,
                loggerFactory,
                static (_, _) => Task.CompletedTask)
            .GetWalletBalanceAsync(DomainAccountType.Unified);

        var entry = sink.Entries.Should().ContainSingle().Subject;
        entry.EventId.Id.Should().Be(1010);
        entry.LogLevel.Should().Be(LogLevel.Warning);
        entry.Fields.Any(field => field.Key == "Operation" && field.Value?.ToString() == BybitExchangeTelemetry.BalanceOperation).Should().BeTrue();
        entry.Fields.Any(field => field.Key == "FailureKind" && field.Value?.ToString() == nameof(ExchangeFailureKind.InvalidCredentials)).Should().BeTrue();
        entry.Fields.Any(field => field.Key == "Retryable" && Equals(field.Value, false)).Should().BeTrue();
        entry.Fields.Any(field => field.Key == "ProviderCode" && field.Value?.ToString() == "10003").Should().BeTrue();
        entry.Fields.Any(field => field.Key is "Error" or "Message").Should().BeFalse();
        entry.Fields.Select(field => field.Value?.ToString()).Any(value => value?.Contains(secret, StringComparison.Ordinal) == true).Should().BeFalse();
    }

    [Fact]
    public async Task Open_Positions_Failure_Log_Does_Not_Use_Raw_Provider_Message()
    {
        const string secret = "test-api-key-secret-value";
        var sink = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(sink));
        var trading = new Mock<IBybitRestClientApiTrading>();
        trading
            .Setup(t => t.GetPositionsAsync(
                Category.Linear, null, null, null, 200, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HttpResult<BybitResponse<BybitPosition>>(
                "Bybit",
                default!,
                new ServerError(
                    "network-code",
                    new ErrorInfo(ErrorType.NetworkError, true, secret, ["network-code"]),
                    null!)
                {
                    Message = secret,
                }));

        await CreateProvider(
                trading,
                loggerFactory: loggerFactory,
                retryDelay: static (_, _) => Task.CompletedTask)
            .GetOpenPositionsAsync(MarketCategory.Linear);

        var entry = sink.Entries.Should().ContainSingle().Subject;
        entry.EventId.Id.Should().Be(1009);
        entry.LogLevel.Should().Be(LogLevel.Warning);
        entry.Fields.Any(field => field.Key == "FailureKind" && field.Value?.ToString() == nameof(ExchangeFailureKind.Unavailable)).Should().BeTrue();
        entry.Fields.Any(field => field.Key == "Retryable" && Equals(field.Value, true)).Should().BeTrue();
        entry.Fields.Any(field => field.Key is "Error" or "Message").Should().BeFalse();
        entry.Fields.Select(field => field.Value?.ToString()).Any(value => value?.Contains(secret, StringComparison.Ordinal) == true).Should().BeFalse();
    }

    [Fact]
    public async Task Cancellation_Does_Not_Log_Error_Or_Retry()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var sink = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(sink));
        var account = new Mock<IBybitRestClientApiAccount>();
        account
            .Setup(a => a.GetBalancesAsync(
                BybitAccountType.Unified,
                null,
                cancellation.Token))
            .Returns(() => Task.FromCanceled<HttpResult<BybitResponse<BybitBalance>>>(cancellation.Token));

        var act = () => CreateProvider(
                new Mock<IBybitRestClientApiTrading>(),
                account,
                loggerFactory,
                static (_, _) => Task.CompletedTask)
            .GetWalletBalanceAsync(DomainAccountType.Unified, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        account.Verify(a => a.GetBalancesAsync(
            BybitAccountType.Unified,
            null,
            cancellation.Token), Times.Once);
        sink.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Successful_Balance_Emits_Activity_And_Request_Duration_Metrics()
    {
        var activities = new List<Activity>();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == BybitExchangeTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => activities.Add(activity),
        };
        ActivitySource.AddActivityListener(activityListener);

        var measurements = new List<MetricMeasurement>();
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == BybitExchangeTelemetry.MeterName)
                listener.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            measurements.Add(new(instrument.Name, value, tags.ToArray())));
        meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            measurements.Add(new(instrument.Name, value, tags.ToArray())));
        meterListener.Start();

        var account = new Mock<IBybitRestClientApiAccount>();
        account
            .Setup(a => a.GetBalancesAsync(
                BybitAccountType.Unified,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccess(new BybitResponse<BybitBalance>
            {
                List = [new BybitBalance { AccountType = BybitAccountType.Unified }],
            }));

        var observation = await CreateProvider(
                new Mock<IBybitRestClientApiTrading>(),
                account,
                retryDelay: static (_, _) => Task.CompletedTask)
            .GetWalletBalanceAsync(DomainAccountType.Unified);

        observation.Status.Should().Be(AccountBalanceObservationStatus.Complete);
        var activity = activities.Should().ContainSingle().Subject;
        activity.OperationName.Should().Be(BybitExchangeTelemetry.BalanceOperation);
        activity.GetTagItem("exchange.outcome").Should().Be(BybitExchangeTelemetry.SuccessOutcome);
        activity.GetTagItem("retry.count").Should().Be(0);
        measurements.Any(measurement => measurement.Name == "exchange.requests" && Equals(measurement.Value, 1L)).Should().BeTrue();
        measurements.Any(measurement => measurement.Name == "exchange.request.duration" && measurement.Value is double).Should().BeTrue();
        measurements.SelectMany(measurement => measurement.Tags.Select(tag => tag.Key))
            .Should().NotContain("symbol");
    }

    private static BybitPrivateAccountProvider CreateProvider(
        Mock<IBybitRestClientApiTrading> trading,
        Mock<IBybitRestClientApiAccount>? account = null,
        ILoggerFactory? loggerFactory = null,
        Func<TimeSpan, CancellationToken, Task>? retryDelay = null)
    {
        var v5Api = new Mock<IBybitRestClientApi>();
        v5Api.SetupGet(a => a.Trading).Returns(trading.Object);
        if (account is not null)
        {
            v5Api.SetupGet(a => a.Account).Returns(account.Object);
        }

        var client = new Mock<IBybitRestClient>();
        client.SetupGet(c => c.V5Api).Returns(v5Api.Object);

        loggerFactory ??= LoggerFactory.Create(_ => { });
        return new BybitPrivateAccountProvider(
            client.Object,
            loggerFactory.CreateLogger<BybitPrivateAccountProvider>(),
            retryDelay);
    }

    private static HttpResult<T> CreateSuccess<T>(T data) => new("Bybit", data, null!);

    private static HttpResult<T> CreateError<T>(string message) =>
        new("Bybit", default!, new ServerError(ErrorType.Unknown, message, null!) { Message = message });

    private static HttpResult<T> CreateProviderError<T>(string providerCode, ErrorType errorType) =>
        new(
            "Bybit",
            default!,
            new ServerError(
                providerCode,
                new ErrorInfo(errorType, isTransient: false, "provider failure", [providerCode]),
                null!));

    private static HttpResult<T> CreateRateLimitError<T>(DateTime retryAfter) =>
        new(
            "Bybit",
            default!,
            new ServerRateLimitError("rate limited", null!)
            {
                RetryAfter = retryAfter,
            });

    private sealed record CapturedLog(
        LogLevel LogLevel,
        EventId EventId,
        IReadOnlyList<KeyValuePair<string, object?>> Fields);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<CapturedLog> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Entries);

        public void Dispose()
        {
        }
    }

    private sealed class CapturingLogger(List<CapturedLog> entries) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NoopScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var fields = state is IEnumerable<KeyValuePair<string, object?>> structuredState
                ? structuredState.ToList()
                : [];
            entries.Add(new CapturedLog(logLevel, eventId, fields));
        }
    }

    private sealed class NoopScope : IDisposable
    {
        public static NoopScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }

    private sealed record MetricMeasurement(
        string Name,
        object Value,
        IReadOnlyList<KeyValuePair<string, object?>> Tags);
}
