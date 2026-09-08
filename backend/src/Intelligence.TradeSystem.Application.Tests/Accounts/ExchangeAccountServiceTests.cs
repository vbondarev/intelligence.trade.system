using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Accounts.Access;
using Intelligence.TradeSystem.Application.Accounts.Credentials;
using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Users;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Moq;

namespace Intelligence.TradeSystem.Application.Tests.Accounts;

public sealed class ExchangeAccountServiceTests
{
    private static readonly ExchangeAccountCapabilities RequiredCapabilities =
        ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions;

    [Fact]
    public async Task ConnectAsync_Stores_Credentials_And_Connected_Account_After_Verification()
    {
        var fixture = CreateFixture();
        var secret = new ExchangeAccountCredentialSecret("api-key", "api-secret");
        fixture.Verifier
            .Setup(verifier => verifier.VerifyAsync(ExchangeId.Bybit, secret, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExchangeAccountAccessVerificationResult.Verified(RequiredCapabilities));
        fixture.Repository
            .Setup(repository => repository.SaveAsync(
                fixture.UserId,
                It.Is<ExchangeAccount>(account =>
                    account.ConnectionStatus == ExchangeAccountConnectionStatus.Unknown),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConcurrencyVersion.Initial);
        fixture.CredentialStore
            .Setup(store => store.CreateAsync(
                fixture.UserId,
                It.IsAny<ExchangeAccountId>(),
                secret,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConcurrencyVersion.Initial);
        fixture.Repository
            .Setup(repository => repository.SaveAsync(
                fixture.UserId,
                It.Is<ExchangeAccount>(account =>
                    account.ConnectionStatus == ExchangeAccountConnectionStatus.Connected &&
                    account.Capabilities == RequiredCapabilities),
                It.Is<ConcurrencyVersion>(version => version == ConcurrencyVersion.Initial),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConcurrencyVersion(2));

        var result = await fixture.Service.ConnectAsync(ExchangeId.Bybit, secret);

        result.Outcome.Should().Be(ExchangeAccountConnectionOutcome.Connected);
        result.Account.Should().NotBeNull();
        result.Account!.ConnectionStatus.Should().Be(ExchangeAccountConnectionStatus.Connected);
        result.Account.Capabilities.Should().Be(RequiredCapabilities);
        fixture.CredentialStore.VerifyAll();
        fixture.Repository.VerifyAll();
    }

    [Theory]
    [InlineData(ExchangeAccountAccessVerificationStatus.InvalidCredentials)]
    [InlineData(ExchangeAccountAccessVerificationStatus.PermissionsRejected)]
    [InlineData(ExchangeAccountAccessVerificationStatus.Unavailable)]
    public async Task ConnectAsync_Does_Not_Persist_When_Verification_Fails(
        ExchangeAccountAccessVerificationStatus verificationStatus)
    {
        var fixture = CreateFixture();
        fixture.Verifier
            .Setup(verifier => verifier.VerifyAsync(
                ExchangeId.Bybit,
                It.IsAny<ExchangeAccountCredentialSecret>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExchangeAccountAccessVerificationResult.Failed(verificationStatus));

        var result = await fixture.Service.ConnectAsync(
            ExchangeId.Bybit,
            new ExchangeAccountCredentialSecret("api-key", "api-secret"));

        result.Outcome.Should().Be(verificationStatus switch
        {
            ExchangeAccountAccessVerificationStatus.InvalidCredentials =>
                ExchangeAccountConnectionOutcome.InvalidCredentials,
            ExchangeAccountAccessVerificationStatus.PermissionsRejected =>
                ExchangeAccountConnectionOutcome.PermissionsRejected,
            _ => ExchangeAccountConnectionOutcome.Unavailable,
        });
        result.Account.Should().BeNull();
        fixture.Repository.Verify(
            repository => repository.SaveAsync(
                It.IsAny<UserId>(),
                It.IsAny<ExchangeAccount>(),
                It.IsAny<ConcurrencyVersion?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        fixture.CredentialStore.Verify(
            store => store.CreateAsync(
                It.IsAny<UserId>(),
                It.IsAny<ExchangeAccountId>(),
                It.IsAny<ExchangeAccountCredentialSecret>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ConnectAsync_Compensates_Stored_Credentials_When_Final_Account_Save_Fails()
    {
        var fixture = CreateFixture();
        var secret = new ExchangeAccountCredentialSecret("api-key", "api-secret");
        var persistedSecret = new ExchangeAccountCredentialSecret("api-key", "api-secret");
        fixture.Verifier
            .Setup(verifier => verifier.VerifyAsync(ExchangeId.Bybit, secret, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExchangeAccountAccessVerificationResult.Verified(RequiredCapabilities));
        fixture.Repository
            .Setup(repository => repository.SaveAsync(
                fixture.UserId,
                It.IsAny<ExchangeAccount>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConcurrencyVersion.Initial);
        fixture.CredentialStore
            .Setup(store => store.CreateAsync(
                fixture.UserId,
                It.IsAny<ExchangeAccountId>(),
                secret,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConcurrencyVersion.Initial);
        fixture.Repository
            .Setup(repository => repository.SaveAsync(
                fixture.UserId,
                It.Is<ExchangeAccount>(account =>
                    account.ConnectionStatus == ExchangeAccountConnectionStatus.Connected),
                ConcurrencyVersion.Initial,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("account persistence failed"));
        fixture.CredentialStore
            .Setup(store => store.GetAsync(
                fixture.UserId,
                It.IsAny<ExchangeAccountId>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExchangeAccountCredential(persistedSecret, ConcurrencyVersion.Initial));
        fixture.CredentialStore
            .Setup(store => store.RevokeAsync(
                fixture.UserId,
                It.IsAny<ExchangeAccountId>(),
                ConcurrencyVersion.Initial,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        fixture.Repository
            .Setup(repository => repository.DeleteAsync(
                fixture.UserId,
                It.IsAny<ExchangeAccountId>(),
                ConcurrencyVersion.Initial,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var act = () => fixture.Service.ConnectAsync(ExchangeId.Bybit, secret);

        await act.Should().ThrowAsync<InvalidOperationException>();
        fixture.CredentialStore.Verify(
            store => store.RevokeAsync(
                fixture.UserId,
                It.IsAny<ExchangeAccountId>(),
                ConcurrencyVersion.Initial,
                It.IsAny<CancellationToken>()),
            Times.Once);
        fixture.Repository.Verify(
            repository => repository.DeleteAsync(
                fixture.UserId,
                It.IsAny<ExchangeAccountId>(),
                ConcurrencyVersion.Initial,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DisconnectAsync_Revokes_Metadata_Without_Decrypting_And_Disables_Owned_Account()
    {
        var fixture = CreateFixture();
        var account = CreateAccount(fixture.UserId, ExchangeAccountConnectionStatus.Connected);
        var loaded = new Versioned<ExchangeAccount>(account, ConcurrencyVersion.Initial);
        fixture.Repository
            .Setup(repository => repository.GetByIdAsync(
                fixture.UserId,
                account.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(loaded);
        fixture.CredentialStore
            .Setup(store => store.GetMetadataAsync(
                fixture.UserId,
                account.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExchangeAccountCredentialMetadata(ConcurrencyVersion.Initial));
        fixture.CredentialStore
            .Setup(store => store.RevokeAsync(
                fixture.UserId,
                account.Id,
                ConcurrencyVersion.Initial,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        fixture.Repository
            .Setup(repository => repository.SaveAsync(
                fixture.UserId,
                It.Is<ExchangeAccount>(value =>
                    value.ConnectionStatus == ExchangeAccountConnectionStatus.Disabled),
                ConcurrencyVersion.Initial,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConcurrencyVersion(2));

        var result = await fixture.Service.DisconnectAsync(account.Id);

        result.Should().NotBeNull();
        result!.ConnectionStatus.Should().Be(ExchangeAccountConnectionStatus.Disabled);
        fixture.CredentialStore.Verify(
            store => store.GetAsync(
                It.IsAny<UserId>(),
                It.IsAny<ExchangeAccountId>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        fixture.CredentialStore.VerifyAll();
        fixture.Repository.VerifyAll();
    }

    [Fact]
    public async Task DisconnectAsync_Leaves_Account_Disabled_When_Revoke_Fails()
    {
        var fixture = CreateFixture();
        var account = CreateAccount(fixture.UserId, ExchangeAccountConnectionStatus.Connected);
        fixture.Repository
            .Setup(repository => repository.GetByIdAsync(
                fixture.UserId,
                account.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Versioned<ExchangeAccount>(account, ConcurrencyVersion.Initial));
        fixture.CredentialStore
            .Setup(store => store.GetMetadataAsync(
                fixture.UserId,
                account.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExchangeAccountCredentialMetadata(ConcurrencyVersion.Initial));
        fixture.Repository
            .Setup(repository => repository.SaveAsync(
                fixture.UserId,
                It.Is<ExchangeAccount>(value =>
                    value.ConnectionStatus == ExchangeAccountConnectionStatus.Disabled),
                ConcurrencyVersion.Initial,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConcurrencyVersion(2));
        fixture.CredentialStore
            .Setup(store => store.RevokeAsync(
                fixture.UserId,
                account.Id,
                ConcurrencyVersion.Initial,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("credential revoke failed"));

        var act = () => fixture.Service.DisconnectAsync(account.Id);

        await act.Should().ThrowAsync<InvalidOperationException>();
        account.ConnectionStatus.Should().Be(ExchangeAccountConnectionStatus.Disabled);
        fixture.CredentialStore.Verify(
            store => store.GetAsync(
                It.IsAny<UserId>(),
                It.IsAny<ExchangeAccountId>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DisconnectAsync_Hides_Foreign_Account_As_Missing()
    {
        var fixture = CreateFixture();
        fixture.Repository
            .Setup(repository => repository.GetByIdAsync(
                fixture.UserId,
                It.IsAny<ExchangeAccountId>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Versioned<ExchangeAccount>?)null);

        var result = await fixture.Service.DisconnectAsync(ExchangeAccountId.New());

        result.Should().BeNull();
        fixture.CredentialStore.Verify(
            store => store.GetMetadataAsync(
                It.IsAny<UserId>(),
                It.IsAny<ExchangeAccountId>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DisconnectAsync_Is_Idempotent_For_Already_Disabled_Account()
    {
        var fixture = CreateFixture();
        var account = CreateAccount(fixture.UserId, ExchangeAccountConnectionStatus.Disabled);
        fixture.Repository
            .Setup(repository => repository.GetByIdAsync(
                fixture.UserId,
                account.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Versioned<ExchangeAccount>(account, ConcurrencyVersion.Initial));
        fixture.CredentialStore
            .Setup(store => store.GetMetadataAsync(
                fixture.UserId,
                account.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExchangeAccountCredentialMetadata?)null);

        var result = await fixture.Service.DisconnectAsync(account.Id);

        result.Should().BeSameAs(account);
        fixture.Repository.Verify(
            repository => repository.SaveAsync(
                It.IsAny<UserId>(),
                It.IsAny<ExchangeAccount>(),
                It.IsAny<ConcurrencyVersion?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DisconnectAsync_Retries_Revoke_For_Disabled_Account_With_Leftover_Credentials()
    {
        var fixture = CreateFixture();
        var account = CreateAccount(fixture.UserId, ExchangeAccountConnectionStatus.Disabled);
        fixture.Repository
            .Setup(repository => repository.GetByIdAsync(
                fixture.UserId,
                account.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Versioned<ExchangeAccount>(account, ConcurrencyVersion.Initial));
        fixture.CredentialStore
            .Setup(store => store.GetMetadataAsync(
                fixture.UserId,
                account.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExchangeAccountCredentialMetadata(new ConcurrencyVersion(2)));
        fixture.CredentialStore
            .Setup(store => store.RevokeAsync(
                fixture.UserId,
                account.Id,
                new ConcurrencyVersion(2),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await fixture.Service.DisconnectAsync(account.Id);

        result.Should().BeSameAs(account);
        fixture.Repository.Verify(
            repository => repository.SaveAsync(
                It.IsAny<UserId>(),
                It.IsAny<ExchangeAccount>(),
                It.IsAny<ConcurrencyVersion?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        fixture.CredentialStore.VerifyAll();
    }

    private static ExchangeAccount CreateAccount(
        UserId userId,
        ExchangeAccountConnectionStatus status) =>
        ExchangeAccount.Create(
            ExchangeAccountId.New(),
            userId,
            ExchangeId.Bybit,
            status,
            RequiredCapabilities);

    private static Fixture CreateFixture()
    {
        var userId = UserId.New();
        var currentUser = new Mock<ICurrentUserContext>(MockBehavior.Strict);
        currentUser.SetupGet(context => context.UserId).Returns(userId);

        return new Fixture(
            userId,
            currentUser,
            new Mock<IExchangeAccountAccessVerifier>(MockBehavior.Strict),
            new Mock<IExchangeAccountRepository>(MockBehavior.Strict),
            new Mock<IExchangeAccountCredentialStore>(MockBehavior.Strict));
    }

    private sealed class Fixture(
        UserId userId,
        Mock<ICurrentUserContext> currentUser,
        Mock<IExchangeAccountAccessVerifier> verifier,
        Mock<IExchangeAccountRepository> repository,
        Mock<IExchangeAccountCredentialStore> credentialStore)
    {
        public UserId UserId { get; } = userId;
        public Mock<IExchangeAccountAccessVerifier> Verifier { get; } = verifier;
        public Mock<IExchangeAccountRepository> Repository { get; } = repository;
        public Mock<IExchangeAccountCredentialStore> CredentialStore { get; } = credentialStore;
        public ExchangeAccountService Service { get; } =
            new(currentUser.Object, verifier.Object, repository.Object, credentialStore.Object);
    }
}
