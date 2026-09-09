using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Accounts.Credentials;
using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Events;
using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Portfolio;
using Intelligence.TradeSystem.Domain.Snapshots;
using Moq;

namespace Intelligence.TradeSystem.Application.Tests.Accounts;

public sealed class ExchangeAccountSyncServiceTests
{
    private static readonly DateTimeOffset CalculatedAt =
        new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ObservedAt = CalculatedAt.AddMinutes(-1);

    [Fact]
    public async Task SynchronizeAsync_Persists_Reconciled_State_And_Records_Successful_Sync()
    {
        var fixture = CreateFixture();
        var balance = new AccountBalance(AccountType.Unified, 1_000m, 950m, 800m, 50m, []);
        var observation = OpenPositionsObservation.Complete(
            MarketCategory.Linear,
            null,
            ObservedAt,
            [CreateOpenPosition()]);
        fixture.Provider
            .Setup(provider => provider.GetWalletBalanceAsync(
                AccountType.Unified,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccountBalanceObservation.Complete(balance, ObservedAt));
        fixture.Provider
            .Setup(provider => provider.GetOpenPositionsAsync(
                MarketCategory.Linear,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(observation);
        fixture.PositionRepository
            .Setup(repository => repository.GetByExchangeAccountAsync(
                fixture.UserId,
                fixture.Account.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        fixture.PositionRepository
            .Setup(repository => repository.SaveAsync(
                fixture.UserId,
                It.IsAny<Position>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConcurrencyVersion.Initial);
        fixture.PortfolioRepository
            .Setup(repository => repository.SaveAsync(
                fixture.UserId,
                It.IsAny<PortfolioState>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        fixture.AccountRepository
            .Setup(repository => repository.SaveAsync(
                fixture.UserId,
                It.Is<ExchangeAccount>(account =>
                    account.ConnectionStatus == ExchangeAccountConnectionStatus.Connected &&
                    account.LastSyncedAt.HasValue &&
                    account.LastError == null),
                fixture.AccountVersion,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConcurrencyVersion(2));

        var result = await fixture.Service.SynchronizeAsync(fixture.UserId, fixture.Account.Id);

        result.Outcome.Should().Be(ExchangeAccountSyncOutcome.Synchronized);
        result.Account.Should().BeSameAs(fixture.Account);
        result.PortfolioState.Should().NotBeNull();
        result.PortfolioState!.Capital.TotalEquity.Should().Be(1_000m);
        result.PortfolioState.Positions.Should().ContainSingle();
        result.PortfolioState.PositionsFullyReconciled.Should().BeTrue();
        result.PortfolioState.IsFresh.Should().BeTrue();
        result.PortfolioState.IsComplete.Should().BeTrue();
        fixture.CredentialStore.Verify(
            store => store.GetAsync(
                fixture.UserId,
                fixture.Account.Id,
                It.IsAny<CancellationToken>()),
            Times.Once);
        fixture.Provider.VerifyAll();
        fixture.PositionRepository.VerifyAll();
        fixture.PortfolioRepository.VerifyAll();
        fixture.AccountRepository.VerifyAll();
    }

    [Fact]
    public async Task SynchronizeAsync_Duplicate_Observation_Is_A_NoOp()
    {
        var fixture = CreateFixture();
        SetupSuccessfulObservation(fixture, ObservedAt);
        SetupPositionLoad(fixture);
        var savedStates = new List<PortfolioState>();
        SetupPortfolioSave(fixture, savedStates);
        fixture.AccountRepository
            .Setup(repository => repository.SaveAsync(
                fixture.UserId,
                It.Is<ExchangeAccount>(account =>
                    account.LastAppliedBalanceObservationAt == ObservedAt &&
                    account.LastAppliedPositionsObservationAt == ObservedAt &&
                    account.ConnectionStatus == ExchangeAccountConnectionStatus.Connected),
                fixture.AccountVersion,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConcurrencyVersion(2));

        var first = await fixture.Service.SynchronizeAsync(fixture.UserId, fixture.Account.Id);
        var second = await fixture.Service.SynchronizeAsync(fixture.UserId, fixture.Account.Id);

        first.Outcome.Should().Be(ExchangeAccountSyncOutcome.Synchronized);
        second.Outcome.Should().Be(ExchangeAccountSyncOutcome.AlreadyApplied);
        second.Account.Should().BeSameAs(fixture.Account);
        savedStates.Should().ContainSingle();
        fixture.PositionRepository.Verify(
            repository => repository.SaveAsync(
                It.IsAny<UserId>(),
                It.IsAny<Position>(),
                It.IsAny<ConcurrencyVersion?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        fixture.PortfolioRepository.Verify(
            repository => repository.SaveAsync(
                fixture.UserId,
                It.IsAny<PortfolioState>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        fixture.AccountRepository.Verify(
            repository => repository.SaveAsync(
                fixture.UserId,
                It.IsAny<ExchangeAccount>(),
                It.IsAny<ConcurrencyVersion?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SynchronizeAsync_New_Balance_With_Superseded_Positions_Uses_Current_Positions()
    {
        var previousSyncAt = ObservedAt.AddMinutes(-2);
        var fixture = CreateFixture(
            ExchangeAccount.Create(
                ExchangeAccountId.New(),
                UserId.New(),
                ExchangeId.Bybit,
                ExchangeAccountConnectionStatus.Connected,
                RequiredCapabilities,
                lastSyncedAt: previousSyncAt,
                lastAppliedBalanceObservationAt: ObservedAt.AddMinutes(-1),
                lastAppliedPositionsObservationAt: ObservedAt.AddMinutes(-1)));
        var trackedPosition = CreateTrackedPosition(fixture, "BTCUSDT");
        var previousPortfolio = PortfolioState.Create(
            fixture.Account.Id,
            [trackedPosition],
            new PortfolioCapitalState(900m, 700m, previousSyncAt, 800m),
            ObservedAt,
            TimeSpan.FromMinutes(5));
        fixture.Provider
            .Setup(provider => provider.GetWalletBalanceAsync(
                AccountType.Unified,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccountBalanceObservation.Complete(
                new AccountBalance(AccountType.Unified, 1_000m, 950m, 800m, 50m, []),
                ObservedAt));
        fixture.Provider
            .Setup(provider => provider.GetOpenPositionsAsync(
                MarketCategory.Linear,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OpenPositionsObservation.Failed(
                MarketCategory.Linear,
                null,
                ObservedAt.AddMinutes(-2),
                "old positions failure"));
        SetupPositionLoad(fixture, trackedPosition);
        fixture.PortfolioRepository
            .Setup(repository => repository.GetLatestAsync(
                fixture.UserId,
                fixture.Account.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(previousPortfolio);
        SetupPortfolioSave(fixture, []);
        fixture.AccountRepository
            .Setup(repository => repository.SaveAsync(
                fixture.UserId,
                It.Is<ExchangeAccount>(saved =>
                    saved.ConnectionStatus == ExchangeAccountConnectionStatus.Connected &&
                    saved.LastError == null &&
                    saved.LastSyncedAt == previousSyncAt &&
                    saved.LastAppliedBalanceObservationAt == ObservedAt &&
                    saved.LastAppliedPositionsObservationAt == ObservedAt.AddMinutes(-1)),
                fixture.AccountVersion,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConcurrencyVersion(2));

        var result = await fixture.Service.SynchronizeAsync(fixture.UserId, fixture.Account.Id);

        result.Outcome.Should().Be(ExchangeAccountSyncOutcome.Synchronized);
        result.PortfolioState!.Capital.TotalEquity.Should().Be(1_000m);
        result.PortfolioState.Positions.Should().ContainSingle();
        result.PortfolioState.Positions[0].TrackingState.Should().Be(PositionTrackingState.Active);
        fixture.Account.ConnectionStatus.Should().Be(ExchangeAccountConnectionStatus.Connected);
        fixture.Account.LastSyncedAt.Should().Be(previousSyncAt);
        fixture.Account.LastError.Should().BeNull();
    }

    [Fact]
    public async Task SynchronizeAsync_Old_Balance_With_New_Positions_Uses_Last_Known_Capital()
    {
        var previousSyncAt = ObservedAt.AddMinutes(-2);
        var fixture = CreateFixture(
            ExchangeAccount.Create(
                ExchangeAccountId.New(),
                UserId.New(),
                ExchangeId.Bybit,
                ExchangeAccountConnectionStatus.Connected,
                RequiredCapabilities,
                lastSyncedAt: previousSyncAt,
                lastAppliedBalanceObservationAt: ObservedAt.AddMinutes(-1),
                lastAppliedPositionsObservationAt: ObservedAt.AddMinutes(-2)));
        var trackedPosition = CreateTrackedPosition(fixture, "BTCUSDT");
        var previousPortfolio = PortfolioState.Create(
            fixture.Account.Id,
            [trackedPosition],
            new PortfolioCapitalState(900m, 700m, previousSyncAt, 800m),
            ObservedAt,
            TimeSpan.FromMinutes(5));
        fixture.Provider
            .Setup(provider => provider.GetWalletBalanceAsync(
                AccountType.Unified,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccountBalanceObservation.Complete(
                new AccountBalance(AccountType.Unified, 1_100m, 1_000m, 900m, 50m, []),
                ObservedAt.AddMinutes(-2)));
        fixture.Provider
            .Setup(provider => provider.GetOpenPositionsAsync(
                MarketCategory.Linear,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OpenPositionsObservation.Complete(
                MarketCategory.Linear,
                null,
                ObservedAt,
                [CreateOpenPosition() with { Size = 2m, PositionValue = 200m }]));
        SetupPositionLoad(fixture, trackedPosition);
        SetupPositionSave(fixture);
        fixture.PortfolioRepository
            .Setup(repository => repository.GetLatestAsync(
                fixture.UserId,
                fixture.Account.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(previousPortfolio);
        SetupPortfolioSave(fixture, []);
        fixture.AccountRepository
            .Setup(repository => repository.SaveAsync(
                fixture.UserId,
                It.Is<ExchangeAccount>(saved =>
                    saved.ConnectionStatus == ExchangeAccountConnectionStatus.Connected &&
                    saved.LastError == null &&
                    saved.LastSyncedAt == previousSyncAt &&
                    saved.LastAppliedBalanceObservationAt == ObservedAt.AddMinutes(-1) &&
                    saved.LastAppliedPositionsObservationAt == ObservedAt),
                fixture.AccountVersion,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConcurrencyVersion(2));

        var result = await fixture.Service.SynchronizeAsync(fixture.UserId, fixture.Account.Id);

        result.Outcome.Should().Be(ExchangeAccountSyncOutcome.Synchronized);
        result.PortfolioState!.Capital.TotalEquity.Should().Be(900m);
        result.PortfolioState.Positions.Should().ContainSingle()
            .Which.Size.Should().Be(2m);
        fixture.Account.LastSyncedAt.Should().Be(previousSyncAt);
        fixture.Account.LastError.Should().BeNull();
    }

    [Fact]
    public async Task SynchronizeAsync_Retry_Uses_The_Same_Provider_Observations()
    {
        var fixture = CreateFixture();
        SetupSuccessfulObservation(fixture, ObservedAt);
        SetupPositionLoad(fixture);
        SetupPortfolioSave(fixture, []);
        fixture.AccountRepository
            .SetupSequence(repository => repository.SaveAsync(
                fixture.UserId,
                It.IsAny<ExchangeAccount>(),
                fixture.AccountVersion,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConcurrencyConflictException("simulated race"))
            .ReturnsAsync(new ConcurrencyVersion(2));

        var result = await fixture.Service.SynchronizeAsync(fixture.UserId, fixture.Account.Id);

        result.Outcome.Should().Be(ExchangeAccountSyncOutcome.Synchronized);
        fixture.Provider.Verify(
            provider => provider.GetWalletBalanceAsync(
                AccountType.Unified,
                It.IsAny<CancellationToken>()),
            Times.Once);
        fixture.Provider.Verify(
            provider => provider.GetOpenPositionsAsync(
                MarketCategory.Linear,
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
        fixture.AccountRepository.Verify(
            repository => repository.SaveAsync(
                fixture.UserId,
                It.IsAny<ExchangeAccount>(),
                fixture.AccountVersion,
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task SynchronizeAsync_Retry_Recognizes_Already_Applied_Observation()
    {
        var fixture = CreateFixture();
        SetupSuccessfulObservation(fixture, ObservedAt);
        SetupPositionLoad(fixture);
        SetupPortfolioSave(fixture, []);
        var alreadyApplied = ExchangeAccount.Create(
            fixture.Account.Id,
            fixture.UserId,
            ExchangeId.Bybit,
            ExchangeAccountConnectionStatus.Connected,
            RequiredCapabilities,
            lastAppliedBalanceObservationAt: ObservedAt,
            lastAppliedPositionsObservationAt: ObservedAt);
        fixture.AccountRepository
            .SetupSequence(repository => repository.GetByIdAsync(
                fixture.UserId,
                fixture.Account.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Versioned<ExchangeAccount>(fixture.Account, fixture.AccountVersion))
            .ReturnsAsync(new Versioned<ExchangeAccount>(fixture.Account, fixture.AccountVersion))
            .ReturnsAsync(new Versioned<ExchangeAccount>(alreadyApplied, new ConcurrencyVersion(2)));
        fixture.AccountRepository
            .Setup(repository => repository.SaveAsync(
                fixture.UserId,
                It.IsAny<ExchangeAccount>(),
                fixture.AccountVersion,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConcurrencyConflictException("simulated race"));

        var result = await fixture.Service.SynchronizeAsync(fixture.UserId, fixture.Account.Id);

        result.Outcome.Should().Be(ExchangeAccountSyncOutcome.AlreadyApplied);
        result.Account.Should().BeSameAs(alreadyApplied);
        fixture.AccountRepository.Verify(
            repository => repository.SaveAsync(
                fixture.UserId,
                It.IsAny<ExchangeAccount>(),
                It.IsAny<ConcurrencyVersion?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        fixture.Provider.Verify(
            provider => provider.GetWalletBalanceAsync(
                AccountType.Unified,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SynchronizeAsync_Retry_Recognizes_Superseded_Observation()
    {
        var fixture = CreateFixture();
        var olderObservation = ObservedAt.AddMinutes(-1);
        SetupSuccessfulObservation(fixture, olderObservation);
        SetupPositionLoad(fixture);
        SetupPortfolioSave(fixture, []);
        var newerAccount = ExchangeAccount.Create(
            fixture.Account.Id,
            fixture.UserId,
            ExchangeId.Bybit,
            ExchangeAccountConnectionStatus.Connected,
            RequiredCapabilities,
            lastAppliedBalanceObservationAt: ObservedAt,
            lastAppliedPositionsObservationAt: ObservedAt);
        fixture.AccountRepository
            .SetupSequence(repository => repository.GetByIdAsync(
                fixture.UserId,
                fixture.Account.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Versioned<ExchangeAccount>(fixture.Account, fixture.AccountVersion))
            .ReturnsAsync(new Versioned<ExchangeAccount>(fixture.Account, fixture.AccountVersion))
            .ReturnsAsync(new Versioned<ExchangeAccount>(newerAccount, new ConcurrencyVersion(2)));
        fixture.AccountRepository
            .Setup(repository => repository.SaveAsync(
                fixture.UserId,
                It.IsAny<ExchangeAccount>(),
                fixture.AccountVersion,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConcurrencyConflictException("simulated race"));

        var result = await fixture.Service.SynchronizeAsync(fixture.UserId, fixture.Account.Id);

        result.Outcome.Should().Be(ExchangeAccountSyncOutcome.Superseded);
        result.Account.Should().BeSameAs(newerAccount);
        fixture.PortfolioRepository.Verify(
            repository => repository.SaveAsync(
                It.IsAny<UserId>(),
                It.IsAny<PortfolioState>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SynchronizeAsync_Does_Not_Apply_Fetched_Observation_After_Account_Is_Disabled()
    {
        var fixture = CreateFixture();
        SetupSuccessfulObservation(fixture, ObservedAt);
        SetupPositionLoad(fixture);
        SetupPortfolioSave(fixture, []);
        var disabled = ExchangeAccount.Create(
            fixture.Account.Id,
            fixture.UserId,
            ExchangeId.Bybit,
            ExchangeAccountConnectionStatus.Disabled,
            RequiredCapabilities);
        fixture.AccountRepository
            .SetupSequence(repository => repository.GetByIdAsync(
                fixture.UserId,
                fixture.Account.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Versioned<ExchangeAccount>(fixture.Account, fixture.AccountVersion))
            .ReturnsAsync(new Versioned<ExchangeAccount>(fixture.Account, fixture.AccountVersion))
            .ReturnsAsync(new Versioned<ExchangeAccount>(disabled, new ConcurrencyVersion(2)));
        fixture.AccountRepository
            .Setup(repository => repository.SaveAsync(
                fixture.UserId,
                It.IsAny<ExchangeAccount>(),
                fixture.AccountVersion,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConcurrencyConflictException("simulated race"));

        var result = await fixture.Service.SynchronizeAsync(fixture.UserId, fixture.Account.Id);

        result.Outcome.Should().Be(ExchangeAccountSyncOutcome.AccountDisabled);
        fixture.PortfolioRepository.Verify(
            repository => repository.SaveAsync(
                It.IsAny<UserId>(),
                It.IsAny<PortfolioState>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        fixture.AccountRepository.Verify(
            repository => repository.SaveAsync(
                It.IsAny<UserId>(),
                It.IsAny<ExchangeAccount>(),
                It.IsAny<ConcurrencyVersion?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SynchronizeAsync_Stops_After_The_Bounded_Persistence_Retry_Limit()
    {
        var fixture = CreateFixture();
        SetupSuccessfulObservation(fixture, ObservedAt);
        SetupPositionLoad(fixture);
        SetupPortfolioSave(fixture, []);
        fixture.AccountRepository
            .Setup(repository => repository.SaveAsync(
                fixture.UserId,
                It.IsAny<ExchangeAccount>(),
                fixture.AccountVersion,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConcurrencyConflictException("persistent race"));

        await FluentActions
            .Invoking(() => fixture.Service.SynchronizeAsync(fixture.UserId, fixture.Account.Id))
            .Should()
            .ThrowAsync<ConcurrencyConflictException>();

        fixture.Provider.Verify(
            provider => provider.GetWalletBalanceAsync(
                AccountType.Unified,
                It.IsAny<CancellationToken>()),
            Times.Once);
        fixture.AccountRepository.Verify(
            repository => repository.SaveAsync(
                fixture.UserId,
                It.IsAny<ExchangeAccount>(),
                fixture.AccountVersion,
                It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task SynchronizeAsync_Cancellation_Prevents_A_Further_Persistence_Attempt()
    {
        var fixture = CreateFixture();
        using var cancellation = new CancellationTokenSource();
        SetupSuccessfulObservation(fixture, ObservedAt);
        SetupPositionLoad(fixture);
        SetupPortfolioSave(fixture, []);
        fixture.AccountRepository
            .Setup(repository => repository.SaveAsync(
                fixture.UserId,
                It.IsAny<ExchangeAccount>(),
                fixture.AccountVersion,
                It.IsAny<CancellationToken>()))
            .Callback<UserId, ExchangeAccount, ConcurrencyVersion?, CancellationToken>(
                (_, _, _, _) => cancellation.Cancel())
            .ThrowsAsync(new ConcurrencyConflictException("simulated race"));

        await FluentActions
            .Invoking(() => fixture.Service.SynchronizeAsync(
                fixture.UserId,
                fixture.Account.Id,
                cancellation.Token))
            .Should()
            .ThrowAsync<OperationCanceledException>();

        fixture.Provider.Verify(
            provider => provider.GetWalletBalanceAsync(
                AccountType.Unified,
                It.IsAny<CancellationToken>()),
            Times.Once);
        fixture.AccountRepository.Verify(
            repository => repository.SaveAsync(
                fixture.UserId,
                It.IsAny<ExchangeAccount>(),
                It.IsAny<ConcurrencyVersion?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SynchronizeAsync_Treats_Complete_Empty_Positions_As_A_Valid_Observation()
    {
        var fixture = CreateFixture();
        var trackedPosition = Position.Create(
            ExchangePositionKey.Create(
                fixture.Account.Id,
                InstrumentId.From("BTCUSDT"),
                PositionSide.Long,
                0),
            MarketCategory.Linear,
            1m,
            ObservedAt.AddMinutes(-1),
            ObservedAt.AddMinutes(-1),
            averageEntryPrice: 100m,
            positionValue: 100m,
            leverage: 2m,
            markPrice: 100m,
            unrealizedPnl: 0m);
        fixture.Provider
            .Setup(provider => provider.GetWalletBalanceAsync(
                AccountType.Unified,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccountBalanceObservation.Complete(
                new AccountBalance(AccountType.Unified, 1_000m, 950m, 800m, 50m, []),
                ObservedAt));
        fixture.Provider
            .Setup(provider => provider.GetOpenPositionsAsync(
                MarketCategory.Linear,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OpenPositionsObservation.Complete(
                MarketCategory.Linear,
                null,
                ObservedAt,
                []));
        fixture.PositionRepository
            .Setup(repository => repository.GetByExchangeAccountAsync(
                fixture.UserId,
                fixture.Account.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                [new Versioned<Position>(trackedPosition, ConcurrencyVersion.Initial)]);
        fixture.PositionRepository
            .Setup(repository => repository.SaveAsync(
                fixture.UserId,
                trackedPosition,
                ConcurrencyVersion.Initial,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConcurrencyVersion(2));
        fixture.PortfolioRepository
            .Setup(repository => repository.SaveAsync(
                fixture.UserId,
                It.IsAny<PortfolioState>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        fixture.AccountRepository
            .Setup(repository => repository.SaveAsync(
                fixture.UserId,
                It.IsAny<ExchangeAccount>(),
                fixture.AccountVersion,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConcurrencyVersion(2));

        var result = await fixture.Service.SynchronizeAsync(fixture.UserId, fixture.Account.Id);

        result.Outcome.Should().Be(ExchangeAccountSyncOutcome.Synchronized);
        trackedPosition.TrackingState.Should().Be(PositionTrackingState.Closed);
        result.PortfolioState!.Positions.Should().BeEmpty();
        result.PortfolioState.PositionsFullyReconciled.Should().BeTrue();
        result.PortfolioState.IsFresh.Should().BeTrue();
        result.PortfolioState.IsComplete.Should().BeTrue();
        fixture.PositionRepository.VerifyAll();
        fixture.PortfolioRepository.VerifyAll();
        fixture.AccountRepository.VerifyAll();
    }

    [Fact]
    public async Task SynchronizeAsync_Returns_NotFound_Without_Reading_Credentials()
    {
        var fixture = CreateFixture();
        fixture.AccountRepository
            .Setup(repository => repository.GetByIdAsync(
                fixture.UserId,
                fixture.Account.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Versioned<ExchangeAccount>?)null);

        var result = await fixture.Service.SynchronizeAsync(fixture.UserId, fixture.Account.Id);

        result.Outcome.Should().Be(ExchangeAccountSyncOutcome.NotFound);
        fixture.CredentialStore.Verify(
            store => store.GetAsync(
                It.IsAny<UserId>(),
                It.IsAny<ExchangeAccountId>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        fixture.Factory.Verify(
            factory => factory.Create(
                It.IsAny<ExchangeId>(),
                It.IsAny<ExchangeAccountCredential>()),
            Times.Never);
    }

    [Fact]
    public async Task SynchronizeAsync_Treats_Foreign_Account_As_NotFound()
    {
        var fixture = CreateFixture();
        var foreignUserId = UserId.New();
        fixture.AccountRepository
            .Setup(repository => repository.GetByIdAsync(
                foreignUserId,
                fixture.Account.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Versioned<ExchangeAccount>?)null);

        var result = await fixture.Service.SynchronizeAsync(foreignUserId, fixture.Account.Id);

        result.Outcome.Should().Be(ExchangeAccountSyncOutcome.NotFound);
    }

    [Fact]
    public async Task SynchronizeAsync_Does_Not_Use_Credentials_For_Disabled_Account()
    {
        var disabledAccount = ExchangeAccount.Create(
            ExchangeAccountId.New(),
            UserId.New(),
            ExchangeId.Bybit,
            ExchangeAccountConnectionStatus.Disabled,
            RequiredCapabilities);
        var fixture = CreateFixture(disabledAccount);

        var result = await fixture.Service.SynchronizeAsync(fixture.UserId, fixture.Account.Id);

        result.Outcome.Should().Be(ExchangeAccountSyncOutcome.AccountDisabled);
        fixture.CredentialStore.Verify(
            store => store.GetAsync(
                It.IsAny<UserId>(),
                It.IsAny<ExchangeAccountId>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        fixture.Factory.Verify(
            factory => factory.Create(
                It.IsAny<ExchangeId>(),
                It.IsAny<ExchangeAccountCredential>()),
            Times.Never);
    }

    [Fact]
    public async Task SynchronizeAsync_Returns_CredentialsUnavailable_Without_Calling_Exchange()
    {
        var fixture = CreateFixture();
        fixture.CredentialStore
            .Setup(store => store.GetAsync(
                fixture.UserId,
                fixture.Account.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExchangeAccountCredential?)null);

        var result = await fixture.Service.SynchronizeAsync(fixture.UserId, fixture.Account.Id);

        result.Outcome.Should().Be(ExchangeAccountSyncOutcome.CredentialsUnavailable);
        fixture.Factory.Verify(
            factory => factory.Create(
                It.IsAny<ExchangeId>(),
                It.IsAny<ExchangeAccountCredential>()),
            Times.Never);
    }

    [Fact]
    public async Task SynchronizeAsync_Persists_Last_Known_Capital_And_Closes_From_Complete_Positions_When_Balance_Fails()
    {
        var fixture = CreateFixture();
        var previousSyncAt = ObservedAt.AddMinutes(-2);
        fixture.Account.RecordSuccessfulSync(previousSyncAt);
        var trackedPosition = Position.Create(
            ExchangePositionKey.Create(
                fixture.Account.Id,
                InstrumentId.From("BTCUSDT"),
                PositionSide.Long,
                0),
            MarketCategory.Linear,
            1m,
            ObservedAt.AddMinutes(-1),
            ObservedAt.AddMinutes(-1),
            averageEntryPrice: 100m,
            positionValue: 100m,
            leverage: 2m,
            unrealizedPnl: 0m);
        var previousState = PortfolioState.Create(
            fixture.Account.Id,
            [trackedPosition],
            new PortfolioCapitalState(1_000m, 800m, ObservedAt.AddMinutes(-1), 900m),
            ObservedAt,
            TimeSpan.FromMinutes(5));
        fixture.Provider
            .Setup(provider => provider.GetWalletBalanceAsync(
                AccountType.Unified,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccountBalanceObservation.Failed(
                new ExchangeFailure(ExchangeFailureKind.Unavailable, Retryable: true),
                ObservedAt));
        fixture.Provider
            .Setup(provider => provider.GetOpenPositionsAsync(
                MarketCategory.Linear,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OpenPositionsObservation.Complete(
                MarketCategory.Linear,
                null,
                ObservedAt,
                []));
        fixture.PositionRepository
            .Setup(repository => repository.GetByExchangeAccountAsync(
                fixture.UserId,
                fixture.Account.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Versioned<Position>(trackedPosition, ConcurrencyVersion.Initial)]);
        fixture.PositionRepository
            .Setup(repository => repository.SaveAsync(
                fixture.UserId,
                trackedPosition,
                ConcurrencyVersion.Initial,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConcurrencyVersion(2));
        fixture.PortfolioRepository
            .Setup(repository => repository.GetLatestAsync(
                fixture.UserId,
                fixture.Account.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(previousState);
        PortfolioState? savedState = null;
        fixture.PortfolioRepository
            .Setup(repository => repository.SaveAsync(
                fixture.UserId,
                It.IsAny<PortfolioState>(),
                It.IsAny<CancellationToken>()))
            .Callback<UserId, PortfolioState, CancellationToken>((_, state, _) => savedState = state)
            .Returns(Task.CompletedTask);
        fixture.AccountRepository
            .Setup(repository => repository.SaveAsync(
                fixture.UserId,
                It.Is<ExchangeAccount>(account =>
                    account.ConnectionStatus == ExchangeAccountConnectionStatus.Unavailable &&
                    account.LastError == "balance_failed" &&
                    account.LastSyncedAt == previousSyncAt),
                fixture.AccountVersion,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConcurrencyVersion(2));

        var result = await fixture.Service.SynchronizeAsync(fixture.UserId, fixture.Account.Id);

        result.Outcome.Should().Be(ExchangeAccountSyncOutcome.ExchangeUnavailable);
        result.Account.Should().NotBeNull();
        result.Account!.LastSyncedAt.Should().Be(previousSyncAt);
        trackedPosition.TrackingState.Should().Be(PositionTrackingState.Closed);
        savedState.Should().NotBeNull();
        savedState!.Capital.TotalEquity.Should().Be(1_000m);
        savedState.Capital.AvailableCapital.Should().Be(800m);
        savedState.Capital.TotalWalletBalance.Should().Be(900m);
        savedState.Capital.ObservedAt.Should().Be(ObservedAt.AddMinutes(-1));
        fixture.Account.ConnectionStatus.Should().Be(ExchangeAccountConnectionStatus.Unavailable);
        fixture.Account.LastSyncedAt.Should().Be(previousSyncAt);
        fixture.Account.LastError.Should().Be("balance_failed");
        fixture.Provider.Verify(
            provider => provider.GetOpenPositionsAsync(
                It.IsAny<MarketCategory>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        fixture.PositionRepository.VerifyAll();
        fixture.PortfolioRepository.VerifyAll();
        fixture.AccountRepository.VerifyAll();
    }

    [Fact]
    public async Task SynchronizeAsync_Persists_Unknown_Capital_When_Balance_Fails_Without_Previous_Portfolio()
    {
        var fixture = CreateFixture();
        fixture.Provider
            .Setup(provider => provider.GetWalletBalanceAsync(
                AccountType.Unified,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccountBalanceObservation.Failed(
                new ExchangeFailure(ExchangeFailureKind.Unavailable, Retryable: true),
                ObservedAt));
        fixture.Provider
            .Setup(provider => provider.GetOpenPositionsAsync(
                MarketCategory.Linear,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OpenPositionsObservation.Failed(
                MarketCategory.Linear,
                null,
                ObservedAt,
                "provider-secret-like-message"));
        fixture.PositionRepository
            .Setup(repository => repository.GetByExchangeAccountAsync(
                fixture.UserId,
                fixture.Account.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        fixture.PortfolioRepository
            .Setup(repository => repository.GetLatestAsync(
                fixture.UserId,
                fixture.Account.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((PortfolioState?)null);
        PortfolioState? savedState = null;
        fixture.PortfolioRepository
            .Setup(repository => repository.SaveAsync(
                fixture.UserId,
                It.IsAny<PortfolioState>(),
                It.IsAny<CancellationToken>()))
            .Callback<UserId, PortfolioState, CancellationToken>((_, state, _) => savedState = state)
            .Returns(Task.CompletedTask);
        fixture.AccountRepository
            .Setup(repository => repository.SaveAsync(
                fixture.UserId,
                It.Is<ExchangeAccount>(account =>
                    account.ConnectionStatus == ExchangeAccountConnectionStatus.Unavailable &&
                    account.LastError == "balance_failed"),
                fixture.AccountVersion,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConcurrencyVersion(2));

        var result = await fixture.Service.SynchronizeAsync(fixture.UserId, fixture.Account.Id);

        result.Outcome.Should().Be(ExchangeAccountSyncOutcome.ExchangeUnavailable);
        savedState.Should().NotBeNull();
        savedState!.Capital.TotalEquity.Should().BeNull();
        savedState.Capital.AvailableCapital.Should().BeNull();
        savedState.Capital.TotalWalletBalance.Should().BeNull();
        savedState.Capital.ObservedAt.Should().BeNull();
        savedState.IsComplete.Should().BeFalse();
        savedState.IsFresh.Should().BeFalse();
        fixture.Account.LastError.Should().Be("balance_failed");
        fixture.Account.LastError.Should().NotContain("provider-secret-like-message");
        fixture.Account.LastSyncedAt.Should().BeNull();
        fixture.PortfolioRepository.VerifyAll();
        fixture.AccountRepository.VerifyAll();
    }

    [Fact]
    public async Task SynchronizeAsync_Persists_Fresh_Balance_And_Unknown_Positions_When_Positions_Are_Partial()
    {
        var fixture = CreateFixture();
        var trackedPosition = Position.Create(
            ExchangePositionKey.Create(
                fixture.Account.Id,
                InstrumentId.From("BTCUSDT"),
                PositionSide.Long,
                0),
            MarketCategory.Linear,
            1m,
            ObservedAt.AddMinutes(-1),
            ObservedAt.AddMinutes(-1),
            averageEntryPrice: 100m,
            positionValue: 100m,
            leverage: 2m,
            unrealizedPnl: 0m);
        fixture.Provider
            .Setup(provider => provider.GetWalletBalanceAsync(
                AccountType.Unified,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccountBalanceObservation.Complete(
                new AccountBalance(AccountType.Unified, 1_000m, 950m, 800m, 50m, []),
                ObservedAt));
        fixture.Provider
            .Setup(provider => provider.GetOpenPositionsAsync(
                MarketCategory.Linear,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OpenPositionsObservation.Partial(
                MarketCategory.Linear,
                null,
                ObservedAt,
                []));
        fixture.PositionRepository
            .Setup(repository => repository.GetByExchangeAccountAsync(
                fixture.UserId,
                fixture.Account.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Versioned<Position>(trackedPosition, ConcurrencyVersion.Initial)]);
        fixture.PositionRepository
            .Setup(repository => repository.SaveAsync(
                fixture.UserId,
                trackedPosition,
                ConcurrencyVersion.Initial,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConcurrencyVersion(2));
        PortfolioState? savedState = null;
        fixture.PortfolioRepository
            .Setup(repository => repository.SaveAsync(
                fixture.UserId,
                It.IsAny<PortfolioState>(),
                It.IsAny<CancellationToken>()))
            .Callback<UserId, PortfolioState, CancellationToken>((_, state, _) => savedState = state)
            .Returns(Task.CompletedTask);
        fixture.AccountRepository
            .Setup(repository => repository.SaveAsync(
                fixture.UserId,
                It.Is<ExchangeAccount>(account =>
                    account.ConnectionStatus == ExchangeAccountConnectionStatus.Unavailable &&
                    account.LastError == "positions_partial" &&
                    account.LastSyncedAt == null),
                fixture.AccountVersion,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConcurrencyVersion(2));

        var result = await fixture.Service.SynchronizeAsync(fixture.UserId, fixture.Account.Id);

        result.Outcome.Should().Be(ExchangeAccountSyncOutcome.ExchangeUnavailable);
        trackedPosition.TrackingState.Should().Be(PositionTrackingState.Unknown);
        savedState.Should().NotBeNull();
        savedState!.Capital.TotalEquity.Should().Be(1_000m);
        savedState.Capital.ObservedAt.Should().Be(ObservedAt);
        savedState.Positions.Should().ContainSingle();
        savedState.Positions[0].TrackingState.Should().Be(PositionTrackingState.Unknown);
        savedState.IsFresh.Should().BeFalse();
        fixture.Account.ConnectionStatus.Should().Be(ExchangeAccountConnectionStatus.Unavailable);
        fixture.Account.LastSyncedAt.Should().BeNull();
        fixture.Account.LastError.Should().Be("positions_partial");
        fixture.PositionRepository.VerifyAll();
        fixture.PortfolioRepository.VerifyAll();
        fixture.AccountRepository.VerifyAll();
    }

    [Fact]
    public async Task SynchronizeAsync_Marks_Empty_Portfolio_Incomplete_When_Positions_Fail()
    {
        var fixture = CreateFixture();
        fixture.Provider
            .Setup(provider => provider.GetWalletBalanceAsync(
                AccountType.Unified,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccountBalanceObservation.Complete(
                new AccountBalance(AccountType.Unified, 1_000m, 950m, 800m, 50m, []),
                ObservedAt));
        fixture.Provider
            .Setup(provider => provider.GetOpenPositionsAsync(
                MarketCategory.Linear,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OpenPositionsObservation.Failed(
                MarketCategory.Linear,
                null,
                ObservedAt,
                "provider failure"));
        SetupPositionLoad(fixture);
        var savedStates = new List<PortfolioState>();
        SetupPortfolioSave(fixture, savedStates);
        SetupAccountSave(fixture, "positions_failed", null);

        var result = await fixture.Service.SynchronizeAsync(fixture.UserId, fixture.Account.Id);

        result.Outcome.Should().Be(ExchangeAccountSyncOutcome.ExchangeUnavailable);
        savedStates.Should().ContainSingle();
        savedStates[0].Positions.Should().BeEmpty();
        savedStates[0].PositionsFullyReconciled.Should().BeFalse();
        savedStates[0].IsFresh.Should().BeFalse();
        savedStates[0].IsComplete.Should().BeFalse();
        fixture.Account.ConnectionStatus.Should().Be(ExchangeAccountConnectionStatus.Unavailable);
        fixture.Account.LastError.Should().Be("positions_failed");
    }

    [Fact]
    public async Task SynchronizeAsync_Marks_Partial_Coverage_Incomplete_When_All_Known_Positions_Are_Returned()
    {
        var fixture = CreateFixture();
        var trackedPosition = CreateTrackedPosition(fixture, "BTCUSDT");
        fixture.Provider
            .Setup(provider => provider.GetWalletBalanceAsync(
                AccountType.Unified,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccountBalanceObservation.Complete(
                new AccountBalance(AccountType.Unified, 1_000m, 950m, 800m, 50m, []),
                ObservedAt));
        fixture.Provider
            .Setup(provider => provider.GetOpenPositionsAsync(
                MarketCategory.Linear,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OpenPositionsObservation.Partial(
                MarketCategory.Linear,
                null,
                ObservedAt,
                [CreateOpenPosition()]));
        SetupPositionLoad(fixture, trackedPosition);
        SetupPositionSave(fixture);
        var savedStates = new List<PortfolioState>();
        SetupPortfolioSave(fixture, savedStates);
        SetupAccountSave(fixture, "positions_partial", null);

        var result = await fixture.Service.SynchronizeAsync(fixture.UserId, fixture.Account.Id);

        result.Outcome.Should().Be(ExchangeAccountSyncOutcome.ExchangeUnavailable);
        trackedPosition.TrackingState.Should().Be(PositionTrackingState.Active);
        savedStates.Should().ContainSingle();
        savedStates[0].PositionsFullyReconciled.Should().BeFalse();
        savedStates[0].IsFresh.Should().BeFalse();
        savedStates[0].IsComplete.Should().BeFalse();
    }

    [Fact]
    public async Task SynchronizeAsync_Marks_Ambiguous_Complete_Coverage_Incomplete()
    {
        var fixture = CreateFixture();
        var trackedPosition = CreateTrackedPosition(fixture, "BTCUSDT");
        var duplicate = CreateOpenPosition();
        fixture.Provider
            .Setup(provider => provider.GetWalletBalanceAsync(
                AccountType.Unified,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccountBalanceObservation.Complete(
                new AccountBalance(AccountType.Unified, 1_000m, 950m, 800m, 50m, []),
                ObservedAt));
        fixture.Provider
            .Setup(provider => provider.GetOpenPositionsAsync(
                MarketCategory.Linear,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OpenPositionsObservation.Complete(
                MarketCategory.Linear,
                null,
                ObservedAt,
                [duplicate, duplicate]));
        SetupPositionLoad(fixture, trackedPosition);
        SetupPositionSave(fixture);
        var savedStates = new List<PortfolioState>();
        SetupPortfolioSave(fixture, savedStates);
        SetupAccountSave(fixture, "positions_ambiguous", null);

        var result = await fixture.Service.SynchronizeAsync(fixture.UserId, fixture.Account.Id);

        result.Outcome.Should().Be(ExchangeAccountSyncOutcome.ExchangeUnavailable);
        savedStates.Should().ContainSingle();
        savedStates[0].PositionsFullyReconciled.Should().BeFalse();
        savedStates[0].IsFresh.Should().BeFalse();
        savedStates[0].IsComplete.Should().BeFalse();
        fixture.Account.LastError.Should().Be("positions_ambiguous");
    }

    [Fact]
    public async Task SynchronizeAsync_Does_Not_Claim_Accountwide_Coverage_When_Other_Category_Is_Tracked()
    {
        var fixture = CreateFixture();
        var linear = CreateTrackedPosition(fixture, "BTCUSDT");
        var inverse = CreateTrackedPosition(fixture, "BTCUSD", MarketCategory.Inverse);
        fixture.Provider
            .Setup(provider => provider.GetWalletBalanceAsync(
                AccountType.Unified,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccountBalanceObservation.Complete(
                new AccountBalance(AccountType.Unified, 1_000m, 950m, 800m, 50m, []),
                ObservedAt));
        fixture.Provider
            .Setup(provider => provider.GetOpenPositionsAsync(
                MarketCategory.Linear,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OpenPositionsObservation.Complete(
                MarketCategory.Linear,
                null,
                ObservedAt,
                [CreateOpenPosition()]));
        SetupPositionLoad(fixture, linear, inverse);
        SetupPositionSave(fixture);
        var savedStates = new List<PortfolioState>();
        SetupPortfolioSave(fixture, savedStates);
        SetupAccountSave(fixture, "positions_ambiguous", null);

        var result = await fixture.Service.SynchronizeAsync(fixture.UserId, fixture.Account.Id);

        result.Outcome.Should().Be(ExchangeAccountSyncOutcome.ExchangeUnavailable);
        savedStates.Should().ContainSingle();
        savedStates[0].Positions.Should().HaveCount(2);
        savedStates[0].PositionsFullyReconciled.Should().BeFalse();
        savedStates[0].IsFresh.Should().BeFalse();
        savedStates[0].IsComplete.Should().BeFalse();
        fixture.Account.LastError.Should().Be("positions_ambiguous");
    }

    [Fact]
    public async Task SynchronizeAsync_Uses_Injected_Time_For_Invalid_Position_Observation()
    {
        var fixture = CreateFixture();
        fixture.Provider
            .Setup(provider => provider.GetWalletBalanceAsync(
                AccountType.Unified,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccountBalanceObservation.Complete(
                new AccountBalance(AccountType.Unified, 1_000m, 950m, 800m, 50m, []),
                ObservedAt));
        fixture.Provider
            .Setup(provider => provider.GetOpenPositionsAsync(
                MarketCategory.Linear,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OpenPositionsObservation
            {
                Status = OpenPositionsObservationStatus.Complete,
                Category = MarketCategory.Linear,
                Symbol = null,
                ObservedAt = default,
                Positions = [],
            });
        SetupPositionLoad(fixture);
        var savedStates = new List<PortfolioState>();
        SetupPortfolioSave(fixture, savedStates);
        SetupAccountSave(fixture, "positions_failed", null);

        await fixture.Service.SynchronizeAsync(fixture.UserId, fixture.Account.Id);

        savedStates.Should().ContainSingle();
        savedStates[0].PositionsFullyReconciled.Should().BeFalse();
        savedStates[0].CalculatedAt.Should().Be(CalculatedAt);
        savedStates[0].IsFresh.Should().BeFalse();
        savedStates[0].IsComplete.Should().BeFalse();
    }

    [Fact]
    public async Task SynchronizeAsync_Persists_Observed_Partial_Position_And_Marks_Missing_Position_Unknown()
    {
        var fixture = CreateFixture();
        var btc = CreateTrackedPosition(fixture, "BTCUSDT");
        var eth = CreateTrackedPosition(fixture, "ETHUSDT");
        fixture.Provider
            .Setup(provider => provider.GetWalletBalanceAsync(
                AccountType.Unified,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccountBalanceObservation.Complete(
                new AccountBalance(AccountType.Unified, 1_000m, 950m, 800m, 50m, []),
                ObservedAt));
        fixture.Provider
            .Setup(provider => provider.GetOpenPositionsAsync(
                MarketCategory.Linear,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OpenPositionsObservation.Partial(
                MarketCategory.Linear,
                null,
                ObservedAt,
                [CreateOpenPosition() with { Symbol = "BTCUSDT", Size = 2m }]));
        SetupPositionLoad(fixture, btc, eth);
        SetupPositionSave(fixture);
        var savedStates = new List<PortfolioState>();
        SetupPortfolioSave(fixture, savedStates);
        SetupAccountSave(fixture, "positions_partial", null);

        var result = await fixture.Service.SynchronizeAsync(fixture.UserId, fixture.Account.Id);

        result.Outcome.Should().Be(ExchangeAccountSyncOutcome.ExchangeUnavailable);
        btc.Size.Should().Be(2m);
        btc.TrackingState.Should().Be(PositionTrackingState.Active);
        eth.TrackingState.Should().Be(PositionTrackingState.Unknown);
        eth.TrackingState.Should().NotBe(PositionTrackingState.Closed);
        savedStates.Should().ContainSingle();
        savedStates[0].Positions.Should().HaveCount(2);
        savedStates[0].Positions.Should().Contain(position =>
            position.ExchangePositionKey.InstrumentId.Value == "BTCUSDT" &&
            position.Size == 2m &&
            position.TrackingState == PositionTrackingState.Active);
        savedStates[0].Positions.Should().Contain(position =>
            position.ExchangePositionKey.InstrumentId.Value == "ETHUSDT" &&
            position.TrackingState == PositionTrackingState.Unknown);
        savedStates[0].IsFresh.Should().BeFalse();
        fixture.Account.LastError.Should().Be("positions_partial");
    }

    [Fact]
    public async Task SynchronizeAsync_Persists_Last_Known_Capital_And_Unknown_Positions_When_Both_Observations_Fail()
    {
        var fixture = CreateFixture();
        var previousSyncAt = ObservedAt.AddMinutes(-2);
        fixture.Account.RecordSuccessfulSync(previousSyncAt);
        var trackedPosition = CreateTrackedPosition(fixture, "BTCUSDT");
        var previousState = PortfolioState.Create(
            fixture.Account.Id,
            [trackedPosition],
            new PortfolioCapitalState(1_000m, 800m, ObservedAt.AddMinutes(-1), 900m),
            ObservedAt,
            TimeSpan.FromMinutes(5));
        fixture.Provider
            .Setup(provider => provider.GetWalletBalanceAsync(
                AccountType.Unified,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccountBalanceObservation.Failed(
                new ExchangeFailure(ExchangeFailureKind.Unavailable, Retryable: true),
                ObservedAt));
        fixture.Provider
            .Setup(provider => provider.GetOpenPositionsAsync(
                MarketCategory.Linear,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OpenPositionsObservation.Failed(
                MarketCategory.Linear,
                null,
                ObservedAt,
                "provider-secret-like-message"));
        SetupPositionLoad(fixture, trackedPosition);
        SetupPositionSave(fixture);
        fixture.PortfolioRepository
            .Setup(repository => repository.GetLatestAsync(
                fixture.UserId,
                fixture.Account.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(previousState);
        var savedStates = new List<PortfolioState>();
        SetupPortfolioSave(fixture, savedStates);
        SetupAccountSave(fixture, "balance_failed", previousSyncAt);

        var result = await fixture.Service.SynchronizeAsync(fixture.UserId, fixture.Account.Id);

        result.Outcome.Should().Be(ExchangeAccountSyncOutcome.ExchangeUnavailable);
        trackedPosition.TrackingState.Should().Be(PositionTrackingState.Unknown);
        savedStates.Should().ContainSingle();
        savedStates[0].Capital.Should().Be(previousState.Capital);
        savedStates[0].Capital.ObservedAt.Should().Be(previousState.Capital.ObservedAt);
        savedStates[0].Positions.Should().ContainSingle()
            .Which.TrackingState.Should().Be(PositionTrackingState.Unknown);
        fixture.Account.ConnectionStatus.Should().Be(ExchangeAccountConnectionStatus.Unavailable);
        fixture.Account.LastSyncedAt.Should().Be(previousSyncAt);
        fixture.Account.LastError.Should().Be("balance_failed");
        fixture.Account.LastError.Should().NotContain("provider-secret-like-message");
    }

    [Fact]
    public async Task SynchronizeAsync_Preserves_Old_Capital_Observation_Time_And_Marks_It_Stale()
    {
        var fixture = CreateFixture();
        var oldObservedAt = ObservedAt.AddMinutes(-10);
        var previousState = PortfolioState.Create(
            fixture.Account.Id,
            [],
            new PortfolioCapitalState(1_000m, 800m, oldObservedAt, 900m),
            oldObservedAt.AddMinutes(1),
            TimeSpan.FromMinutes(5));
        fixture.Provider
            .Setup(provider => provider.GetWalletBalanceAsync(
                AccountType.Unified,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccountBalanceObservation.Failed(
                new ExchangeFailure(ExchangeFailureKind.Timeout, Retryable: true),
                ObservedAt));
        fixture.Provider
            .Setup(provider => provider.GetOpenPositionsAsync(
                MarketCategory.Linear,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OpenPositionsObservation.Failed(
                MarketCategory.Linear,
                null,
                ObservedAt,
                "temporary provider failure"));
        SetupPositionLoad(fixture);
        fixture.PortfolioRepository
            .Setup(repository => repository.GetLatestAsync(
                fixture.UserId,
                fixture.Account.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(previousState);
        var savedStates = new List<PortfolioState>();
        SetupPortfolioSave(fixture, savedStates);
        SetupAccountSave(fixture, "balance_failed", null);

        await fixture.Service.SynchronizeAsync(fixture.UserId, fixture.Account.Id);

        savedStates.Should().ContainSingle();
        savedStates[0].Capital.ObservedAt.Should().Be(oldObservedAt);
        savedStates[0].Capital.TotalEquity.Should().Be(1_000m);
        savedStates[0].IsFresh.Should().BeFalse();
    }

    [Fact]
    public async Task SynchronizeAsync_Recovers_Account_And_Position_After_A_Temporary_Failure()
    {
        var fixture = CreateFixture();
        var previousSyncAt = ObservedAt.AddMinutes(-2);
        fixture.Account.RecordSuccessfulSync(previousSyncAt);
        var trackedPosition = CreateTrackedPosition(fixture, "BTCUSDT");
        var previousState = PortfolioState.Create(
            fixture.Account.Id,
            [trackedPosition],
            new PortfolioCapitalState(1_000m, 800m, ObservedAt, 900m),
            ObservedAt,
            TimeSpan.FromMinutes(5));
        fixture.Provider
            .SetupSequence(provider => provider.GetWalletBalanceAsync(
                AccountType.Unified,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccountBalanceObservation.Failed(
                new ExchangeFailure(ExchangeFailureKind.Unavailable, Retryable: true),
                ObservedAt))
            .ReturnsAsync(AccountBalanceObservation.Complete(
                new AccountBalance(AccountType.Unified, 1_100m, 1_000m, 900m, 50m, []),
                ObservedAt.AddMinutes(1)));
        fixture.Provider
            .SetupSequence(provider => provider.GetOpenPositionsAsync(
                MarketCategory.Linear,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OpenPositionsObservation.Failed(
                MarketCategory.Linear,
                null,
                ObservedAt,
                "temporary provider failure"))
            .ReturnsAsync(OpenPositionsObservation.Complete(
                MarketCategory.Linear,
                null,
                ObservedAt.AddMinutes(1),
                [CreateOpenPosition()]));
        SetupPositionLoad(fixture, trackedPosition);
        SetupPositionSave(fixture);
        fixture.PortfolioRepository
            .Setup(repository => repository.GetLatestAsync(
                fixture.UserId,
                fixture.Account.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(previousState);
        var savedStates = new List<PortfolioState>();
        SetupPortfolioSave(fixture, savedStates);
        fixture.AccountRepository
            .Setup(repository => repository.SaveAsync(
                fixture.UserId,
                It.IsAny<ExchangeAccount>(),
                fixture.AccountVersion,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConcurrencyVersion(2));

        var first = await fixture.Service.SynchronizeAsync(fixture.UserId, fixture.Account.Id);
        var firstSyncedAt = fixture.Account.LastSyncedAt;

        var second = await fixture.Service.SynchronizeAsync(fixture.UserId, fixture.Account.Id);

        first.Outcome.Should().Be(ExchangeAccountSyncOutcome.ExchangeUnavailable);
        firstSyncedAt.Should().Be(previousSyncAt);
        second.Outcome.Should().Be(ExchangeAccountSyncOutcome.Synchronized);
        fixture.Account.ConnectionStatus.Should().Be(ExchangeAccountConnectionStatus.Connected);
        fixture.Account.LastError.Should().BeNull();
        fixture.Account.LastSyncedAt.Should().BeAfter(previousSyncAt);
        trackedPosition.TrackingState.Should().Be(PositionTrackingState.Active);
        trackedPosition.Changes.Should().Contain(change => change.Kind == PositionChangeKind.Recovered);
        savedStates.Should().HaveCount(2);
    }

    [Fact]
    public async Task SynchronizeAsync_Propagates_Cancellation_Without_Mutating_Account_Or_Writing_State()
    {
        var fixture = CreateFixture();
        using var cancellation = new CancellationTokenSource();
        fixture.Provider
            .Setup(provider => provider.GetWalletBalanceAsync(
                AccountType.Unified,
                cancellation.Token))
            .ThrowsAsync(new OperationCanceledException(cancellation.Token));

        await FluentActions
            .Invoking(() => fixture.Service.SynchronizeAsync(
                fixture.UserId,
                fixture.Account.Id,
                cancellation.Token))
            .Should()
            .ThrowAsync<OperationCanceledException>();

        fixture.Account.ConnectionStatus.Should().Be(ExchangeAccountConnectionStatus.Connected);
        fixture.Account.LastSyncedAt.Should().BeNull();
        fixture.Account.LastError.Should().BeNull();
        fixture.Provider.Verify(
            provider => provider.GetOpenPositionsAsync(
                It.IsAny<MarketCategory>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        fixture.PositionRepository.Verify(
            repository => repository.GetByExchangeAccountAsync(
                It.IsAny<UserId>(),
                It.IsAny<ExchangeAccountId>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        fixture.PortfolioRepository.Verify(
            repository => repository.SaveAsync(
                It.IsAny<UserId>(),
                It.IsAny<PortfolioState>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        fixture.AccountRepository.Verify(
            repository => repository.SaveAsync(
                It.IsAny<UserId>(),
                It.IsAny<ExchangeAccount>(),
                It.IsAny<ConcurrencyVersion?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static readonly ExchangeAccountCapabilities RequiredCapabilities =
        ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions;

    private static Position CreateTrackedPosition(
        Fixture fixture,
        string symbol,
        MarketCategory category = MarketCategory.Linear) =>
        Position.Create(
            ExchangePositionKey.Create(
                fixture.Account.Id,
                InstrumentId.From(symbol),
                PositionSide.Long,
                0),
            category,
            1m,
            ObservedAt.AddMinutes(-1),
            ObservedAt.AddMinutes(-1),
            averageEntryPrice: 100m,
            positionValue: 100m,
            leverage: 2m,
            unrealizedPnl: 0m);

    private static void SetupSuccessfulObservation(Fixture fixture, DateTimeOffset observedAt)
    {
        fixture.Provider
            .Setup(provider => provider.GetWalletBalanceAsync(
                AccountType.Unified,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccountBalanceObservation.Complete(
                new AccountBalance(AccountType.Unified, 1_000m, 950m, 800m, 50m, []),
                observedAt));
        fixture.Provider
            .Setup(provider => provider.GetOpenPositionsAsync(
                MarketCategory.Linear,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OpenPositionsObservation.Complete(
                MarketCategory.Linear,
                null,
                observedAt,
                []));
    }

    private static void SetupPositionLoad(Fixture fixture, params Position[] positions) =>
        fixture.PositionRepository
            .Setup(repository => repository.GetByExchangeAccountAsync(
                fixture.UserId,
                fixture.Account.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                positions
                    .Select(position => new Versioned<Position>(position, ConcurrencyVersion.Initial))
                    .ToArray());

    private static void SetupPositionSave(Fixture fixture) =>
        fixture.PositionRepository
            .Setup(repository => repository.SaveAsync(
                fixture.UserId,
                It.IsAny<Position>(),
                It.IsAny<ConcurrencyVersion?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConcurrencyVersion(2));

    private static void SetupPortfolioSave(Fixture fixture, List<PortfolioState> savedStates) =>
        fixture.PortfolioRepository
            .Setup(repository => repository.SaveAsync(
                fixture.UserId,
                It.IsAny<PortfolioState>(),
                It.IsAny<CancellationToken>()))
            .Callback<UserId, PortfolioState, CancellationToken>((_, state, _) => savedStates.Add(state))
            .Returns(Task.CompletedTask);

    private static void SetupAccountSave(
        Fixture fixture,
        string expectedError,
        DateTimeOffset? expectedLastSyncedAt) =>
        fixture.AccountRepository
            .Setup(repository => repository.SaveAsync(
                fixture.UserId,
                It.Is<ExchangeAccount>(account =>
                    account.ConnectionStatus == ExchangeAccountConnectionStatus.Unavailable &&
                    account.LastError == expectedError &&
                    account.LastSyncedAt == expectedLastSyncedAt),
                fixture.AccountVersion,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConcurrencyVersion(2));

    private static OpenPosition CreateOpenPosition() =>
        new(
            "BTCUSDT",
            MarketCategory.Linear,
            PositionSide.Long,
            PositionStatus.Normal,
            1m,
            100m,
            100m,
            2m,
            100m,
            null,
            null,
            0m,
            null,
            null,
            null,
            1,
            null,
            null,
            null,
            0);

    private static Fixture CreateFixture(ExchangeAccount? account = null)
    {
        var userId = account?.UserId ?? UserId.New();
        var ownedAccount = account ?? ExchangeAccount.Create(
            ExchangeAccountId.New(),
            userId,
            ExchangeId.Bybit,
            ExchangeAccountConnectionStatus.Connected,
            RequiredCapabilities);
        var accountRepository = new Mock<IExchangeAccountRepository>(MockBehavior.Strict);
        var credentialStore = new Mock<IExchangeAccountCredentialStore>(MockBehavior.Strict);
        var factory = new Mock<IPrivateAccountProviderFactory>(MockBehavior.Strict);
        var provider = new Mock<IPrivateAccountProvider>(MockBehavior.Strict);
        var lease = new Mock<IPrivateAccountProviderLease>(MockBehavior.Strict);
        var positionRepository = new Mock<IPositionRepository>(MockBehavior.Strict);
        var portfolioRepository = new Mock<IPortfolioStateRepository>(MockBehavior.Strict);
        lease.SetupGet(value => value.Provider).Returns(provider.Object);
        lease.Setup(value => value.Dispose());
        factory
            .Setup(value => value.Create(
                ownedAccount.ExchangeId,
                It.IsAny<ExchangeAccountCredential>()))
            .Returns(lease.Object);
        credentialStore
            .Setup(value => value.GetAsync(
                userId,
                ownedAccount.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new ExchangeAccountCredential(
                    new ExchangeAccountCredentialSecret("api-key", "api-secret"),
                    ConcurrencyVersion.Initial));
        accountRepository
            .Setup(value => value.GetByIdAsync(
                userId,
                ownedAccount.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Versioned<ExchangeAccount>(ownedAccount, ConcurrencyVersion.Initial));
        var transaction = new InlineSyncTransaction();

        return new Fixture(
            userId,
            ownedAccount,
            ConcurrencyVersion.Initial,
            accountRepository,
            credentialStore,
            factory,
            provider,
            positionRepository,
            portfolioRepository,
            transaction,
            new ExchangeAccountSyncService(
                accountRepository.Object,
                credentialStore.Object,
                factory.Object,
                positionRepository.Object,
                portfolioRepository.Object,
                transaction,
                new NoOpApplicationEventOutbox(),
                new FixedTimeProvider(CalculatedAt)));
    }

    private sealed record Fixture(
        UserId UserId,
        ExchangeAccount Account,
        ConcurrencyVersion AccountVersion,
        Mock<IExchangeAccountRepository> AccountRepository,
        Mock<IExchangeAccountCredentialStore> CredentialStore,
        Mock<IPrivateAccountProviderFactory> Factory,
        Mock<IPrivateAccountProvider> Provider,
        Mock<IPositionRepository> PositionRepository,
        Mock<IPortfolioStateRepository> PortfolioRepository,
        IExchangeAccountSyncTransaction Transaction,
        ExchangeAccountSyncService Service);

    private sealed class InlineSyncTransaction : IExchangeAccountSyncTransaction
    {
        public Task ExecuteAsync(
            Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken = default) =>
            operation(cancellationToken);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class NoOpApplicationEventOutbox : IApplicationEventOutbox
    {
        public Task AddAsync(
            IApplicationEvent applicationEvent,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task AddRangeAsync(
            IReadOnlyCollection<IApplicationEvent> applicationEvents,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
