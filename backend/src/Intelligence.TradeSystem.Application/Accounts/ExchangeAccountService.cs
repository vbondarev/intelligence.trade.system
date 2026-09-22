using Intelligence.TradeSystem.Application.Accounts.Access;
using Intelligence.TradeSystem.Application.Accounts.Credentials;
using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Events;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Accounts;

public sealed class ExchangeAccountService(
    IExchangeAccountAccessVerifier accessVerifier,
    IExchangeAccountRepository repository,
    IExchangeAccountCredentialStore credentialStore,
    IExchangeAccountLifecycleTransaction lifecycleTransaction,
    IApplicationEventOutbox applicationEventOutbox,
    TimeProvider? timeProvider = null)
    : IExchangeAccountService
{
    private const ExchangeAccountCapabilities RequiredCapabilities =
        ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions;
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<IReadOnlyList<ExchangeAccount>> ListActiveAsync(
        UserId userId,
        CancellationToken cancellationToken = default) =>
        (await repository.ListActiveAsync(userId, cancellationToken).ConfigureAwait(false))
        .Select(account => account.Value)
        .ToArray();

    public async Task<ExchangeAccountConnectionResult> ConnectAsync(
        UserId userId,
        ExchangeId exchange,
        ExchangeAccountCredentialSecret credentials,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        var verification = await accessVerifier.VerifyAsync(exchange, credentials, cancellationToken)
            .ConfigureAwait(false);
        if (verification.Status != ExchangeAccountAccessVerificationStatus.Verified)
        {
            return ExchangeAccountConnectionResult.Failed(verification.Status switch
            {
                ExchangeAccountAccessVerificationStatus.InvalidCredentials => ExchangeAccountConnectionOutcome.InvalidCredentials,
                ExchangeAccountAccessVerificationStatus.PermissionsRejected => ExchangeAccountConnectionOutcome.PermissionsRejected,
                ExchangeAccountAccessVerificationStatus.UnsupportedExchange => ExchangeAccountConnectionOutcome.UnsupportedExchange,
                _ => ExchangeAccountConnectionOutcome.Unavailable,
            });
        }

        if (verification.ProviderIdentity is not { } providerIdentity)
            return ExchangeAccountConnectionResult.Failed(ExchangeAccountConnectionOutcome.Unavailable);

        if (!HasRequiredCapabilities(verification.Capabilities))
            return ExchangeAccountConnectionResult.Failed(ExchangeAccountConnectionOutcome.PermissionsRejected);

        ExchangeAccount? connectedAccount = null;
        await lifecycleTransaction.ExecuteAsync(
            async transactionToken =>
            {
                var account = ExchangeAccount.Create(
                    ExchangeAccountId.New(),
                    userId,
                    exchange,
                    providerIdentity,
                    capabilities: verification.Capabilities);
                var accountVersion = await repository
                    .SaveAsync(userId, account, null, transactionToken)
                    .ConfigureAwait(false);
                await credentialStore
                    .CreateAsync(userId, account.Id, credentials, transactionToken)
                    .ConfigureAwait(false);
                account.MarkConnected();
                await repository
                    .SaveAsync(userId, account, accountVersion, transactionToken)
                    .ConfigureAwait(false);
                await applicationEventOutbox
                    .AddAsync(
                        new ExchangeAccountUpdatedEventV1(
                            Guid.NewGuid(),
                            clock.GetUtcNow(),
                            userId.Value,
                            account.Id.Value),
                        transactionToken)
                    .ConfigureAwait(false);
                connectedAccount = account;
            },
            cancellationToken).ConfigureAwait(false);

        return ExchangeAccountConnectionResult.Connected(
            connectedAccount ?? throw new InvalidOperationException(
                "The account lifecycle transaction completed without an account."));
    }

    public async Task<ExchangeAccountVerificationResult> VerifyAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        CancellationToken cancellationToken = default)
    {
        var loaded = await repository.GetByIdAsync(userId, exchangeAccountId, cancellationToken).ConfigureAwait(false);
        if (loaded is null)
            return new(ExchangeAccountVerificationOutcome.NotFound, null);
        var account = loaded.Value;
        if (account.ConnectionStatus == ExchangeAccountConnectionStatus.Disabled)
            return new(ExchangeAccountVerificationOutcome.AccountDisabled, null);

        ExchangeAccountCredential? credential;
        try
        {
            credential = await credentialStore.GetAsync(userId, exchangeAccountId, cancellationToken).ConfigureAwait(false);
        }
        catch (ExchangeAccountCredentialsUnavailableException)
        {
            return new(ExchangeAccountVerificationOutcome.CredentialsUnavailable, null);
        }

        if (credential is null)
            return new(ExchangeAccountVerificationOutcome.CredentialsUnavailable, null);

        var verification = await accessVerifier.VerifyAsync(account.ExchangeId, credential.Use((key, secret) =>
            new ExchangeAccountCredentialSecret(key, secret)), cancellationToken).ConfigureAwait(false);
        var outcome = ToVerificationOutcome(verification);
        if (outcome == ExchangeAccountVerificationOutcome.Succeeded &&
            verification.ProviderIdentity != account.ProviderIdentity)
        {
            outcome = ExchangeAccountVerificationOutcome.ProviderIdentityMismatch;
        }
        if (outcome == ExchangeAccountVerificationOutcome.UnsupportedExchange)
            return new(outcome, null);

        var previousState = AccountState.Capture(account);
        if (outcome == ExchangeAccountVerificationOutcome.Succeeded)
            account.MarkConnected();
        else
            account.MarkUnavailable("Exchange credential verification failed.");

        if (!previousState.Equals(AccountState.Capture(account)))
        {
            await lifecycleTransaction.ExecuteAsync(
                async transactionToken =>
                {
                    await repository
                        .SaveAsync(userId, account, loaded.Version, transactionToken)
                        .ConfigureAwait(false);
                    await applicationEventOutbox
                        .AddAsync(
                            new ExchangeAccountUpdatedEventV1(
                                Guid.NewGuid(),
                                clock.GetUtcNow(),
                                userId.Value,
                                account.Id.Value),
                            transactionToken)
                        .ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);
        }

        return new(outcome, account);
    }

    public async Task<ExchangeAccountCredentialRotationResult> RotateCredentialsAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        ExchangeAccountCredentialSecret replacement,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        var initialAccount = await repository.GetByIdAsync(userId, exchangeAccountId, cancellationToken).ConfigureAwait(false);
        if (initialAccount is null)
            return new(ExchangeAccountCredentialRotationOutcome.NotFound, null);
        if (initialAccount.Value.ConnectionStatus == ExchangeAccountConnectionStatus.Disabled)
            return new(ExchangeAccountCredentialRotationOutcome.AccountDisabled, null);
        var initialCredential = await credentialStore.GetMetadataAsync(userId, exchangeAccountId, cancellationToken).ConfigureAwait(false);
        if (initialCredential is null)
            return new(ExchangeAccountCredentialRotationOutcome.CredentialsUnavailable, null);

        var verification = await accessVerifier.VerifyAsync(initialAccount.Value.ExchangeId, replacement, cancellationToken)
            .ConfigureAwait(false);
        var outcome = ToRotationOutcome(verification);
        if (outcome == ExchangeAccountCredentialRotationOutcome.Succeeded &&
            verification.ProviderIdentity != initialAccount.Value.ProviderIdentity)
        {
            return new(ExchangeAccountCredentialRotationOutcome.ProviderIdentityMismatch, null);
        }
        if (outcome != ExchangeAccountCredentialRotationOutcome.Succeeded)
            return new(outcome, null);

        ExchangeAccount? rotated = null;
        await lifecycleTransaction.ExecuteAsync(async transactionToken =>
        {
            var currentAccount = await repository.GetByIdAsync(userId, exchangeAccountId, transactionToken).ConfigureAwait(false);
            var currentCredential = await credentialStore.GetMetadataAsync(userId, exchangeAccountId, transactionToken).ConfigureAwait(false);
            if (currentAccount is null || currentCredential is null ||
                currentAccount.Version != initialAccount.Version ||
                currentCredential.Version != initialCredential.Version)
            {
                throw new ConcurrencyConflictException("The exchange account was modified during credential verification.");
            }

            if (currentAccount.Value.ConnectionStatus == ExchangeAccountConnectionStatus.Disabled)
                throw new ConcurrencyConflictException("The exchange account was disabled during credential verification.");

            await credentialStore.RotateAsync(userId, exchangeAccountId, initialCredential.Version, replacement, transactionToken)
                .ConfigureAwait(false);
            currentAccount.Value.MarkConnected();
            await repository.SaveAsync(userId, currentAccount.Value, initialAccount.Version, transactionToken)
                .ConfigureAwait(false);
            await applicationEventOutbox
                .AddAsync(
                    new ExchangeAccountUpdatedEventV1(
                        Guid.NewGuid(),
                        clock.GetUtcNow(),
                        userId.Value,
                        exchangeAccountId.Value),
                    transactionToken)
                .ConfigureAwait(false);
            rotated = currentAccount.Value;
        }, cancellationToken).ConfigureAwait(false);

        return new(ExchangeAccountCredentialRotationOutcome.Succeeded, rotated);
    }

    public async Task<ExchangeAccount?> DisconnectAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        CancellationToken cancellationToken = default)
    {
        var loaded = await repository.GetByIdAsync(userId, exchangeAccountId, cancellationToken).ConfigureAwait(false);
        if (loaded is null)
            return null;
        var account = loaded.Value;
        var metadata = await credentialStore.GetMetadataAsync(userId, exchangeAccountId, cancellationToken).ConfigureAwait(false);
        if (account.ConnectionStatus != ExchangeAccountConnectionStatus.Disabled)
        {
            await lifecycleTransaction.ExecuteAsync(
                async transactionToken =>
                {
                    account.Disable();
                    await repository
                        .SaveAsync(userId, account, loaded.Version, transactionToken)
                        .ConfigureAwait(false);

                    if (metadata is not null)
                    {
                        await credentialStore
                            .RevokeAsync(
                                userId,
                                exchangeAccountId,
                                metadata.Version,
                                transactionToken)
                            .ConfigureAwait(false);
                    }

                    await applicationEventOutbox
                        .AddAsync(
                            new ExchangeAccountUpdatedEventV1(
                                Guid.NewGuid(),
                                clock.GetUtcNow(),
                                userId.Value,
                                exchangeAccountId.Value),
                            transactionToken)
                        .ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);
        }
        else if (metadata is not null)
        {
            await lifecycleTransaction.ExecuteAsync(
                transactionToken => credentialStore.RevokeAsync(
                    userId,
                    exchangeAccountId,
                    metadata.Version,
                    transactionToken),
                cancellationToken).ConfigureAwait(false);
        }

        return account;
    }

    private static bool HasRequiredCapabilities(ExchangeAccountCapabilities capabilities) =>
        (capabilities & RequiredCapabilities) == RequiredCapabilities;

    private static ExchangeAccountVerificationOutcome ToVerificationOutcome(
        ExchangeAccountAccessVerificationResult verification) =>
        verification.Status switch
        {
            ExchangeAccountAccessVerificationStatus.Verified
                when verification.ProviderIdentity is not null &&
                     HasRequiredCapabilities(verification.Capabilities) =>
                ExchangeAccountVerificationOutcome.Succeeded,
            ExchangeAccountAccessVerificationStatus.Verified =>
                HasRequiredCapabilities(verification.Capabilities)
                    ? ExchangeAccountVerificationOutcome.ExchangeUnavailable
                    : ExchangeAccountVerificationOutcome.PermissionsRejected,
            ExchangeAccountAccessVerificationStatus.InvalidCredentials =>
                ExchangeAccountVerificationOutcome.InvalidCredentials,
            ExchangeAccountAccessVerificationStatus.Unavailable =>
                ExchangeAccountVerificationOutcome.ExchangeUnavailable,
            ExchangeAccountAccessVerificationStatus.UnsupportedExchange =>
                ExchangeAccountVerificationOutcome.UnsupportedExchange,
            _ => ExchangeAccountVerificationOutcome.PermissionsRejected,
        };

    private static ExchangeAccountCredentialRotationOutcome ToRotationOutcome(
        ExchangeAccountAccessVerificationResult verification) =>
        ToVerificationOutcome(verification) switch
        {
            ExchangeAccountVerificationOutcome.Succeeded => ExchangeAccountCredentialRotationOutcome.Succeeded,
            ExchangeAccountVerificationOutcome.InvalidCredentials => ExchangeAccountCredentialRotationOutcome.InvalidCredentials,
            ExchangeAccountVerificationOutcome.PermissionsRejected => ExchangeAccountCredentialRotationOutcome.PermissionsRejected,
            ExchangeAccountVerificationOutcome.ExchangeUnavailable => ExchangeAccountCredentialRotationOutcome.ExchangeUnavailable,
            _ => ExchangeAccountCredentialRotationOutcome.UnsupportedExchange,
        };

    private readonly record struct AccountState(
        ExchangeAccountConnectionStatus ConnectionStatus,
        DateTimeOffset? LastSyncedAt,
        string? LastError,
        DateTimeOffset? LastAppliedBalanceObservationAt,
        DateTimeOffset? LastAppliedPositionsObservationAt)
    {
        public static AccountState Capture(ExchangeAccount account) =>
            new(
                account.ConnectionStatus,
                account.LastSyncedAt,
                account.LastError,
                account.LastAppliedBalanceObservationAt,
                account.LastAppliedPositionsObservationAt);
    }
}
