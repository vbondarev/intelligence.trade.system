using System.Runtime.ExceptionServices;
using Intelligence.TradeSystem.Application.Accounts.Access;
using Intelligence.TradeSystem.Application.Accounts.Credentials;
using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Users;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Accounts;

public sealed class ExchangeAccountService(
    ICurrentUserContext currentUserContext,
    IExchangeAccountAccessVerifier accessVerifier,
    IExchangeAccountRepository repository,
    IExchangeAccountCredentialStore credentialStore,
    IExchangeAccountLifecycleTransaction? lifecycleTransaction = null)
    : IExchangeAccountService
{
    private const ExchangeAccountCapabilities RequiredCapabilities =
        ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions;

    public Task<ExchangeAccountConnectionResult> ConnectAsync(
        ExchangeId exchange,
        ExchangeAccountCredentialSecret credentials,
        CancellationToken cancellationToken = default) =>
        ConnectAsync(currentUserContext.UserId, exchange, credentials, cancellationToken);

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

        if (!HasRequiredCapabilities(verification.Capabilities))
            return ExchangeAccountConnectionResult.Failed(ExchangeAccountConnectionOutcome.PermissionsRejected);

        var account = ExchangeAccount.Create(ExchangeAccountId.New(), userId, exchange, capabilities: verification.Capabilities);
        var accountVersion = await repository.SaveAsync(userId, account, null, cancellationToken).ConfigureAwait(false);
        try
        {
            await credentialStore.CreateAsync(userId, account.Id, credentials, cancellationToken).ConfigureAwait(false);
            account.MarkConnected();
            await repository.SaveAsync(userId, account, accountVersion, cancellationToken).ConfigureAwait(false);
            return ExchangeAccountConnectionResult.Connected(account);
        }
        catch (Exception exception)
        {
            await CompensateFailedConnectionAsync(userId, account, accountVersion, exception).ConfigureAwait(false);
            throw;
        }
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
        if (outcome == ExchangeAccountVerificationOutcome.UnsupportedExchange)
            return new(outcome, null);

        if (outcome == ExchangeAccountVerificationOutcome.Succeeded)
            account.MarkConnected();
        else
            account.MarkUnavailable("Exchange credential verification failed.");

        await repository.SaveAsync(userId, account, loaded.Version, cancellationToken).ConfigureAwait(false);
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
        if (outcome != ExchangeAccountCredentialRotationOutcome.Succeeded)
            return new(outcome, null);

        ExchangeAccount? rotated = null;
        if (lifecycleTransaction is null)
            throw new InvalidOperationException("The exchange account lifecycle transaction is not configured.");
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
            rotated = currentAccount.Value;
        }, cancellationToken).ConfigureAwait(false);

        return new(ExchangeAccountCredentialRotationOutcome.Succeeded, rotated);
    }

    public async Task<ExchangeAccount?> DisconnectAsync(
        ExchangeAccountId exchangeAccountId,
        CancellationToken cancellationToken = default) =>
        await DisconnectAsync(currentUserContext.UserId, exchangeAccountId, cancellationToken)
            .ConfigureAwait(false);

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
            account.Disable();
            await repository.SaveAsync(userId, account, loaded.Version, cancellationToken).ConfigureAwait(false);
        }

        if (metadata is not null)
            await credentialStore.RevokeAsync(userId, exchangeAccountId, metadata.Version, cancellationToken).ConfigureAwait(false);
        return account;
    }

    private static bool HasRequiredCapabilities(ExchangeAccountCapabilities capabilities) =>
        (capabilities & RequiredCapabilities) == RequiredCapabilities;

    private static ExchangeAccountVerificationOutcome ToVerificationOutcome(
        ExchangeAccountAccessVerificationResult verification) =>
        verification.Status switch
        {
            ExchangeAccountAccessVerificationStatus.Verified when HasRequiredCapabilities(verification.Capabilities) =>
                ExchangeAccountVerificationOutcome.Succeeded,
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

    private async Task CompensateFailedConnectionAsync(UserId userId, ExchangeAccount account,
        ConcurrencyVersion accountVersion, Exception originalException)
    {
        var failures = new List<Exception>();
        try
        {
            var credential = await credentialStore.GetAsync(userId, account.Id, CancellationToken.None).ConfigureAwait(false);
            if (credential is not null)
                await credentialStore.RevokeAsync(userId, account.Id, credential.Version, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) { failures.Add(exception); }
        try { await repository.DeleteAsync(userId, account.Id, accountVersion, CancellationToken.None).ConfigureAwait(false); }
        catch (Exception exception) { failures.Add(exception); }
        if (failures.Count == 0)
            ExceptionDispatchInfo.Capture(originalException).Throw();
        throw new AggregateException("The exchange account operation failed and its compensation also failed.", [originalException, .. failures]);
    }
}
