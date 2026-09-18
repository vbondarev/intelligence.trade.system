using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Accounts.Access;
using Intelligence.TradeSystem.Application.Accounts.Credentials;
using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Users;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Moq;

namespace Intelligence.TradeSystem.Application.Tests.Accounts;

public sealed class ExchangeAccountVerificationTests
{
    [Fact]
    public async Task RotateCredentialsAsync_WhenVersionsAreUnchanged_RotatesAndMarksConnected()
    {
        var userId = UserId.New();
        var account = ExchangeAccount.Create(ExchangeAccountId.New(), userId, ExchangeId.Bybit,
            connectionStatus: ExchangeAccountConnectionStatus.Unavailable,
            capabilities: ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions);
        var repository = new Mock<IExchangeAccountRepository>(MockBehavior.Strict);
        var store = new Mock<IExchangeAccountCredentialStore>(MockBehavior.Strict);
        var verifier = new Mock<IExchangeAccountAccessVerifier>(MockBehavior.Strict);
        var context = new Mock<ICurrentUserContext>(MockBehavior.Strict);
        context.SetupGet(value => value.UserId).Returns(userId);
        var version = ConcurrencyVersion.Initial;
        repository.Setup(value => value.GetByIdAsync(userId, account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Versioned<ExchangeAccount>(account, version));
        store.Setup(value => value.GetMetadataAsync(userId, account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExchangeAccountCredentialMetadata(version));
        verifier.Setup(value => value.VerifyAsync(ExchangeId.Bybit, It.IsAny<ExchangeAccountCredentialSecret>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExchangeAccountAccessVerificationResult.Verified(
                ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions));
        store.Setup(value => value.RotateAsync(userId, account.Id, version, It.IsAny<ExchangeAccountCredentialSecret>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConcurrencyVersion(2));
        repository.Setup(value => value.SaveAsync(userId,
                It.Is<ExchangeAccount>(saved => saved.ConnectionStatus == ExchangeAccountConnectionStatus.Connected),
                version, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConcurrencyVersion(2));
        var transaction = new InlineLifecycleTransaction();
        var service = new ExchangeAccountService(context.Object, verifier.Object, repository.Object, store.Object, transaction);

        var result = await service.RotateCredentialsAsync(userId, account.Id,
            new ExchangeAccountCredentialSecret("replacement-key", "replacement-secret"));

        result.Outcome.Should().Be(ExchangeAccountCredentialRotationOutcome.Succeeded);
        result.Account!.Id.Should().Be(account.Id);
        transaction.Executed.Should().BeTrue();
    }
    [Fact]
    public async Task VerifyAsync_WhenExchangeIsUnavailable_MarksAccountUnavailable()
    {
        var userId = UserId.New();
        var account = ExchangeAccount.Create(
            ExchangeAccountId.New(),
            userId,
            ExchangeId.Bybit,
            connectionStatus: ExchangeAccountConnectionStatus.Connected,
            capabilities: ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions);
        var accountVersion = ConcurrencyVersion.Initial;
        var repository = new Mock<IExchangeAccountRepository>(MockBehavior.Strict);
        var store = new Mock<IExchangeAccountCredentialStore>(MockBehavior.Strict);
        var verifier = new Mock<IExchangeAccountAccessVerifier>(MockBehavior.Strict);
        var context = new Mock<ICurrentUserContext>(MockBehavior.Strict);
        context.SetupGet(value => value.UserId).Returns(userId);
        repository.Setup(value => value.GetByIdAsync(userId, account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Versioned<ExchangeAccount>(account, accountVersion));
        var credential = new ExchangeAccountCredential(
            new ExchangeAccountCredentialSecret("key", "secret"),
            ConcurrencyVersion.Initial);
        store.Setup(value => value.GetAsync(userId, account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(credential);
        verifier.Setup(value => value.VerifyAsync(ExchangeId.Bybit, It.IsAny<ExchangeAccountCredentialSecret>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExchangeAccountAccessVerificationResult.Failed(ExchangeAccountAccessVerificationStatus.Unavailable));
        repository.Setup(value => value.SaveAsync(userId,
                It.Is<ExchangeAccount>(saved => saved.ConnectionStatus == ExchangeAccountConnectionStatus.Unavailable),
                accountVersion,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConcurrencyVersion(2));

        var service = new ExchangeAccountService(context.Object, verifier.Object, repository.Object, store.Object);

        var result = await service.VerifyAsync(userId, account.Id);

        result.Outcome.Should().Be(ExchangeAccountVerificationOutcome.ExchangeUnavailable);
        result.Account.Should().BeSameAs(account);
        repository.VerifyAll();
        store.VerifyAll();
        verifier.VerifyAll();
    }

    private sealed class InlineLifecycleTransaction : IExchangeAccountLifecycleTransaction
    {
        public bool Executed { get; private set; }
        public async Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
        {
            Executed = true;
            await operation(cancellationToken);
        }
    }
}
