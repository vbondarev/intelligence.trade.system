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
    IExchangeAccountCredentialStore credentialStore)
    : IExchangeAccountService
{
    public async Task<ExchangeAccountConnectionResult> ConnectAsync(
        ExchangeId exchange,
        ExchangeAccountCredentialSecret credentials,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        var userId = currentUserContext.UserId;
        var verification = await accessVerifier
            .VerifyAsync(exchange, credentials, cancellationToken)
            .ConfigureAwait(false);

        if (verification.Status != ExchangeAccountAccessVerificationStatus.Verified)
        {
            return new(
                verification.Status switch
                {
                    ExchangeAccountAccessVerificationStatus.InvalidCredentials =>
                        ExchangeAccountConnectionOutcome.InvalidCredentials,
                    ExchangeAccountAccessVerificationStatus.PermissionsRejected =>
                        ExchangeAccountConnectionOutcome.PermissionsRejected,
                    ExchangeAccountAccessVerificationStatus.UnsupportedExchange =>
                        ExchangeAccountConnectionOutcome.UnsupportedExchange,
                    _ => ExchangeAccountConnectionOutcome.Unavailable,
                },
                null);
        }

        const ExchangeAccountCapabilities requiredCapabilities =
            ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions;
        if ((verification.Capabilities & requiredCapabilities) != requiredCapabilities)
        {
            return ExchangeAccountConnectionResult.Failed(
                ExchangeAccountConnectionOutcome.PermissionsRejected);
        }

        var account = ExchangeAccount.Create(
            ExchangeAccountId.New(),
            userId,
            exchange,
            capabilities: verification.Capabilities);
        var accountVersion = await repository
            .SaveAsync(userId, account, expectedVersion: null, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await credentialStore
                .CreateAsync(userId, account.Id, credentials, cancellationToken)
                .ConfigureAwait(false);

            account.MarkConnected();
            await repository
                .SaveAsync(userId, account, accountVersion, cancellationToken)
                .ConfigureAwait(false);

            return ExchangeAccountConnectionResult.Connected(account);
        }
        catch (Exception exception)
        {
            await CompensateFailedConnectionAsync(userId, account, accountVersion, exception)
                .ConfigureAwait(false);
            throw;
        }
    }

    public async Task<ExchangeAccount?> DisconnectAsync(
        ExchangeAccountId exchangeAccountId,
        CancellationToken cancellationToken = default)
    {
        if (exchangeAccountId == default)
        {
            throw new ArgumentException(
                "ExchangeAccountId must be initialized.",
                nameof(exchangeAccountId));
        }

        var userId = currentUserContext.UserId;
        var loaded = await repository
            .GetByIdAsync(userId, exchangeAccountId, cancellationToken)
            .ConfigureAwait(false);

        if (loaded is null)
        {
            return null;
        }

        var account = loaded.Value;
        var credential = await credentialStore
            .GetAsync(userId, exchangeAccountId, cancellationToken)
            .ConfigureAwait(false);

        if (account.ConnectionStatus == ExchangeAccountConnectionStatus.Disabled)
        {
            if (credential is not null)
            {
                await credentialStore
                    .RevokeAsync(userId, exchangeAccountId, credential.Version, cancellationToken)
                    .ConfigureAwait(false);
            }

            return account;
        }

        if (credential is null)
        {
            account.Disable();
            await repository
                .SaveAsync(userId, account, loaded.Version, cancellationToken)
                .ConfigureAwait(false);
            return account;
        }

        var restorationSecret = CopySecret(credential);
        await credentialStore
            .RevokeAsync(userId, exchangeAccountId, credential.Version, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            account.Disable();
            await repository
                .SaveAsync(userId, account, loaded.Version, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await RestoreCredentialsAfterDisconnectFailureAsync(
                    userId,
                    exchangeAccountId,
                    restorationSecret,
                    exception)
                .ConfigureAwait(false);
            throw;
        }

        return account;
    }

    private async Task CompensateFailedConnectionAsync(
        UserId userId,
        ExchangeAccount account,
        ConcurrencyVersion accountVersion,
        Exception originalException)
    {
        var compensationFailures = new List<Exception>();

        try
        {
            var persistedCredential = await credentialStore
                .GetAsync(userId, account.Id, CancellationToken.None)
                .ConfigureAwait(false);
            if (persistedCredential is not null)
            {
                await credentialStore
                    .RevokeAsync(
                        userId,
                        account.Id,
                        persistedCredential.Version,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            compensationFailures.Add(exception);
        }

        try
        {
            await repository
                .DeleteAsync(userId, account.Id, accountVersion, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            compensationFailures.Add(exception);
        }

        RethrowWithCompensationFailures(originalException, compensationFailures);
    }

    private async Task RestoreCredentialsAfterDisconnectFailureAsync(
        UserId userId,
        ExchangeAccountId accountId,
        ExchangeAccountCredentialSecret secret,
        Exception originalException)
    {
        var compensationFailures = new List<Exception>();

        try
        {
            await credentialStore
                .CreateAsync(userId, accountId, secret, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            compensationFailures.Add(exception);
        }

        RethrowWithCompensationFailures(originalException, compensationFailures);
    }

    private static ExchangeAccountCredentialSecret CopySecret(ExchangeAccountCredential credential)
    {
        ExchangeAccountCredentialSecret copy = null!;
        credential.Use((apiKey, apiSecret) =>
            copy = new ExchangeAccountCredentialSecret(apiKey, apiSecret));
        return copy;
    }

    private static void RethrowWithCompensationFailures(
        Exception originalException,
        List<Exception> compensationFailures)
    {
        if (compensationFailures.Count == 0)
        {
            ExceptionDispatchInfo.Capture(originalException).Throw();
        }

        throw new AggregateException(
            "The exchange account operation failed and its compensation also failed.",
            [originalException, .. compensationFailures]);
    }
}
