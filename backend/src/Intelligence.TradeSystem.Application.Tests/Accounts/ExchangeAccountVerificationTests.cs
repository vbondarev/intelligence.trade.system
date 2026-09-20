using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Accounts.Access;
using Intelligence.TradeSystem.Application.Accounts.Credentials;
using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Moq;

namespace Intelligence.TradeSystem.Application.Tests.Accounts;

public sealed class ExchangeAccountVerificationTests
{
    private static readonly ExchangeAccountCapabilities RequiredCapabilities =
        ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions;

    [Fact]
    public async Task VerifyAsync_Returns_NotFound_For_A_Missing_Account_Without_Touching_Credentials_Or_Verifier()
    {
        var userId = UserId.New();
        var accountId = ExchangeAccountId.New();
        var repository = new Mock<IExchangeAccountRepository>(MockBehavior.Strict);
        var store = new Mock<IExchangeAccountCredentialStore>(MockBehavior.Strict);
        var verifier = new Mock<IExchangeAccountAccessVerifier>(MockBehavior.Strict);
        repository.Setup(value => value.GetByIdAsync(userId, accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Versioned<ExchangeAccount>?)null);
        var service = new ExchangeAccountService(verifier.Object, repository.Object, store.Object, new InlineLifecycleTransaction());

        var result = await service.VerifyAsync(userId, accountId);

        result.Outcome.Should().Be(ExchangeAccountVerificationOutcome.NotFound);
        result.Account.Should().BeNull();
        repository.VerifyAll();
    }

    [Fact]
    public async Task VerifyAsync_Returns_NotFound_For_A_Foreign_Account_Identically_To_Missing()
    {
        var owner = UserId.New();
        var otherUser = UserId.New();
        var accountId = ExchangeAccountId.New();
        var repository = new Mock<IExchangeAccountRepository>(MockBehavior.Strict);
        var store = new Mock<IExchangeAccountCredentialStore>(MockBehavior.Strict);
        var verifier = new Mock<IExchangeAccountAccessVerifier>(MockBehavior.Strict);
        // The repository is user-scoped: a foreign account query returns null exactly like a
        // missing one, so the caller and Application layer cannot distinguish the two cases.
        repository.Setup(value => value.GetByIdAsync(otherUser, accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Versioned<ExchangeAccount>?)null);
        var service = new ExchangeAccountService(verifier.Object, repository.Object, store.Object, new InlineLifecycleTransaction());

        var result = await service.VerifyAsync(otherUser, accountId);

        result.Outcome.Should().Be(ExchangeAccountVerificationOutcome.NotFound);
        owner.Should().NotBe(otherUser);
    }

    [Fact]
    public async Task VerifyAsync_Returns_AccountDisabled_Without_Reading_Credentials_Or_Calling_Verifier()
    {
        var userId = UserId.New();
        var account = ExchangeAccount.Create(ExchangeAccountId.New(), userId, ExchangeId.Bybit, ProviderIdentity,
            connectionStatus: ExchangeAccountConnectionStatus.Disabled);
        var repository = new Mock<IExchangeAccountRepository>(MockBehavior.Strict);
        var store = new Mock<IExchangeAccountCredentialStore>(MockBehavior.Strict);
        var verifier = new Mock<IExchangeAccountAccessVerifier>(MockBehavior.Strict);
        repository.Setup(value => value.GetByIdAsync(userId, account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Versioned<ExchangeAccount>(account, ConcurrencyVersion.Initial));
        var service = new ExchangeAccountService(verifier.Object, repository.Object, store.Object, new InlineLifecycleTransaction());

        var result = await service.VerifyAsync(userId, account.Id);

        result.Outcome.Should().Be(ExchangeAccountVerificationOutcome.AccountDisabled);
        result.Account.Should().BeNull();
        repository.VerifyAll();
    }

    [Fact]
    public async Task VerifyAsync_Succeeds_And_Marks_The_Account_Connected()
    {
        var userId = UserId.New();
        var account = ExchangeAccount.Create(ExchangeAccountId.New(), userId, ExchangeId.Bybit, ProviderIdentity,
            connectionStatus: ExchangeAccountConnectionStatus.Unavailable, capabilities: RequiredCapabilities);
        var version = ConcurrencyVersion.Initial;
        var (repository, store, verifier) = CreateStrictMocks();
        repository.Setup(value => value.GetByIdAsync(userId, account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Versioned<ExchangeAccount>(account, version));
        var credential = new ExchangeAccountCredential(new ExchangeAccountCredentialSecret("key", "secret"), version);
        store.Setup(value => value.GetAsync(userId, account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(credential);
        verifier.Setup(value => value.VerifyAsync(ExchangeId.Bybit, It.IsAny<ExchangeAccountCredentialSecret>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExchangeAccountAccessVerificationResult.Verified(ProviderIdentity, RequiredCapabilities));
        repository.Setup(value => value.SaveAsync(userId,
                It.Is<ExchangeAccount>(saved => saved.ConnectionStatus == ExchangeAccountConnectionStatus.Connected),
                version, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConcurrencyVersion(2));
        var service = new ExchangeAccountService(verifier.Object, repository.Object, store.Object, new InlineLifecycleTransaction());

        var result = await service.VerifyAsync(userId, account.Id);

        result.Outcome.Should().Be(ExchangeAccountVerificationOutcome.Succeeded);
        result.Account!.ConnectionStatus.Should().Be(ExchangeAccountConnectionStatus.Connected);
        result.Account.ProviderIdentity.Should().Be(ProviderIdentity);
        repository.VerifyAll();
        store.VerifyAll();
        verifier.VerifyAll();
    }

    [Fact]
    public async Task VerifyAsync_WhenCredentialsAreInvalid_MarksAccountUnavailable()
    {
        var result = await RunVerifyOutcomeAsync(ExchangeAccountAccessVerificationResult.Failed(
            ExchangeAccountAccessVerificationStatus.InvalidCredentials));

        result.Outcome.Should().Be(ExchangeAccountVerificationOutcome.InvalidCredentials);
        result.Account!.ConnectionStatus.Should().Be(ExchangeAccountConnectionStatus.Unavailable);
    }

    [Fact]
    public async Task VerifyAsync_WhenProviderIdentityDiffers_MarksAccountUnavailable()
    {
        var result = await RunVerifyOutcomeAsync(ExchangeAccountAccessVerificationResult.Verified(
            OtherProviderIdentity, RequiredCapabilities));

        result.Outcome.Should().Be(ExchangeAccountVerificationOutcome.ProviderIdentityMismatch);
        result.Account!.ConnectionStatus.Should().Be(ExchangeAccountConnectionStatus.Unavailable);
    }

    [Fact]
    public async Task VerifyAsync_WhenPermissionsAreRejected_MarksAccountUnavailable()
    {
        var result = await RunVerifyOutcomeAsync(ExchangeAccountAccessVerificationResult.Failed(
            ExchangeAccountAccessVerificationStatus.PermissionsRejected));

        result.Outcome.Should().Be(ExchangeAccountVerificationOutcome.PermissionsRejected);
        result.Account!.ConnectionStatus.Should().Be(ExchangeAccountConnectionStatus.Unavailable);
    }

    [Fact]
    public async Task VerifyAsync_WhenCapabilitiesAreInsufficient_TreatsItAsPermissionsRejectedAndMarksUnavailable()
    {
        var result = await RunVerifyOutcomeAsync(ExchangeAccountAccessVerificationResult.Verified(
            ProviderIdentity, ExchangeAccountCapabilities.ReadBalance));

        result.Outcome.Should().Be(ExchangeAccountVerificationOutcome.PermissionsRejected);
        result.Account!.ConnectionStatus.Should().Be(ExchangeAccountConnectionStatus.Unavailable);
    }

    [Fact]
    public async Task VerifyAsync_WhenExchangeIsUnavailable_MarksAccountUnavailable()
    {
        var userId = UserId.New();
        var account = ExchangeAccount.Create(
            ExchangeAccountId.New(),
            userId,
            ExchangeId.Bybit,
            ProviderIdentity,
            connectionStatus: ExchangeAccountConnectionStatus.Connected,
            capabilities: RequiredCapabilities);
        var accountVersion = ConcurrencyVersion.Initial;
        var (repository, store, verifier) = CreateStrictMocks();
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

        var service = new ExchangeAccountService(
            verifier.Object,
            repository.Object,
            store.Object,
            new InlineLifecycleTransaction());

        var result = await service.VerifyAsync(userId, account.Id);

        result.Outcome.Should().Be(ExchangeAccountVerificationOutcome.ExchangeUnavailable);
        result.Account.Should().BeSameAs(account);
        repository.VerifyAll();
        store.VerifyAll();
        verifier.VerifyAll();
    }

    [Fact]
    public async Task VerifyAsync_WhenCredentialsAreUnreadable_ReturnsCredentialsUnavailableWithoutCallingVerifier()
    {
        var userId = UserId.New();
        var account = ExchangeAccount.Create(ExchangeAccountId.New(), userId, ExchangeId.Bybit, ProviderIdentity,
            connectionStatus: ExchangeAccountConnectionStatus.Connected, capabilities: RequiredCapabilities);
        var (repository, store, verifier) = CreateStrictMocks();
        repository.Setup(value => value.GetByIdAsync(userId, account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Versioned<ExchangeAccount>(account, ConcurrencyVersion.Initial));
        store.Setup(value => value.GetAsync(userId, account.Id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ExchangeAccountCredentialsUnavailableException());
        var service = new ExchangeAccountService(verifier.Object, repository.Object, store.Object, new InlineLifecycleTransaction());

        var result = await service.VerifyAsync(userId, account.Id);

        result.Outcome.Should().Be(ExchangeAccountVerificationOutcome.CredentialsUnavailable);
        result.Account.Should().BeNull();
        repository.VerifyAll();
        store.VerifyAll();
        // verifier is strict and has no setup: if it were invoked, VerifyAll/the call itself would throw.
    }

    [Fact]
    public async Task VerifyAsync_WhenNoCredentialRowExists_ReturnsCredentialsUnavailableWithoutCallingVerifier()
    {
        var userId = UserId.New();
        var account = ExchangeAccount.Create(ExchangeAccountId.New(), userId, ExchangeId.Bybit, ProviderIdentity,
            connectionStatus: ExchangeAccountConnectionStatus.Connected, capabilities: RequiredCapabilities);
        var (repository, store, verifier) = CreateStrictMocks();
        repository.Setup(value => value.GetByIdAsync(userId, account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Versioned<ExchangeAccount>(account, ConcurrencyVersion.Initial));
        store.Setup(value => value.GetAsync(userId, account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExchangeAccountCredential?)null);
        var service = new ExchangeAccountService(verifier.Object, repository.Object, store.Object, new InlineLifecycleTransaction());

        var result = await service.VerifyAsync(userId, account.Id);

        result.Outcome.Should().Be(ExchangeAccountVerificationOutcome.CredentialsUnavailable);
    }

    [Fact]
    public async Task VerifyAsync_Propagates_A_Concurrency_Conflict_From_The_Final_Save()
    {
        var userId = UserId.New();
        var account = ExchangeAccount.Create(ExchangeAccountId.New(), userId, ExchangeId.Bybit, ProviderIdentity,
            connectionStatus: ExchangeAccountConnectionStatus.Unavailable, capabilities: RequiredCapabilities);
        var version = ConcurrencyVersion.Initial;
        var (repository, store, verifier) = CreateStrictMocks();
        repository.Setup(value => value.GetByIdAsync(userId, account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Versioned<ExchangeAccount>(account, version));
        var credential = new ExchangeAccountCredential(new ExchangeAccountCredentialSecret("key", "secret"), version);
        store.Setup(value => value.GetAsync(userId, account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(credential);
        verifier.Setup(value => value.VerifyAsync(ExchangeId.Bybit, It.IsAny<ExchangeAccountCredentialSecret>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExchangeAccountAccessVerificationResult.Verified(ProviderIdentity, RequiredCapabilities));
        repository.Setup(value => value.SaveAsync(userId, It.IsAny<ExchangeAccount>(), version, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConcurrencyConflictException("stale version"));
        var service = new ExchangeAccountService(verifier.Object, repository.Object, store.Object, new InlineLifecycleTransaction());

        var act = () => service.VerifyAsync(userId, account.Id);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    [Fact]
    public async Task RotateCredentialsAsync_WhenVersionsAreUnchanged_RotatesAndMarksConnected()
    {
        var userId = UserId.New();
        var account = ExchangeAccount.Create(ExchangeAccountId.New(), userId, ExchangeId.Bybit, ProviderIdentity,
            connectionStatus: ExchangeAccountConnectionStatus.Unavailable,
            capabilities: ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions);
        var repository = new Mock<IExchangeAccountRepository>(MockBehavior.Strict);
        var store = new Mock<IExchangeAccountCredentialStore>(MockBehavior.Strict);
        var verifier = new Mock<IExchangeAccountAccessVerifier>(MockBehavior.Strict);
        var version = ConcurrencyVersion.Initial;
        repository.Setup(value => value.GetByIdAsync(userId, account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Versioned<ExchangeAccount>(account, version));
        store.Setup(value => value.GetMetadataAsync(userId, account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExchangeAccountCredentialMetadata(version));
        verifier.Setup(value => value.VerifyAsync(ExchangeId.Bybit, It.IsAny<ExchangeAccountCredentialSecret>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExchangeAccountAccessVerificationResult.Verified(
                ProviderIdentity, ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions));
        store.Setup(value => value.RotateAsync(userId, account.Id, version, It.IsAny<ExchangeAccountCredentialSecret>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConcurrencyVersion(2));
        repository.Setup(value => value.SaveAsync(userId,
                It.Is<ExchangeAccount>(saved => saved.ConnectionStatus == ExchangeAccountConnectionStatus.Connected),
                version, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConcurrencyVersion(2));
        var transaction = new InlineLifecycleTransaction();
        var service = new ExchangeAccountService(verifier.Object, repository.Object, store.Object, transaction);

        var result = await service.RotateCredentialsAsync(userId, account.Id,
            new ExchangeAccountCredentialSecret("replacement-key", "replacement-secret"));

        result.Outcome.Should().Be(ExchangeAccountCredentialRotationOutcome.Succeeded);
        result.Account!.Id.Should().Be(account.Id);
        transaction.Executed.Should().BeTrue();
        verifier.Verify(
            value => value.VerifyAsync(ExchangeId.Bybit, It.IsAny<ExchangeAccountCredentialSecret>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RotateCredentialsAsync_Reads_Versions_Before_Calling_The_Verifier()
    {
        var userId = UserId.New();
        var account = ExchangeAccount.Create(ExchangeAccountId.New(), userId, ExchangeId.Bybit, ProviderIdentity,
            connectionStatus: ExchangeAccountConnectionStatus.Connected, capabilities: RequiredCapabilities);
        var version = ConcurrencyVersion.Initial;
        var (repository, store, verifier) = CreateStrictMocks();
        var sequence = new MockSequence();
        repository.InSequence(sequence)
            .Setup(value => value.GetByIdAsync(userId, account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Versioned<ExchangeAccount>(account, version));
        store.InSequence(sequence)
            .Setup(value => value.GetMetadataAsync(userId, account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExchangeAccountCredentialMetadata(version));
        verifier.InSequence(sequence)
            .Setup(value => value.VerifyAsync(ExchangeId.Bybit, It.IsAny<ExchangeAccountCredentialSecret>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExchangeAccountAccessVerificationResult.Verified(ProviderIdentity, RequiredCapabilities));
        repository.InSequence(sequence)
            .Setup(value => value.GetByIdAsync(userId, account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Versioned<ExchangeAccount>(account, version));
        store.InSequence(sequence)
            .Setup(value => value.GetMetadataAsync(userId, account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExchangeAccountCredentialMetadata(version));
        store.Setup(value => value.RotateAsync(userId, account.Id, version, It.IsAny<ExchangeAccountCredentialSecret>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConcurrencyVersion(2));
        repository.Setup(value => value.SaveAsync(userId, It.IsAny<ExchangeAccount>(), version, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConcurrencyVersion(2));
        var service = new ExchangeAccountService(verifier.Object, repository.Object, store.Object, new InlineLifecycleTransaction());

        var result = await service.RotateCredentialsAsync(userId, account.Id,
            new ExchangeAccountCredentialSecret("replacement-key", "replacement-secret"));

        result.Outcome.Should().Be(ExchangeAccountCredentialRotationOutcome.Succeeded);
        repository.VerifyAll();
        store.VerifyAll();
        verifier.VerifyAll();
    }

    [Fact]
    public async Task RotateCredentialsAsync_Returns_NotFound_For_A_Missing_Account_Without_Calling_The_Verifier()
    {
        var userId = UserId.New();
        var accountId = ExchangeAccountId.New();
        var (repository, store, verifier) = CreateStrictMocks();
        repository.Setup(value => value.GetByIdAsync(userId, accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Versioned<ExchangeAccount>?)null);
        var service = new ExchangeAccountService(verifier.Object, repository.Object, store.Object, new InlineLifecycleTransaction());

        var result = await service.RotateCredentialsAsync(userId, accountId,
            new ExchangeAccountCredentialSecret("replacement-key", "replacement-secret"));

        result.Outcome.Should().Be(ExchangeAccountCredentialRotationOutcome.NotFound);
        result.Account.Should().BeNull();
    }

    [Fact]
    public async Task RotateCredentialsAsync_Returns_NotFound_For_A_Foreign_Account_Identically_To_Missing()
    {
        var otherUser = UserId.New();
        var accountId = ExchangeAccountId.New();
        var (repository, store, verifier) = CreateStrictMocks();
        repository.Setup(value => value.GetByIdAsync(otherUser, accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Versioned<ExchangeAccount>?)null);
        var service = new ExchangeAccountService(verifier.Object, repository.Object, store.Object, new InlineLifecycleTransaction());

        var result = await service.RotateCredentialsAsync(otherUser, accountId,
            new ExchangeAccountCredentialSecret("replacement-key", "replacement-secret"));

        result.Outcome.Should().Be(ExchangeAccountCredentialRotationOutcome.NotFound);
    }

    [Fact]
    public async Task RotateCredentialsAsync_Returns_AccountDisabled_Without_Calling_The_Verifier()
    {
        var userId = UserId.New();
        var account = ExchangeAccount.Create(ExchangeAccountId.New(), userId, ExchangeId.Bybit, ProviderIdentity,
            connectionStatus: ExchangeAccountConnectionStatus.Disabled);
        var (repository, store, verifier) = CreateStrictMocks();
        repository.Setup(value => value.GetByIdAsync(userId, account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Versioned<ExchangeAccount>(account, ConcurrencyVersion.Initial));
        var service = new ExchangeAccountService(verifier.Object, repository.Object, store.Object, new InlineLifecycleTransaction());

        var result = await service.RotateCredentialsAsync(userId, account.Id,
            new ExchangeAccountCredentialSecret("replacement-key", "replacement-secret"));

        result.Outcome.Should().Be(ExchangeAccountCredentialRotationOutcome.AccountDisabled);
    }

    [Fact]
    public async Task RotateCredentialsAsync_Returns_CredentialsUnavailable_When_No_Credential_Row_Exists()
    {
        var userId = UserId.New();
        var account = ExchangeAccount.Create(ExchangeAccountId.New(), userId, ExchangeId.Bybit, ProviderIdentity,
            connectionStatus: ExchangeAccountConnectionStatus.Connected, capabilities: RequiredCapabilities);
        var (repository, store, verifier) = CreateStrictMocks();
        repository.Setup(value => value.GetByIdAsync(userId, account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Versioned<ExchangeAccount>(account, ConcurrencyVersion.Initial));
        store.Setup(value => value.GetMetadataAsync(userId, account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExchangeAccountCredentialMetadata?)null);
        var service = new ExchangeAccountService(verifier.Object, repository.Object, store.Object, new InlineLifecycleTransaction());

        var result = await service.RotateCredentialsAsync(userId, account.Id,
            new ExchangeAccountCredentialSecret("replacement-key", "replacement-secret"));

        result.Outcome.Should().Be(ExchangeAccountCredentialRotationOutcome.CredentialsUnavailable);
    }

    [Theory]
    [InlineData(ExchangeAccountAccessVerificationStatus.InvalidCredentials, ExchangeAccountCredentialRotationOutcome.InvalidCredentials)]
    [InlineData(ExchangeAccountAccessVerificationStatus.PermissionsRejected, ExchangeAccountCredentialRotationOutcome.PermissionsRejected)]
    [InlineData(ExchangeAccountAccessVerificationStatus.Unavailable, ExchangeAccountCredentialRotationOutcome.ExchangeUnavailable)]
    public async Task RotateCredentialsAsync_WhenVerificationFails_ReturnsTheMappedOutcomeAndWritesNothing(
        ExchangeAccountAccessVerificationStatus status,
        ExchangeAccountCredentialRotationOutcome expectedOutcome)
    {
        var userId = UserId.New();
        var account = ExchangeAccount.Create(ExchangeAccountId.New(), userId, ExchangeId.Bybit, ProviderIdentity,
            connectionStatus: ExchangeAccountConnectionStatus.Connected, capabilities: RequiredCapabilities);
        var version = ConcurrencyVersion.Initial;
        var (repository, store, verifier) = CreateStrictMocks();
        repository.Setup(value => value.GetByIdAsync(userId, account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Versioned<ExchangeAccount>(account, version));
        store.Setup(value => value.GetMetadataAsync(userId, account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExchangeAccountCredentialMetadata(version));
        verifier.Setup(value => value.VerifyAsync(ExchangeId.Bybit, It.IsAny<ExchangeAccountCredentialSecret>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExchangeAccountAccessVerificationResult.Failed(status));
        var service = new ExchangeAccountService(verifier.Object, repository.Object, store.Object, new InlineLifecycleTransaction());

        var result = await service.RotateCredentialsAsync(userId, account.Id,
            new ExchangeAccountCredentialSecret("replacement-key", "replacement-secret"));

        result.Outcome.Should().Be(expectedOutcome);
        result.Account.Should().BeNull();
        // repository/store have no RotateAsync/SaveAsync setup: a strict mock throws if either is invoked.
    }

    [Fact]
    public async Task RotateCredentialsAsync_WhenReplacementHasInsufficientCapabilities_ReturnsPermissionsRejectedAndWritesNothing()
    {
        var userId = UserId.New();
        var account = ExchangeAccount.Create(ExchangeAccountId.New(), userId, ExchangeId.Bybit, ProviderIdentity,
            connectionStatus: ExchangeAccountConnectionStatus.Connected, capabilities: RequiredCapabilities);
        var version = ConcurrencyVersion.Initial;
        var (repository, store, verifier) = CreateStrictMocks();
        repository.Setup(value => value.GetByIdAsync(userId, account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Versioned<ExchangeAccount>(account, version));
        store.Setup(value => value.GetMetadataAsync(userId, account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExchangeAccountCredentialMetadata(version));
        verifier.Setup(value => value.VerifyAsync(ExchangeId.Bybit, It.IsAny<ExchangeAccountCredentialSecret>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExchangeAccountAccessVerificationResult.Verified(
                ProviderIdentity, ExchangeAccountCapabilities.ReadBalance));
        var service = new ExchangeAccountService(verifier.Object, repository.Object, store.Object, new InlineLifecycleTransaction());

        var result = await service.RotateCredentialsAsync(userId, account.Id,
            new ExchangeAccountCredentialSecret("replacement-key", "replacement-secret"));

        result.Outcome.Should().Be(ExchangeAccountCredentialRotationOutcome.PermissionsRejected);
        result.Account.Should().BeNull();
        // No RotateAsync/SaveAsync setup on the strict mocks: this proves no persistence mutation happened.
    }

    [Fact]
    public async Task RotateCredentialsAsync_WhenAccountVersionChangedDuringVerification_ThrowsConcurrencyConflict()
    {
        var userId = UserId.New();
        var account = ExchangeAccount.Create(ExchangeAccountId.New(), userId, ExchangeId.Bybit, ProviderIdentity,
            connectionStatus: ExchangeAccountConnectionStatus.Connected, capabilities: RequiredCapabilities);
        var initialVersion = ConcurrencyVersion.Initial;
        var changedVersion = initialVersion.Next();
        var (repository, store, verifier) = CreateStrictMocks();
        repository.SetupSequence(value => value.GetByIdAsync(userId, account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Versioned<ExchangeAccount>(account, initialVersion))
            .ReturnsAsync(new Versioned<ExchangeAccount>(account, changedVersion));
        store.Setup(value => value.GetMetadataAsync(userId, account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExchangeAccountCredentialMetadata(initialVersion));
        verifier.Setup(value => value.VerifyAsync(ExchangeId.Bybit, It.IsAny<ExchangeAccountCredentialSecret>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExchangeAccountAccessVerificationResult.Verified(ProviderIdentity, RequiredCapabilities));
        var service = new ExchangeAccountService(verifier.Object, repository.Object, store.Object, new InlineLifecycleTransaction());

        var act = () => service.RotateCredentialsAsync(userId, account.Id,
            new ExchangeAccountCredentialSecret("replacement-key", "replacement-secret"));

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
        // No RotateAsync/SaveAsync setup on the strict mocks: this proves the write never happened.
    }

    [Fact]
    public async Task RotateCredentialsAsync_WhenCredentialVersionChangedDuringVerification_ThrowsConcurrencyConflict()
    {
        var userId = UserId.New();
        var account = ExchangeAccount.Create(ExchangeAccountId.New(), userId, ExchangeId.Bybit, ProviderIdentity,
            connectionStatus: ExchangeAccountConnectionStatus.Connected, capabilities: RequiredCapabilities);
        var version = ConcurrencyVersion.Initial;
        var initialCredentialVersion = ConcurrencyVersion.Initial;
        var changedCredentialVersion = initialCredentialVersion.Next();
        var (repository, store, verifier) = CreateStrictMocks();
        repository.Setup(value => value.GetByIdAsync(userId, account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Versioned<ExchangeAccount>(account, version));
        store.SetupSequence(value => value.GetMetadataAsync(userId, account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExchangeAccountCredentialMetadata(initialCredentialVersion))
            .ReturnsAsync(new ExchangeAccountCredentialMetadata(changedCredentialVersion));
        verifier.Setup(value => value.VerifyAsync(ExchangeId.Bybit, It.IsAny<ExchangeAccountCredentialSecret>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExchangeAccountAccessVerificationResult.Verified(ProviderIdentity, RequiredCapabilities));
        var service = new ExchangeAccountService(verifier.Object, repository.Object, store.Object, new InlineLifecycleTransaction());

        var act = () => service.RotateCredentialsAsync(userId, account.Id,
            new ExchangeAccountCredentialSecret("replacement-key", "replacement-secret"));

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    [Fact]
    public async Task RotateCredentialsAsync_WhenAccountWasDisabledDuringVerification_ThrowsConcurrencyConflictAndDoesNotReactivate()
    {
        var userId = UserId.New();
        var account = ExchangeAccount.Create(ExchangeAccountId.New(), userId, ExchangeId.Bybit, ProviderIdentity,
            connectionStatus: ExchangeAccountConnectionStatus.Connected, capabilities: RequiredCapabilities);
        var version = ConcurrencyVersion.Initial;
        var disabledAccount = ExchangeAccount.Create(account.Id, userId, ExchangeId.Bybit, ProviderIdentity,
            connectionStatus: ExchangeAccountConnectionStatus.Disabled, capabilities: RequiredCapabilities);
        var (repository, store, verifier) = CreateStrictMocks();
        repository.SetupSequence(value => value.GetByIdAsync(userId, account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Versioned<ExchangeAccount>(account, version))
            .ReturnsAsync(new Versioned<ExchangeAccount>(disabledAccount, version));
        store.Setup(value => value.GetMetadataAsync(userId, account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExchangeAccountCredentialMetadata(version));
        verifier.Setup(value => value.VerifyAsync(ExchangeId.Bybit, It.IsAny<ExchangeAccountCredentialSecret>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExchangeAccountAccessVerificationResult.Verified(ProviderIdentity, RequiredCapabilities));
        var service = new ExchangeAccountService(verifier.Object, repository.Object, store.Object, new InlineLifecycleTransaction());

        var act = () => service.RotateCredentialsAsync(userId, account.Id,
            new ExchangeAccountCredentialSecret("replacement-key", "replacement-secret"));

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
        // No RotateAsync/SaveAsync setup on the strict mocks: this proves the account was never reactivated.
    }

    private static async Task<ExchangeAccountVerificationResult> RunVerifyOutcomeAsync(
        ExchangeAccountAccessVerificationResult verification)
    {
        var userId = UserId.New();
        var account = ExchangeAccount.Create(ExchangeAccountId.New(), userId, ExchangeId.Bybit, ProviderIdentity,
            connectionStatus: ExchangeAccountConnectionStatus.Connected, capabilities: RequiredCapabilities);
        var version = ConcurrencyVersion.Initial;
        var (repository, store, verifier) = CreateStrictMocks();
        repository.Setup(value => value.GetByIdAsync(userId, account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Versioned<ExchangeAccount>(account, version));
        var credential = new ExchangeAccountCredential(new ExchangeAccountCredentialSecret("key", "secret"), version);
        store.Setup(value => value.GetAsync(userId, account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(credential);
        verifier.Setup(value => value.VerifyAsync(ExchangeId.Bybit, It.IsAny<ExchangeAccountCredentialSecret>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(verification);
        repository.Setup(value => value.SaveAsync(userId, It.IsAny<ExchangeAccount>(), version, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConcurrencyVersion(2));
        var service = new ExchangeAccountService(verifier.Object, repository.Object, store.Object, new InlineLifecycleTransaction());

        return await service.VerifyAsync(userId, account.Id);
    }

    private static (Mock<IExchangeAccountRepository> Repository, Mock<IExchangeAccountCredentialStore> Store,
        Mock<IExchangeAccountAccessVerifier> Verifier) CreateStrictMocks() =>
        (new Mock<IExchangeAccountRepository>(MockBehavior.Strict),
            new Mock<IExchangeAccountCredentialStore>(MockBehavior.Strict),
            new Mock<IExchangeAccountAccessVerifier>(MockBehavior.Strict));

    private sealed class InlineLifecycleTransaction : IExchangeAccountLifecycleTransaction
    {
        public bool Executed { get; private set; }
        public async Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
        {
            Executed = true;
            await operation(cancellationToken);
        }
    }

    private static readonly ExchangeAccountProviderIdentity ProviderIdentity =
        ExchangeAccountProviderIdentity.From("provider-account");
    private static readonly ExchangeAccountProviderIdentity OtherProviderIdentity =
        ExchangeAccountProviderIdentity.From("other-provider-account");
}
