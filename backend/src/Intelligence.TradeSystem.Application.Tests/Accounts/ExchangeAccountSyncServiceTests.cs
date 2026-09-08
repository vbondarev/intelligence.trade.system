using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Accounts.Credentials;
using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Application.Users;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Portfolio;
using Intelligence.TradeSystem.Domain.Snapshots;
using Moq;

namespace Intelligence.TradeSystem.Application.Tests.Accounts;

public sealed class ExchangeAccountSyncServiceTests
{
    private static readonly DateTimeOffset ObservedAt =
        new(2026, 9, 8, 15, 0, 0, TimeSpan.Zero);

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

        var result = await fixture.Service.SynchronizeAsync(fixture.Account.Id);

        result.Outcome.Should().Be(ExchangeAccountSyncOutcome.Synchronized);
        result.Account.Should().BeSameAs(fixture.Account);
        result.PortfolioState.Should().NotBeNull();
        result.PortfolioState!.Capital.TotalEquity.Should().Be(1_000m);
        result.PortfolioState.Positions.Should().ContainSingle();
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

        var result = await fixture.Service.SynchronizeAsync(fixture.Account.Id);

        result.Outcome.Should().Be(ExchangeAccountSyncOutcome.Synchronized);
        trackedPosition.TrackingState.Should().Be(PositionTrackingState.Closed);
        result.PortfolioState!.Positions.Should().BeEmpty();
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

        var result = await fixture.Service.SynchronizeAsync(fixture.Account.Id);

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
        fixture.AccountRepository
            .Setup(repository => repository.GetByIdAsync(
                fixture.UserId,
                fixture.Account.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Versioned<ExchangeAccount>?)null);

        var result = await fixture.Service.SynchronizeAsync(fixture.Account.Id);

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

        var result = await fixture.Service.SynchronizeAsync(fixture.Account.Id);

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

        var result = await fixture.Service.SynchronizeAsync(fixture.Account.Id);

        result.Outcome.Should().Be(ExchangeAccountSyncOutcome.CredentialsUnavailable);
        fixture.Factory.Verify(
            factory => factory.Create(
                It.IsAny<ExchangeId>(),
                It.IsAny<ExchangeAccountCredential>()),
            Times.Never);
    }

    [Fact]
    public async Task SynchronizeAsync_Does_Not_Persist_When_Balance_Observation_Fails()
    {
        var fixture = CreateFixture();
        fixture.Provider
            .Setup(provider => provider.GetWalletBalanceAsync(
                AccountType.Unified,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccountBalanceObservation.Failed(
                new ExchangeFailure(ExchangeFailureKind.Unavailable, Retryable: true),
                ObservedAt));

        var result = await fixture.Service.SynchronizeAsync(fixture.Account.Id);

        result.Outcome.Should().Be(ExchangeAccountSyncOutcome.ExchangeUnavailable);
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
        fixture.AccountRepository.Verify(
            repository => repository.SaveAsync(
                It.IsAny<UserId>(),
                It.IsAny<ExchangeAccount>(),
                It.IsAny<ConcurrencyVersion?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SynchronizeAsync_Does_Not_Treat_Failed_Positions_As_Empty()
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

        var result = await fixture.Service.SynchronizeAsync(fixture.Account.Id);

        result.Outcome.Should().Be(ExchangeAccountSyncOutcome.ExchangeUnavailable);
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
    }

    [Fact]
    public async Task SynchronizeAsync_Does_Not_Persist_Partial_Positions()
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
            .ReturnsAsync(OpenPositionsObservation.Partial(
                MarketCategory.Linear,
                null,
                ObservedAt,
                []));

        var result = await fixture.Service.SynchronizeAsync(fixture.Account.Id);

        result.Outcome.Should().Be(ExchangeAccountSyncOutcome.ExchangeUnavailable);
        fixture.PositionRepository.Verify(
            repository => repository.GetByExchangeAccountAsync(
                It.IsAny<UserId>(),
                It.IsAny<ExchangeAccountId>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static readonly ExchangeAccountCapabilities RequiredCapabilities =
        ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions;

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
        var currentUser = new Mock<ICurrentUserContext>(MockBehavior.Strict);
        var userId = account?.UserId ?? UserId.New();
        currentUser.SetupGet(context => context.UserId).Returns(userId);
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
                currentUser.Object,
                accountRepository.Object,
                credentialStore.Object,
                factory.Object,
                positionRepository.Object,
                portfolioRepository.Object,
                transaction));
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
}
